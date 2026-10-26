using System.Net.Http.Json;
using System.Text.Json;

namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //result of day model benchmark
    public sealed record DaySpeedResult(int? GpuLayers, int? GraphicsTokensPerSec, int? ProcessorTokensPerSec);

    //-----------------------------
    //benchmarks day model on graphics and processor
    public static class SpeedCheck
    {
        private const string TestPrompt = "Write one sentence about a clock tower in a quiet town.";

        //-----------------------------
        //runs warmup and compares graphics versus processor
        public static async Task<DaySpeedResult> RunAsync(HttpClient http, string model, CancellationToken ct = default)
        {
            //warm up model with single token call
            try
            {
                using var warmupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                warmupCts.CancelAfter(TimeSpan.FromSeconds(60));
                var warmupPayload = new
                {
                    model,
                    prompt = "hi",
                    stream = false,
                    options = new { num_predict = 1 }
                };
                await http.PostAsJsonAsync("api/generate", warmupPayload, warmupCts.Token);
            }
            //ignore warmup failures
            catch
            {
            }

            int? graphicsTokens = await MeasureAsync(http, model, numGpu: null, ct);
            int? processorTokens = await MeasureAsync(http, model, numGpu: 0, ct);

            //graphics failed so choose processor
            if (graphicsTokens == null)
            {
                return new DaySpeedResult(0, null, processorTokens);
            }

            //processor failed but graphics worked so choose graphics
            if (processorTokens == null)
            {
                return new DaySpeedResult(null, graphicsTokens, null);
            }

            //choose graphics only when at least fifteen percent faster
            if (graphicsTokens.Value >= processorTokens.Value * 1.15)
            {
                return new DaySpeedResult(null, graphicsTokens, processorTokens);
            }

            return new DaySpeedResult(0, graphicsTokens, processorTokens);
        }

        //-----------------------------
        //measures tokens per second for a configuration
        private static async Task<int?> MeasureAsync(HttpClient http, string model, int? numGpu, CancellationToken ct)
        {
            //measure speed within sixty seconds timeout
            try
            {
                using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                runCts.CancelAfter(TimeSpan.FromSeconds(60));

                object options = numGpu.HasValue
                    ? new { num_predict = 64, temperature = 0, num_gpu = numGpu.Value }
                    : new { num_predict = 64, temperature = 0 };

                var payload = new
                {
                    model,
                    prompt = TestPrompt,
                    stream = false,
                    options
                };

                var response = await http.PostAsJsonAsync("api/generate", payload, runCts.Token);
                //fail if status is not successful
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                using var stream = await response.Content.ReadAsStreamAsync(runCts.Token);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: runCts.Token);
                var root = doc.RootElement;

                //read token count and duration
                if (root.TryGetProperty("eval_count", out var countProp) &&
                    root.TryGetProperty("eval_duration", out var durProp))
                {
                    var count = countProp.GetInt64();
                    var durationNs = durProp.GetInt64();

                    //calculate tokens per second from nanoseconds
                    if (count > 0 && durationNs > 0)
                    {
                        var seconds = durationNs / 1_000_000_000.0;
                        var tps = count / seconds;
                        return (int)Math.Round(tps);
                    }
                }

                return null;
            }
            //timed out or failed call counts as failed
            catch
            {
                return null;
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
