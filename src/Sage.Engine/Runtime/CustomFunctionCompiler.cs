// Copyright (c) 2022, salesforce.com, inc.
// All rights reserved.
// SPDX-License-Identifier: Apache-2.0
// For full license text, see the LICENSE file in the repo root or https://opensource.org/licenses/Apache-2.0

using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Basic.Reference.Assemblies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Sage.Engine.Data;

namespace Sage.Engine.Runtime
{
    /// <summary>
    /// Compiles the "csharp" bodies authored in custom-functions.json into real, callable methods, using the
    /// same Roslyn compilation approach the engine uses to compile AMPscript itself.
    /// </summary>
    internal static class CustomFunctionCompiler
    {
        private const string GeneratedNamespace = "Sage.Engine.Runtime.Generated";
        private const string GeneratedClassName = "CustomFunctionImplementations";
        private const string GeneratedFullTypeName = $"{GeneratedNamespace}.{GeneratedClassName}";

        /// <summary>
        /// Compiles every definition with a "csharp" body into a method taking
        /// (RuntimeContext context, object?[] args), and returns them keyed by function name.
        /// </summary>
        /// <exception cref="GenerateCodeException">When a custom function body fails to compile</exception>
        public static Dictionary<string, MethodInfo> Compile(
            IReadOnlyDictionary<string, CustomFunctionDefinition> definitions)
        {
            var result = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);

            var functionsWithCode = definitions
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.CSharp))
                .ToList();

            if (functionsWithCode.Count == 0)
            {
                return result;
            }

            string source = GenerateSource(functionsWithCode);
            Assembly assembly = CompileSource(source);

            Type type = assembly.GetType(GeneratedFullTypeName)
                ?? throw new GenerateCodeException($"Unable to find generated type {GeneratedFullTypeName}");

            foreach (KeyValuePair<string, CustomFunctionDefinition> pair in functionsWithCode)
            {
                MethodInfo method = type.GetMethod(pair.Key, BindingFlags.Public | BindingFlags.Static)
                    ?? throw new GenerateCodeException($"Unable to find generated method for custom function '{pair.Key}'");
                result[pair.Key] = method;
            }

            return result;
        }

        private static string GenerateSource(IEnumerable<KeyValuePair<string, CustomFunctionDefinition>> functionsWithCode)
        {
            var builder = new StringBuilder();
            builder.AppendLine("using System;");
            builder.AppendLine("using System.Data;");
            builder.AppendLine("using System.Linq;");
            builder.AppendLine("using System.Globalization;");
            builder.AppendLine("using System.Collections.Generic;");
            builder.AppendLine("using Sage.Engine.Runtime;");
            builder.AppendLine($"namespace {GeneratedNamespace}");
            builder.AppendLine("{");
            builder.AppendLine($"public static class {GeneratedClassName}");
            builder.AppendLine("{");

            foreach (KeyValuePair<string, CustomFunctionDefinition> pair in functionsWithCode)
            {
                AppendMethod(builder, pair.Key, pair.Value);
            }

            builder.AppendLine("}");
            builder.AppendLine("}");

            return builder.ToString();
        }

        private static void AppendMethod(StringBuilder builder, string functionName, CustomFunctionDefinition definition)
        {
            builder.AppendLine($"public static object? {functionName}(RuntimeContext context, object?[] args)");
            builder.AppendLine("{");

            string[] parameters = definition.Parameters ?? Array.Empty<string>();
            for (int i = 0; i < parameters.Length; i++)
            {
                builder.AppendLine($"object? {parameters[i]} = args.Length > {i} ? args[{i}] : null;");
            }

            builder.AppendLine(definition.CSharp);

            // Guards against CS0161 when the authored body doesn't return on every path.
            builder.AppendLine("return null;");
            builder.AppendLine("}");
        }

        private static Assembly CompileSource(string source)
        {
            var references = Net80.References.All.ToList();
            references.Add(MetadataReference.CreateFromFile(typeof(RuntimeContext).Assembly.Location));
            references.Add(MetadataReference.CreateFromFile(typeof(IDataExtensionClient).Assembly.Location));

            CSharpCompilation compilation = CSharpCompilation.Create(
                "CustomFunctions",
                new[] { CSharpSyntaxTree.ParseText(source) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var assemblyStream = new MemoryStream();
            EmitResult emitResult = compilation.Emit(assemblyStream);

            if (!emitResult.Success)
            {
                string diagnostics = string.Join(
                    "\n",
                    emitResult.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.ToString()));

                throw new GenerateCodeException(
                    $"Failed to compile custom-functions.json. Check the \"csharp\" bodies for errors:\n{diagnostics}");
            }

            assemblyStream.Seek(0, SeekOrigin.Begin);
            var context = new AssemblyLoadContext("CustomFunctions", isCollectible: false);
            return context.LoadFromStream(assemblyStream);
        }
    }
}
