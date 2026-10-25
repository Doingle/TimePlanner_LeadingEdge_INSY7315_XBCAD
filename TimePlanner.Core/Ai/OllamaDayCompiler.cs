using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //day compiler using local ollama instance
    public sealed class OllamaDayCompiler : DayCompilerBase
    {
        private readonly HttpClient _http;
        private readonly AiSettings _settings;

        public OllamaDayCompiler(HttpClient http, AiSettings settings, DayPromptBuilder prompt) : base(prompt)
        {
            _http = http;
            _settings = settings;

            //assign base address when not preset
            if (_http.BaseAddress == null && !string.IsNullOrWhiteSpace(settings.CompilerUrl))
            {
                var url = settings.CompilerUrl.TrimEnd('/') + "/";
                _http.BaseAddress = new Uri(url);
            }
        }

        public override string Model => _settings.CompilerModel;

        //-----------------------------
        //executes chat completion against ollama api
        protected override async Task<(string Json, int? OutputTokens)> CompleteAsync(string system, string user, string schema, CancellationToken ct)
        {
            using var schemaDoc = JsonDocument.Parse(schema);

            var options = new Dictionary<string, object>
            {
                ["temperature"] = 0,
                ["num_ctx"] = _settings.ContextTokens
            };

            //sends num_gpu only when a value is set
            if (_settings.GpuLayers is int layers)
            {
                options["num_gpu"] = layers;
            }

            var payload = new Dictionary<string, object?>
            {
                ["model"] = _settings.CompilerModel,
                ["stream"] = false,
                ["format"] = schemaDoc.RootElement.Clone(),
                ["keep_alive"] = "10m",
                ["options"] = options,
                ["messages"] = new[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = user }
                }
            };

            HttpResponseMessage response;
            try
            {
                response = await _http.PostAsJsonAsync("api/chat", payload, ct);
            }
            //map connection timeouts to plain guidance
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new AiUnavailableException("This took too long. Try a smaller model in Settings › Voice and AI.", ex);
            }
            //map connection failure to plain guidance
            catch (HttpRequestException ex)
            {
                throw new AiUnavailableException("Build my day needs Ollama running on this PC. Open Ollama, then try again.", ex);
            }

            //missing model returns http not found
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new AiUnavailableException($"The model {Model} isn't installed. In a terminal run: ollama pull {Model}");
            }

            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            var content = root.GetProperty("message").GetProperty("content").GetString() ?? "{}";
            int? outputTokens = null;

            //extract evaluation token count when available
            if (root.TryGetProperty("eval_count", out var evalProp) && evalProp.TryGetInt32(out var evalCount))
            {
                outputTokens = evalCount;
            }

            return (content, outputTokens);
        }

        //-----------------------------
        //lists models installed in local ollama
        public override async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
        {
            HttpResponseMessage response;
            try
            {
                response = await _http.GetAsync("api/tags", ct);
            }
            //map connection timeouts
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new AiUnavailableException("This took too long. Try a smaller model in Settings › Voice and AI.", ex);
            }
            //map connection failures
            catch (HttpRequestException ex)
            {
                throw new AiUnavailableException("Build my day needs Ollama running on this PC. Open Ollama, then try again.", ex);
            }

            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var result = new List<string>();

            //gather model names from tags payload
            if (doc.RootElement.TryGetProperty("models", out var modelsProp) && modelsProp.ValueKind == JsonValueKind.Array)
            {
                //read each tag entry
                foreach (var item in modelsProp.EnumerateArray())
                {
                    //keep name property if present
                    if (item.TryGetProperty("name", out var nameProp) && nameProp.GetString() is { } name)
                    {
                        result.Add(name);
                    }
                }
            }

            return result;
        }

        //-----------------------------
        //tests connectivity and model presence
        public override async Task<string?> TestAsync(CancellationToken ct)
        {
            IReadOnlyList<string> models;
            try
            {
                models = await ListModelsAsync(ct);
            }
            //return plain connection failure note
            catch (Exception)
            {
                return "Ollama isn't running";
            }

            var hasModel = models.Any(m =>
                string.Equals(m, Model, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m, $"{Model}:latest", StringComparison.OrdinalIgnoreCase) ||
                m.StartsWith($"{Model}:", StringComparison.OrdinalIgnoreCase));

            //report missing model to caller
            if (!hasModel)
            {
                return $"{Model} isn't installed";
            }

            return null;
        }
    }
}
//------------------------------EOF-----------------------------\\
