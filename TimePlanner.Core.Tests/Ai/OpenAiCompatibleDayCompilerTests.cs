using System.Net;
using System.Text.Json;
using TimePlanner.Core.Ai;
using Xunit;

namespace TimePlanner.Core.Tests.Ai
{
    //-----------------------------
    //unit tests for openai compatible day compiler
    public class OpenAiCompatibleDayCompilerTests
    {
        private readonly AiSettings _settings = new()
        {
            Compiler = CompilerKind.OpenAiCompatible,
            CompilerModel = "deepseek-chat",
            CompilerUrl = "https://api.deepseek.com/v1"
        };

        private readonly DayPromptBuilder _prompt = DayPromptBuilder.Default;

        //-----------------------------
        //attaches bearer header only when api key is provided
        [Fact]
        public async Task BuildAsync_BearerHeaderOnlyWhenKeyPresent()
        {
            var handlerWithoutKey = new FakeAiHttpHandler
            {
                Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"{\\\"entries\\\":[],\\\"flags\\\":[]}\"}}],\"usage\":{\"completion_tokens\":10}}")
                }
            };

            var compilerWithoutKey = new OpenAiCompatibleDayCompiler(new HttpClient(handlerWithoutKey), _settings, null, _prompt);
            var window = new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0),
                [new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0))], "note");
            var request = new DayBuildRequest(new DateOnly(2026, 10, 22), [window], [], []);

            await compilerWithoutKey.BuildAsync(request, CancellationToken.None);

            Assert.Null(handlerWithoutKey.Requests[0].Headers.Authorization);

            var handlerWithKey = new FakeAiHttpHandler
            {
                Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"{\\\"entries\\\":[],\\\"flags\\\":[]}\"}}],\"usage\":{\"completion_tokens\":10}}")
                }
            };

            var compilerWithKey = new OpenAiCompatibleDayCompiler(new HttpClient(handlerWithKey), _settings, "secret-key", _prompt);
            await compilerWithKey.BuildAsync(request, CancellationToken.None);

            Assert.NotNull(handlerWithKey.Requests[0].Headers.Authorization);
            Assert.Equal("Bearer", handlerWithKey.Requests[0].Headers.Authorization!.Scheme);
            Assert.Equal("secret-key", handlerWithKey.Requests[0].Headers.Authorization!.Parameter);
        }

        //-----------------------------
        //retries with json object when server responds with 400 bad request
        [Fact]
        public async Task BuildAsync_BadRequest400_RetriesWithJsonObject()
        {
            var callCount = 0;
            var handler = new FakeAiHttpHandler
            {
                Respond = _ =>
                {
                    callCount++;
                    //first call returns 400 bad request
                    if (callCount == 1)
                    {
                        return new HttpResponseMessage(HttpStatusCode.BadRequest)
                        {
                            Content = new StringContent("{\"error\":{\"message\":\"schema not supported\"}}")
                        };
                    }

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"{\\\"entries\\\":[],\\\"flags\\\":[]}\"}}],\"usage\":{\"completion_tokens\":12}}")
                    };
                }
            };

            var compiler = new OpenAiCompatibleDayCompiler(new HttpClient(handler), _settings, "key", _prompt);
            var window = new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0),
                [new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0))], "note");
            var request = new DayBuildRequest(new DateOnly(2026, 10, 22), [window], [], []);

            var result = await compiler.BuildAsync(request, CancellationToken.None);

            Assert.Equal(2, callCount);
            Assert.Equal(12, result.OutputTokens);

            using var doc = JsonDocument.Parse(handler.Bodies[1]);
            var formatType = doc.RootElement.GetProperty("response_format").GetProperty("type").GetString();
            Assert.Equal("json_object", formatType);
        }
    }
}
//------------------------------EOF-----------------------------\\
