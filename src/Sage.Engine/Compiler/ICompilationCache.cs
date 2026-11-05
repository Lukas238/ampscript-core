// Copyright (c) 2022, salesforce.com, inc.
// All rights reserved.
// SPDX-License-Identifier: Apache-2.0
// For full license text, see the LICENSE file in the repo root or https://opensource.org/licenses/Apache-2.0

namespace Sage.Engine.Compiler
{
    /// <summary>
    /// Interface for caching compiled AMPscript assemblies to avoid recompilation on repeated requests.
    /// </summary>
    public interface ICompilationCache
    {
        /// <summary>
        /// Attempts to retrieve a cached compilation result.
        /// </summary>
        /// <param name="sourceCodeHash">SHA256 hash of the AMPscript source code</param>
        /// <param name="generatedMethodName">Name of the generated method</param>
        /// <returns>Cached CompileResult if found; null otherwise</returns>
        CompileResult? TryGet(string sourceCodeHash, string generatedMethodName);

        /// <summary>
        /// Stores a compilation result in the cache.
        /// </summary>
        /// <param name="sourceCodeHash">SHA256 hash of the AMPscript source code</param>
        /// <param name="generatedMethodName">Name of the generated method</param>
        /// <param name="result">The compiled result to cache</param>
        void Set(string sourceCodeHash, string generatedMethodName, CompileResult result);

        /// <summary>
        /// Clears all cached entries.
        /// </summary>
        void Clear();
    }
}
