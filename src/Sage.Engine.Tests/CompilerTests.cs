// Copyright (c) 2022, salesforce.com, inc.
// All rights reserved.
// SPDX-License-Identifier: Apache-2.0
// For full license text, see the LICENSE file in the repo root or https://opensource.org/licenses/Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Sage.Engine.Runtime;

namespace Sage.Engine.Tests
{
    using NUnit.Framework;
    using Compiler;

    /// <summary>
    /// These tests validate that the ANTLR4 generated parse tree is what is cSharpExpected from test files
    /// </summary>
    public class CompilerTests : SageTest
    {
        [Test]
        [TestCase("TestAssemblyGeneration.ampscript")]
        public void TestAssemblyGeneration(string sourceFile)
        {
            string pathToTest = Path.Combine(TestContext.CurrentContext.TestDirectory, "Corpus", "Compiler", sourceFile);

            Compiler.CompilationOptions options = new CompilerOptionsBuilder()
                .WithContent(new LocalFileContent(pathToTest, 1))
                .Build();

            CompileResult result = CSharpCompiler.GenerateAssemblyFromSource(options);

            if (!result.EmitResult.Success)
            {
                Assert.Fail(string.Join("\n", result.EmitResult.Diagnostics.Select(d => d.ToString())));
            }

            Assert.That(result.Assembly, Is.Not.Null);

            var context = new RuntimeContext(_serviceProvider, options);
            object[] variables = new object[] { context };
            result.Assembly
                ?.GetType("Sage.Engine.Runtime.AmpProgram")
                ?.GetMethod(options.GeneratedMethodName)
                ?.Invoke(null, variables);
            Assert.That(context.PopContext(), Is.EqualTo("Hello  World"));
        }

        [Test]
        [TestCase("%%=ADD(1,2)=%%", ContentType.Handlebars, "%%=ADD(1,2)=%%")]
        [TestCase("%%=ADD(1,2)=%%", ContentType.AMPscript, "3")]
        [TestCase("{{#if true}}Hello{{/if}}", ContentType.AMPscript, "")]
        [TestCase("{{#if true}}Hello{{/if}}", ContentType.Handlebars, "Hello")]
        public void TestRenderer(string input, ContentType type, string expected)
        {
            var content = new EmbeddedContent(input, "TEST", "TEST", 1, type);
            CompilationOptions options = new CompilerOptionsBuilder()
                .WithContent(content)
                .Build();
            var result = _serviceProvider.GetRequiredService<Renderer>().Render(options);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase("DATEADD", "{ \"DATEADD\": { \"echoArgument\": 0 } }", "%%=DATEADD(\"hi\",1,\"D\")=%%", "hi")]
        [TestCase("FORMATDATE", "{ \"FORMATDATE\": { \"return\": \"2026-01-01\" } }", "%%=FORMATDATE(NOW())=%%", "2026-01-01")]
        public void TestCustomFunctionStub(string functionName, string customFunctionsJson, string input, string expected)
        {
            string jsonPath = Path.Combine(TestContext.CurrentContext.TestDirectory, CustomFunctionRegistry.FileName);
            File.WriteAllText(jsonPath, customFunctionsJson);
            try
            {
                var content = new EmbeddedContent(input, "TEST", "TEST", 1, ContentType.AMPscript);
                CompilationOptions options = new CompilerOptionsBuilder().WithContent(content).Build();
                var result = _serviceProvider.GetRequiredService<Renderer>().Render(options);
                Assert.That(result, Is.EqualTo(expected));
            }
            finally
            {
                File.Delete(jsonPath);
            }
        }

        [Test]
        public void TestUndefinedCustomFunctionThrows()
        {
            var content = new EmbeddedContent("%%=SOMEUNSUPPORTEDFUNCTION(1)=%%", "TEST", "TEST", 1, ContentType.AMPscript);
            CompilationOptions options = new CompilerOptionsBuilder().WithContent(content).Build();

            Assert.Throws<RuntimeException>(() => _serviceProvider.GetRequiredService<Renderer>().Render(options));
        }

        [Test]
        public void TestCustomFunctionCSharpBody()
        {
            string customFunctionsJson = """
            {
              "DATEADD": {
                "parameters": ["date", "interval", "part"],
                "csharp": "var d = SageValue.ToDateTime(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal); return d.AddDays((double)SageValue.ToDecimal(interval)).ToString(\"yyyy-MM-dd\");"
              }
            }
            """;
            string jsonPath = Path.Combine(TestContext.CurrentContext.TestDirectory, CustomFunctionRegistry.FileName);
            File.WriteAllText(jsonPath, customFunctionsJson);
            try
            {
                var content = new EmbeddedContent("%%=DATEADD(\"2026-01-01\",5,\"D\")=%%", "TEST", "TEST", 1, ContentType.AMPscript);
                CompilationOptions options = new CompilerOptionsBuilder().WithContent(content).Build();
                var result = _serviceProvider.GetRequiredService<Renderer>().Render(options);
                Assert.That(result, Is.EqualTo("2026-01-06"));
            }
            finally
            {
                File.Delete(jsonPath);
            }
        }

        [Test]
        [TestCase("\"2026-06-27\"", "MMMM&nb\\sp;d,&nb\\sp;yyyy", "en", "June&nbsp;27,&nbsp;2026")]
        [TestCase("DATEPARSE(\"2026-06-27\", true)", "MM/dd/yyyy", "en", "06/27/2026")]
        public void TestFormatDate(string dateExpression, string formatString, string culture, string expected)
        {
            var content = new EmbeddedContent(
                $"%%=Format({dateExpression}, \"{formatString}\", \"Date\", \"{culture}\")=%%",
                "TEST", "TEST", 1, ContentType.AMPscript);
            CompilationOptions options = new CompilerOptionsBuilder().WithContent(content).Build();
            var result = _serviceProvider.GetRequiredService<Renderer>().Render(options);
            Assert.That(result, Is.EqualTo(expected));
        }
    }
}

