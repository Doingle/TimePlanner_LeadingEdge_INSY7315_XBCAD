using System.Net;
using System.Text.Json;
using TimePlanner.Core.Ai;
using Xunit;

namespace TimePlanner.Core.Tests.Ai
{
    //-----------------------------
    //unit tests for ollama day compiler integration
    public class OllamaDayCompilerTests
    {
        private readonly AiSettings _settings = new()
        {
            Compiler = CompilerKind.Ollama,
            CompilerModel = "qwen2.5:3b",
            CompilerUrl = "http://localhost:11434",
            ContextTokens = 8192
        };

        private readonly DayPromptBuilder _prompt = DayPromptBuilder.Default;

        //-----------------------------
        //verifies request payload options model format and num_ctx
        [Fact]
        public async Task BuildAsync_SendsExpectedPayload()
        {
            var handler = new FakeAiHttpHandler
            {
                Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"message\":{\"content\":\"{\\\"entries\\\":[],\\\"flags\\\":[]}\"},\"eval_count\":42}")
                }
            };

            var http = new HttpClient(handler);
            var compiler = new OllamaDayCompiler(http, _settings, _prompt);

            var window = new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0),
                [new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0))], "note");
            var request = new DayBuildRequest(new DateOnly(2026, 10, 22), [window], [], []);

            await compiler.BuildAsync(request, CancellationToken.None);

            Assert.Single(handler.Bodies);
            using var doc = JsonDocument.Parse(handler.Bodies[0]);
            var root = doc.RootElement;

            Assert.Equal("qwen2.5:3b", root.GetProperty("model").GetString());
            Assert.True(root.TryGetProperty("format", out _));
            Assert.Equal(0, root.GetProperty("options").GetProperty("temperature").GetInt32());
            Assert.Equal(8192, root.GetProperty("options").GetProperty("num_ctx").GetInt32());
        }

        //-----------------------------
        //good model reply parses into suggestions
        [Fact]
        public async Task BuildAsync_GoodReply_ReturnsEntries()
        {
            var jsonReply = "{\"entries\":[{\"w\":\"W1\",\"s\":\"09:00\",\"e\":\"10:00\",\"p\":\"P1\",\"a\":\"A1\",\"note\":\"Completed task\"}],\"flags\":[]}";
            var handler = new FakeAiHttpHandler
            {
                Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{{\"message\":{{\"content\":{JsonSerializer.Serialize(jsonReply)}}},\"eval_count\":35}}")
                }
            };

            var http = new HttpClient(handler);
            var compiler = new OllamaDayCompiler(http, _settings, _prompt);

            var window = new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0),
                [new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0))], "note");
            var request = new DayBuildRequest(new DateOnly(2026, 10, 22), [window], [new("P1", "Company", "App")], [new("A1", ["Coding"])]);

            var result = await compiler.BuildAsync(request, CancellationToken.None);

            Assert.Single(result.Entries);
            Assert.Equal(35, result.OutputTokens);
            Assert.Equal("Completed task", result.Entries[0].Note);
        }

        //-----------------------------
        //retries once when first response has invalid json
        [Fact]
        public async Task BuildAsync_InvalidJsonThenValid_RetriesOnce()
        {
            var callCount = 0;
            var handler = new FakeAiHttpHandler
            {
                Respond = _ =>
                {
                    callCount++;
                    //first call returns bad json
                    if (callCount == 1)
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"message\":{\"content\":\"not valid json\"},\"eval_count\":10}")
                        };
                    }

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"message\":{\"content\":\"{\\\"entries\\\":[],\\\"flags\\\":[]}\"},\"eval_count\":15}")
                    };
                }
            };

            var http = new HttpClient(handler);
            var compiler = new OllamaDayCompiler(http, _settings, _prompt);

            var window = new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0),
                [new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0))], "note");
            var request = new DayBuildRequest(new DateOnly(2026, 10, 22), [window], [], []);

            var result = await compiler.BuildAsync(request, CancellationToken.None);

            Assert.Equal(2, callCount);
            Assert.Equal(25, result.OutputTokens);
        }

        //-----------------------------
        //maps connection refused error to plain language ollama guidance
        [Fact]
        public async Task BuildAsync_ConnectionRefused_ThrowsAiUnavailableException()
        {
            var handler = new FakeAiHttpHandler { ThrowConnectionRefused = true };
            var http = new HttpClient(handler);
            var compiler = new OllamaDayCompiler(http, _settings, _prompt);

            var window = new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0),
                [new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0))], "note");
            var request = new DayBuildRequest(new DateOnly(2026, 10, 22), [window], [], []);

            var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => compiler.BuildAsync(request, CancellationToken.None));
            Assert.Contains("Build my day needs Ollama running on this PC", ex.Message);
        }

        //-----------------------------
        //maps 404 response to model not installed guidance
        [Fact]
        public async Task BuildAsync_NotFound404_ThrowsModelNotInstalled()
        {
            var handler = new FakeAiHttpHandler
            {
                Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };

            var http = new HttpClient(handler);
            var compiler = new OllamaDayCompiler(http, _settings, _prompt);

            var window = new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0),
                [new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0))], "note");
            var request = new DayBuildRequest(new DateOnly(2026, 10, 22), [window], [], []);

            var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => compiler.BuildAsync(request, CancellationToken.None));
            Assert.Contains("isn't installed. In a terminal run: ollama pull", ex.Message);
        }

        //-----------------------------
        //reads installed models from api tags endpoint
        [Fact]
        public async Task ListModelsAsync_ReadsApiTags()
        {
            var handler = new FakeAiHttpHandler
            {
                Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"models\":[{\"name\":\"qwen2.5:3b\"},{\"name\":\"llama3.2:latest\"}]}")
                }
            };

            var http = new HttpClient(handler);
            var compiler = new OllamaDayCompiler(http, _settings, _prompt);

            var models = await compiler.ListModelsAsync(CancellationToken.None);

            Assert.Equal(2, models.Count);
            Assert.Equal("qwen2.5:3b", models[0]);
            Assert.Equal("llama3.2:latest", models[1]);
        }

        //-----------------------------
        //gpu layers zero sends num_gpu zero in options
        [Fact]
        public async Task GpuLayersZero_SendsNumGpuZero()
        {
            var handler = new FakeAiHttpHandler
            {
                Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"message\":{\"content\":\"{\\\"entries\\\":[],\\\"flags\\\":[]}\"},\"eval_count\":12}")
                }
            };

            var settings = new AiSettings
            {
                Compiler = CompilerKind.Ollama,
                CompilerModel = "qwen2.5:3b",
                CompilerUrl = "http://localhost:11434",
                ContextTokens = 8192,
                GpuLayers = 0
            };

            var http = new HttpClient(handler);
            var compiler = new OllamaDayCompiler(http, settings, _prompt);

            var window = new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0),
                [new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0))], "note");
            var request = new DayBuildRequest(new DateOnly(2026, 10, 22), [window], [], []);

            await compiler.BuildAsync(request, CancellationToken.None);

            Assert.Single(handler.Bodies);
            using var doc = JsonDocument.Parse(handler.Bodies[0]);
            var options = doc.RootElement.GetProperty("options");
            Assert.Equal(0, options.GetProperty("num_gpu").GetInt32());
        }

        //-----------------------------
        //default null gpu layers omits num_gpu from options
        [Fact]
        public async Task GpuLayersNull_OmitsNumGpu()
        {
            var handler = new FakeAiHttpHandler
            {
                Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"message\":{\"content\":\"{\\\"entries\\\":[],\\\"flags\\\":[]}\"},\"eval_count\":12}")
                }
            };

            var settings = new AiSettings
            {
                Compiler = CompilerKind.Ollama,
                CompilerModel = "qwen2.5:3b",
                CompilerUrl = "http://localhost:11434",
                ContextTokens = 8192,
                GpuLayers = null
            };

            var http = new HttpClient(handler);
            var compiler = new OllamaDayCompiler(http, settings, _prompt);

            var window = new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0),
                [new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0))], "note");
            var request = new DayBuildRequest(new DateOnly(2026, 10, 22), [window], [], []);

            await compiler.BuildAsync(request, CancellationToken.None);

            Assert.Single(handler.Bodies);
            using var doc = JsonDocument.Parse(handler.Bodies[0]);
            var options = doc.RootElement.GetProperty("options");
            Assert.False(options.TryGetProperty("num_gpu", out _));
        }
    }
}
//------------------------------EOF-----------------------------\\
