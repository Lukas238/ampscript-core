// Copyright (c) 2022, salesforce.com, inc.
// All rights reserved.
// SPDX-License-Identifier: Apache-2.0
// For full license text, see the LICENSE file in the repo root or https://opensource.org/licenses/Apache-2.0

using System.Reflection;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Sage.Engine.Transpiler;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using System.Runtime.Loader;
using Sage.Engine.Runtime;
using Basic.Reference.Assemblies;
using Sage.Engine.Data;

namespace Sage.Engine.Compiler
{
    /// <summary>
    /// A class that compiles AMPscript source code into C# and optionally will execute it.
    /// Includes built-in caching to avoid recompilation of unchanged source code.
    /// </summary>
    public static class CSharpCompiler
    {
        private static ICompilationCache? _cache;

        /// <summary>
        /// Sets the compilation cache to use. Call once during application startup.
        /// </summary>
        public static void SetCache(ICompilationCache cache)
        {
            _cache = cache;
        }

        public static string CompileAndExecute(CompilationOptions options, RuntimeContext context, out CompileResult result)
        {
            result = GenerateAssemblyFromSource(options);

            result.Execute(context);

            result.AssemblyContext!.Unload();
            options.AssemblyStream.Close();
            options.SymbolStream.Close();

            // This pops the original and only context.
            return context.PopContext();
        }

        /// <summary>
        /// Generates C# code from the provided AMPscript, WITH CACHING
        /// </summary>
        public static CompileResult GenerateAssemblyFromSource(CompilationOptions options)
        {
            string code = options.Content.GetTextReader().ReadToEnd();

            // Step 1: Compute hash of source code for cache key
            string sourceCodeHash = ComputeSourceCodeHash(code);

            // Step 2: Check cache first
            if (_cache?.TryGet(sourceCodeHash, options.GeneratedMethodName) is CompileResult cachedResult)
            {
                return cachedResult;
            }

            // Step 3: Not in cache - compile fresh
            CSharpTranspiler transpiler = CSharpTranspiler.CreateFromSource(options.Content.Location, options.GeneratedMethodName, code);

            CompilationUnitSyntax compilationUnit = transpiler.GenerateCode();

            // When there is whitespace in the C# code, the #line indentations don't work as expected.
            // I do not know why.
            string cSharpCode = compilationUnit.NormalizeWhitespace(indentation: string.Empty).ToString();

            (CSharpCompilation compilation, SourceText sourceText) = GenerateCompilation(options, cSharpCode);
            (EmitResult emitResult, Assembly? assembly, AssemblyLoadContext? assemblyContext) = GenerateAssembly(options, compilation, sourceText);

            var result = new CompileResult(
                options.Content.Location,
                options.GeneratedMethodName,
                compilationUnit,
                compilation,
                emitResult,
                assembly,
                assemblyContext,
                cSharpCode);

            // Step 4: Store in cache for future requests
            _cache?.Set(sourceCodeHash, options.GeneratedMethodName, result);

            return result;
        }

        /// <summary>
        /// Generates a CSharpCompilation from the provided source
        /// </summary>
        private static (CSharpCompilation, SourceText) GenerateCompilation(CompilationOptions options, string cSharpCode)
        {
            Encoding encoding = Encoding.UTF8;

            // Basic.Reference.Assemblies.Net70.References.All
            var metadataReference = Net80.References.All.ToList();
            metadataReference.Add(MetadataReference.CreateFromFile(typeof(RuntimeContext).Assembly.Location));
            metadataReference.Add(MetadataReference.CreateFromFile(typeof(IDataExtensionClient).Assembly.Location));

            IEnumerable<string> defaultNamespaces =
                new[]
                {
                    "System",
                    "System.Text",
                    "System.Collections.Generic"
                };

            // This entire round-about of going from CompilationUnitSyntax->String->SyntaxTree is to embed the source file
            // into the PDB and have it be debuggable.
            // https://stackoverflow.com/questions/50649795/how-to-debug-dll-generated-from-roslyn-compilation
            byte[] buffer = encoding.GetBytes(cSharpCode);
            var sourceText = SourceText.From(buffer, buffer.Length, encoding, canBeEmbedded: true);
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
                sourceText,
                new CSharpParseOptions());

            var syntaxRootNode = (CSharpSyntaxNode)syntaxTree.GetRoot();

            SyntaxTree encoded = CSharpSyntaxTree.Create(
                syntaxRootNode,
                path: $"{options.Content.Name}.generated.cs",
                encoding: encoding);

            var compilation = CSharpCompilation.Create(
                options.GeneratedAssemblyOutput.Name,
                syntaxTrees: new[] { encoded },
                references: metadataReference,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithUsings(defaultNamespaces)
                    .WithOptimizationLevel(options.OptimizationLevel));

            return (compilation, sourceText);
        }

        /// <summary>
        /// Writes the in-memory representation of the compilation to an exe on disk
        /// </summary>
        private static (EmitResult, Assembly?, AssemblyLoadContext?) GenerateAssembly(CompilationOptions options, CSharpCompilation compilation, SourceText sourceText)
        {
            var emitOptions = new EmitOptions(
                debugInformationFormat: DebugInformationFormat.PortablePdb);

            var embeddedTexts = new List<EmbeddedText>
            {
                EmbeddedText.FromSource($"{options.Content.Name}.generated.cs", sourceText)
            };

            EmitResult emitResult = compilation.Emit(
                peStream: options.AssemblyStream,
                pdbStream: options.SymbolStream,
                embeddedTexts: embeddedTexts,
                options: emitOptions);

            options.AssemblyStream.Seek(0, SeekOrigin.Begin);
            options.SymbolStream.Seek(0, SeekOrigin.Begin);

            if (emitResult.Success)
            {
                var context = new AssemblyLoadContext(options.Content.Location, true);

                Assembly assembly = context.LoadFromStream(options.AssemblyStream, options.SymbolStream);

                return (emitResult, assembly, context);
            }

            return (emitResult, null, null);
        }

        /// <summary>
        /// Computes a SHA256 hash of the source code for use as a cache key.
        /// </summary>
        private static string ComputeSourceCodeHash(string sourceCode)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(sourceCode));
                return Convert.ToBase64String(hashBytes);
            }
        }
    }
}
