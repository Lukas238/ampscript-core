// Copyright (c) 2022, salesforce.com, inc.
// All rights reserved.
// SPDX-License-Identifier: Apache-2.0
// For full license text, see the LICENSE file in the repo root or https://opensource.org/licenses/Apache-2.0

using Microsoft.Extensions.Options;
using Microsoft.VisualBasic.FileIO;
using System.Text.Json;
using Sage.Engine.Compiler;
using Sage.Engine.Runtime;
using Sage.Webhost;

namespace Sage.Engine
{
    /// <summary>
    ///     Renders a request for the web.
    ///     This also handles what happens if there is an error - it will show the error page if configured to do so.
    /// </summary>
    public class WebRequestRenderer
    {
        private readonly SageWebhostOptions _config;
        private readonly IServiceProvider _provider;
        private readonly Renderer _renderer;

        public WebRequestRenderer(
            IServiceProvider serviceProvider,
            Renderer renderer,
            IOptionsSnapshot<SageWebhostOptions> configuration)
        {
            _provider = serviceProvider;
            _renderer = renderer;
            _config = configuration.Value;
        }

        public string RenderContent(CompilationOptions inputOption, int? subscriberRow = null)
        {
            try
            {
                SubscriberContext? context = null;
                string directory = Path.GetDirectoryName(inputOption.Content.Location)
                    ?? throw new InvalidOperationException("The source file must have a parent directory.");
                string subscriberContextFile = Path.Combine(directory, "subscriber.json");
                if (subscriberRow.HasValue)
                {
                    context = LoadSubscriberRow(directory, subscriberRow.Value);
                }
                else if (Path.Exists(subscriberContextFile))
                {
                    context = new SubscriberContext(File.ReadAllText(subscriberContextFile));
                }

                static SubscriberContext LoadSubscriberRow(string directory, int row)
                {
                    if (row < 1)
                    {
                        throw new BadHttpRequestException("Subscriber row must be a positive, 1-based number.");
                    }

                    string path = Path.Combine(directory, "DataExtensions", "_SUBSCRIBERS.csv");
                    if (!File.Exists(path))
                    {
                        throw new BadHttpRequestException("DataExtensions/_SUBSCRIBERS.csv was not found.", StatusCodes.Status404NotFound);
                    }

                    using var parser = new TextFieldParser(path)
                    {
                        TextFieldType = FieldType.Delimited,
                        TrimWhiteSpace = true,
                        CommentTokens = Array.Empty<string>()
                    };
                    parser.SetDelimiters(";");

                    try
                    {
                        string[]? headers = parser.ReadFields();
                        if (headers == null || headers.Any(string.IsNullOrWhiteSpace) ||
                            headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
                        {
                            throw new BadHttpRequestException("Subscriber CSV headers must be nonempty and unique.");
                        }

                        for (int index = 1; !parser.EndOfData; index++)
                        {
                            string[]? values = parser.ReadFields();
                            if (values == null || values.Length != headers.Length)
                            {
                                throw new BadHttpRequestException($"Subscriber CSV row {index} does not match the header column count.");
                            }

                            if (index == row)
                            {
                                var attributes = headers.Zip(values).ToDictionary(pair => pair.First, pair => pair.Second);
                                return new SubscriberContext(JsonSerializer.Serialize(attributes));
                            }
                        }
                    }
                    catch (MalformedLineException exception)
                    {
                        throw new BadHttpRequestException($"Malformed subscriber CSV at line {exception.LineNumber}.", exception);
                    }

                    throw new BadHttpRequestException($"Subscriber row {row} does not exist.", StatusCodes.Status404NotFound);
                }

                return _renderer.Render(inputOption, context);
            }
            catch (GenerateCodeException compileException)
            {
                if (!_config.CustomExceptionPage)
                {
                    throw;
                }

                using IServiceScope exceptionScope = _provider.CreateScope();

                CompilationOptions exceptionOptions =
                    exceptionScope.ServiceProvider.GetRequiredService<IOptionsSnapshot<CompilationOptions>>().Value;

                return exceptionScope.ServiceProvider.GetRequiredService<ExceptionPageRenderer>()
                    .Render(compileException, exceptionOptions);
            }
            catch (RuntimeException runtimeException)
            {
                if (!_config.CustomExceptionPage)
                {
                    throw;
                }

                using IServiceScope exceptionScope = _provider.CreateScope();

                CompilationOptions exceptionOptions =
                    exceptionScope.ServiceProvider.GetRequiredService<IOptionsSnapshot<CompilationOptions>>().Value;

                return exceptionScope.ServiceProvider.GetRequiredService<ExceptionPageRenderer>()
                    .Render(runtimeException, exceptionOptions);
            }
        }
    }
}
