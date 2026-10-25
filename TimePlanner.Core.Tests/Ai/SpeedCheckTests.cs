using System.Net;
using System.Text.Json;
using TimePlanner.Core.Ai;
using Xunit;

namespace TimePlanner.Core.Tests.Ai
{
    //-----------------------------
    //unit tests for hardware speed benchmark
    public class SpeedCheckTests
    {
        //-----------------------------
        //graphics chosen when at least fifteen percent faster
        [Fact]
        public async Task Run_GraphicsFaster_GraphicsChosen()
        {
            FakeAiHttpHandler? handler = null;
            handler = new FakeAiHttpHandler
            {
                Respond = req =>
                {
                    //handle warmup or generate
                    var body = handler?.Bodies.LastOrDefault() ?? "";
                    //processor call has num_gpu 0
                    if (body.Contains("\"num_gpu\":0") || body.Contains("\"num_gpu\": 0"))
                    {
                        var json = JsonSerializer.Serialize(new { eval_count = 20, eval_duration = 1_000_000_000L });
                        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
                    }
                    //graphics call
                    else if (body.Contains("num_predict\":64") || body.Contains("num_predict\": 64"))
                    {
                        var json = JsonSerializer.Serialize(new { eval_count = 40, eval_duration = 1_000_000_000L });
                        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
                    }

                    //warmup call
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
                }
            };

            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
            var result = await SpeedCheck.RunAsync(http, "qwen2.5:3b");

            Assert.Null(result.GpuLayers);
            Assert.Equal(40, result.GraphicsTokensPerSec);
            Assert.Equal(20, result.ProcessorTokensPerSec);
        }

        //-----------------------------
        //processor chosen when graphics is within fifteen percent
        [Fact]
        public async Task Run_GraphicsWithin15Percent_ProcessorChosen()
        {
            FakeAiHttpHandler? handler = null;
            handler = new FakeAiHttpHandler
            {
                Respond = req =>
                {
                    var body = handler?.Bodies.LastOrDefault() ?? "";
                    //processor call
                    if (body.Contains("\"num_gpu\":0") || body.Contains("\"num_gpu\": 0"))
                    {
                        var json = JsonSerializer.Serialize(new { eval_count = 20, eval_duration = 1_000_000_000L });
                        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
                    }
                    //graphics call only ten percent faster
                    else if (body.Contains("num_predict\":64") || body.Contains("num_predict\": 64"))
                    {
                        var json = JsonSerializer.Serialize(new { eval_count = 22, eval_duration = 1_000_000_000L });
                        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
                    }

                    //warmup call
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
                }
            };

            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
            var result = await SpeedCheck.RunAsync(http, "qwen2.5:3b");

            Assert.Equal(0, result.GpuLayers);
            Assert.Equal(22, result.GraphicsTokensPerSec);
            Assert.Equal(20, result.ProcessorTokensPerSec);
        }

        //-----------------------------
        //processor chosen when graphics call errors
        [Fact]
        public async Task Run_GraphicsErrors_ProcessorChosen()
        {
            FakeAiHttpHandler? handler = null;
            handler = new FakeAiHttpHandler
            {
                Respond = req =>
                {
                    var body = handler?.Bodies.LastOrDefault() ?? "";
                    //processor call succeeds
                    if (body.Contains("\"num_gpu\":0") || body.Contains("\"num_gpu\": 0"))
                    {
                        var json = JsonSerializer.Serialize(new { eval_count = 20, eval_duration = 1_000_000_000L });
                        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
                    }
                    //graphics call fails with internal server error
                    else if (body.Contains("num_predict\":64") || body.Contains("num_predict\": 64"))
                    {
                        return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                    }

                    //warmup call
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
                }
            };

            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
            var result = await SpeedCheck.RunAsync(http, "qwen2.5:3b");

            Assert.Equal(0, result.GpuLayers);
            Assert.Null(result.GraphicsTokensPerSec);
            Assert.Equal(20, result.ProcessorTokensPerSec);
        }
    }
}
//------------------------------EOF-----------------------------\\
