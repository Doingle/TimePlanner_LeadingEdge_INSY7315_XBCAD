using System.Net;
using TimePlanner.Core.Ai;
using Xunit;

namespace TimePlanner.Core.Tests.Ai
{
    //-----------------------------
    //unit tests for ollama setup checks and model download
    public class OllamaSetupTests
    {
        private readonly AiSettings _settings = new()
        {
            Compiler = CompilerKind.Ollama,
            CompilerModel = "qwen2.5:3b",
            CompilerUrl = "http://localhost:11434"
        };

        //-----------------------------
        //connection failure returns not running state
        [Fact]
        public async Task Check_NoServer_ReturnsNotRunning()
        {
            var handler = new FakeAiHttpHandler
            {
                ThrowConnectionRefused = true
            };

            var http = new HttpClient(handler);
            var setup = new OllamaSetup(http, _settings);

            var status = await setup.CheckAsync();

            Assert.Equal(OllamaState.NotRunning, status.State);
            Assert.Null(status.Version);
        }

        //-----------------------------
        //model listed in tags returns ready state
        [Fact]
        public async Task Check_ModelListed_ReturnsReady()
        {
            var handler = new FakeAiHttpHandler
            {
                Respond = req =>
                {
                    if (req.RequestUri!.PathAndQuery.Contains("api/version"))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"version\":\"0.5.1\"}")
                        };
                    }

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"models\":[{\"name\":\"qwen2.5:3b\"}]}")
                    };
                }
            };

            var http = new HttpClient(handler);
            var setup = new OllamaSetup(http, _settings);

            var status = await setup.CheckAsync();

            Assert.Equal(OllamaState.Ready, status.State);
            Assert.Equal("0.5.1", status.Version);
        }

        //-----------------------------
        //model missing from tags returns model missing state
        [Fact]
        public async Task Check_ModelNotListed_ReturnsModelMissing()
        {
            var handler = new FakeAiHttpHandler
            {
                Respond = req =>
                {
                    if (req.RequestUri!.PathAndQuery.Contains("api/version"))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"version\":\"0.5.1\"}")
                        };
                    }

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"models\":[{\"name\":\"llama3.2:1b\"}]}")
                    };
                }
            };

            var http = new HttpClient(handler);
            var setup = new OllamaSetup(http, _settings);

            var status = await setup.CheckAsync();

            Assert.Equal(OllamaState.ModelMissing, status.State);
            Assert.Equal("0.5.1", status.Version);
        }

        //-----------------------------
        //bare model name matches tag with latest
        [Fact]
        public async Task Check_BareNameMatchesLatest()
        {
            var settings = new AiSettings
            {
                Compiler = CompilerKind.Ollama,
                CompilerModel = "qwen2.5",
                CompilerUrl = "http://localhost:11434"
            };

            var handler = new FakeAiHttpHandler
            {
                Respond = req =>
                {
                    if (req.RequestUri!.PathAndQuery.Contains("api/version"))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"version\":\"0.5.1\"}")
                        };
                    }

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"models\":[{\"name\":\"qwen2.5:latest\"}]}")
                    };
                }
            };

            var http = new HttpClient(handler);
            var setup = new OllamaSetup(http, settings);

            var status = await setup.CheckAsync();

            Assert.Equal(OllamaState.Ready, status.State);
        }

        //-----------------------------
        //pull stream reports progress values and finishes on success
        [Fact]
        public async Task Pull_ReportsProgress_AndFinishesOnSuccess()
        {
            var streamPayload = "{\"status\":\"pulling manifest\"}\n" +
                                "{\"total\":1000,\"completed\":500}\n" +
                                "{\"total\":1000,\"completed\":1000}\n" +
                                "{\"status\":\"success\"}\n";

            var handler = new FakeAiHttpHandler
            {
                Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(streamPayload)
                }
            };

            var http = new HttpClient(handler);
            var setup = new OllamaSetup(http, _settings);
            var progressValues = new List<double>();
            var progress = new SyncProgress<double>(v => progressValues.Add(v));

            await setup.PullAsync(progress, CancellationToken.None);

            Assert.Contains(0.5, progressValues);
            Assert.Contains(1.0, progressValues);
        }

        private sealed class SyncProgress<T>(Action<T> action) : IProgress<T>
        {
            public void Report(T value) => action(value);
        }

        //-----------------------------
        //pull stream error line throws ai unavailable exception
        [Fact]
        public async Task Pull_ErrorLine_Throws()
        {
            var streamPayload = "{\"status\":\"pulling manifest\"}\n" +
                                "{\"error\":\"model not found\"}\n";

            var handler = new FakeAiHttpHandler
            {
                Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(streamPayload)
                }
            };

            var http = new HttpClient(handler);
            var setup = new OllamaSetup(http, _settings);

            var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => setup.PullAsync(null, CancellationToken.None));

            Assert.Contains("model not found", ex.Message);
        }
    }
}
//------------------------------EOF-----------------------------\\
