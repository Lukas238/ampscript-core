// Copyright (c) 2022, salesforce.com, inc.
// All rights reserved.
// SPDX-License-Identifier: Apache-2.0
// For full license text, see the LICENSE file in the repo root or https://opensource.org/licenses/Apache-2.0

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sage.Engine.Runtime
{
    /// <summary>
    /// A user-authored stand-in for an AMPscript function that AMPscript Core doesn't natively implement.
    ///
    /// Set <see cref="CSharp"/> for real, parameterized logic, or use <see cref="Return"/>/<see cref="EchoArgument"/>
    /// for a simple static stub.
    /// </summary>
    public class CustomFunctionDefinition
    {
        /// <summary>
        /// A fixed literal value returned every time the function is called.
        /// </summary>
        [JsonPropertyName("return")]
        public string? Return { get; set; }

        /// <summary>
        /// The zero-based index of the argument that's returned unchanged, instead of a fixed value.
        /// </summary>
        [JsonPropertyName("echoArgument")]
        public int? EchoArgument { get; set; }

        /// <summary>
        /// Names given to the call's arguments (by position), so <see cref="CSharp"/> can reference them by name
        /// instead of indexing into the raw argument array.
        /// </summary>
        [JsonPropertyName("parameters")]
        public string[]? Parameters { get; set; }

        /// <summary>
        /// A C# method body implementing the function's real behavior. Has access to the named
        /// <see cref="Parameters"/>, the raw "args" array, "context" (the current <see cref="RuntimeContext"/>),
        /// and <see cref="SageValue"/> helpers. Compiled once, the same way the engine compiles AMPscript itself.
        /// </summary>
        [JsonPropertyName("csharp")]
        public string? CSharp { get; set; }
    }

    /// <summary>
    /// Loads user-defined stub implementations for unsupported AMPscript functions from
    /// "custom-functions.json" in the working directory, so that local debugging doesn't abort with a
    /// compile error the first time an unimplemented function is called.
    ///
    /// Example "custom-functions.json":
    /// <code>
    /// {
    ///   "FORMATDATE": { "return": "2026-01-01" },
    ///   "DATEADD": {
    ///     "parameters": ["date", "interval", "part"],
    ///     "csharp": "var d = SageValue.ToDateTime(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal); return d.AddDays((double)SageValue.ToDecimal(interval)).ToString();"
    ///   }
    /// }
    /// </code>
    /// </summary>
    public class CustomFunctionRegistry
    {
        public const string FileName = "custom-functions.json";

        private readonly Dictionary<string, CustomFunctionDefinition> _definitions;
        private readonly Dictionary<string, MethodInfo> _compiledFunctions;

        public CustomFunctionRegistry(DirectoryInfo? workingPath)
        {
            _definitions = Load(workingPath);
            _compiledFunctions = CustomFunctionCompiler.Compile(_definitions);
        }

        private static Dictionary<string, CustomFunctionDefinition> Load(DirectoryInfo? workingPath)
        {
            var result = new Dictionary<string, CustomFunctionDefinition>(StringComparer.OrdinalIgnoreCase);

            FileInfo? file = workingPath == null ? null : new FileInfo(Path.Combine(workingPath.FullName, FileName));
            if (file == null || !file.Exists)
            {
                return result;
            }

            string json = File.ReadAllText(file.FullName);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, CustomFunctionDefinition>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (parsed != null)
            {
                foreach (KeyValuePair<string, CustomFunctionDefinition> pair in parsed)
                {
                    result[pair.Key] = pair.Value;
                }
            }

            return result;
        }

        /// <summary>
        /// Looks up a user-defined stub for the given AMPscript function name (case-insensitive).
        /// </summary>
        public bool TryGetValue(string functionName, out CustomFunctionDefinition? definition)
        {
            return _definitions.TryGetValue(functionName, out definition);
        }

        /// <summary>
        /// Looks up the compiled method backing a function's "csharp" body, if it has one.
        /// </summary>
        public bool TryGetCompiledFunction(string functionName, out MethodInfo? method)
        {
            return _compiledFunctions.TryGetValue(functionName, out method);
        }
    }
}

