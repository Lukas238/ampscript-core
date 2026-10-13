// Copyright (c) 2022, salesforce.com, inc.
// All rights reserved.
// SPDX-License-Identifier: Apache-2.0
// For full license text, see the LICENSE file in the repo root or https://opensource.org/licenses/Apache-2.0

using System.Reflection;

namespace Sage.Engine.Runtime
{
    public partial class RuntimeContext
    {
        /// <summary>
        /// Fallback invoked by generated code for any AMPscript function call that doesn't match a native
        /// implementation on this class. Rather than failing compilation, this resolves a user-defined stub
        /// from <see cref="CustomFunctionRegistry"/> so unsupported functions can be patched locally.
        /// </summary>
        /// <param name="functionName">The AMPscript function name exactly as written in the source</param>
        /// <param name="arguments">The arguments the function was called with</param>
        /// <exception cref="RuntimeException">When no stub has been defined for the function</exception>
        public object? InvokeCustomFunction(string functionName, params object?[] arguments)
        {
            if (!_customFunctions.TryGetValue(functionName, out CustomFunctionDefinition? definition) || definition == null)
            {
                throw new RuntimeException(
                    $"'{functionName}' is not implemented by AMPscript Core. Add a stub for it to " +
                    $"{CustomFunctionRegistry.FileName} in the working directory, e.g.: " +
                    $"{{ \"{functionName}\": {{ \"return\": \"\" }} }}",
                    this,
                    functionName);
            }

            if (_customFunctions.TryGetCompiledFunction(functionName, out MethodInfo? method) && method != null)
            {
                try
                {
                    return method.Invoke(null, new object?[] { this, arguments });
                }
                catch (TargetInvocationException e) when (e.InnerException != null)
                {
                    throw e.InnerException;
                }
            }

            if (definition.EchoArgument is int echoIndex)
            {
                return echoIndex >= 0 && echoIndex < arguments.Length ? arguments[echoIndex] : null;
            }

            return definition.Return ?? string.Empty;
        }
    }
}
