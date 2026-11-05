// Copyright (c) 2022, salesforce.com, inc.
// All rights reserved.
// SPDX-License-Identifier: Apache-2.0
// For full license text, see the LICENSE file in the repo root or https://opensource.org/licenses/Apache-2.0

using Microsoft.Extensions.Caching.Memory;

namespace Sage.Engine.Compiler
{
    /// <summary>
    /// In-memory cache implementation for compiled AMPscript assemblies.
    /// Provides significant performance improvements by avoiding recompilation of unchanged source code.
    /// </summary>
    public class CompilationCache : ICompilationCache
    {
        private readonly IMemoryCache _cache;
        private const long MaxCacheSizeBytes = 100 * 1024 * 1024; // 100 MB default

        public CompilationCache(IMemoryCache cache)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        /// <summary>
        /// Attempts to retrieve a cached compilation result.
        /// </summary>
        public CompileResult? TryGet(string sourceCodeHash, string generatedMethodName)
        {
            if (string.IsNullOrEmpty(sourceCodeHash))
            {
                return null;
            }

            string cacheKey = BuildCacheKey(sourceCodeHash, generatedMethodName);
            return _cache.TryGetValue(cacheKey, out CompileResult? result) ? result : null;
        }

        /// <summary>
        /// Stores a compilation result in the cache with automatic eviction based on size and sliding expiration.
        /// </summary>
        public void Set(string sourceCodeHash, string generatedMethodName, CompileResult result)
        {
            if (string.IsNullOrEmpty(sourceCodeHash) || result == null)
            {
                return;
            }

            string cacheKey = BuildCacheKey(sourceCodeHash, generatedMethodName);
            
            // Estimate the size of the cached result (assembly stream + overhead)
            long estimatedSize = EstimateResultSize(result);

            var cacheEntryOptions = new MemoryCacheEntryOptions
            {
                // Size is used for eviction when cache exceeds SizeLimit
                Size = (long)Math.Min(estimatedSize, MaxCacheSizeBytes / 10), // Prevent single entry from taking all space
                
                // Sliding expiration: cache entry is evicted if not accessed for 30 minutes
                SlidingExpiration = TimeSpan.FromMinutes(30)
            };

            _cache.Set(cacheKey, result, cacheEntryOptions);
        }

        /// <summary>
        /// Clears all cached entries.
        /// </summary>
        public void Clear()
        {
            if (_cache is MemoryCache memCache)
            {
                memCache.Compact(1.0);
            }
        }

        /// <summary>
        /// Builds a unique cache key from source code hash and method name.
        /// </summary>
        private static string BuildCacheKey(string sourceCodeHash, string generatedMethodName)
        {
            return $"ampscript:compile:{sourceCodeHash}:{generatedMethodName}";
        }

        /// <summary>
        /// Estimates the memory size of a compilation result.
        /// This is a rough estimate based on the assembly and metadata.
        /// </summary>
        private static long EstimateResultSize(CompileResult result)
        {
            try
            {
                // Rough estimate: Assembly size + compilation metadata
                // Typical compiled AMPscript assembly is 50-500 KB
                if (result.Assembly != null)
                {
                    // GetModules()[0].GetModuleHandle().GetModuleSize() doesn't work reliably
                    // so we use a reasonable estimate
                    return 200_000; // 200 KB average estimate
                }
            }
            catch
            {
                // If we can't get size, use a reasonable default
            }

            // Default estimate if size can't be determined
            return 200_000; // 200 KB default estimate
        }
    }
}
