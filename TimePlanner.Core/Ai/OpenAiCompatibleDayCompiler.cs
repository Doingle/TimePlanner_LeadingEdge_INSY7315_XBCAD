using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //day compiler using openai compatible rest endpoints
    public sealed class OpenAiCompatibleDayCompiler : DayCompilerBase
    {
        private readonly HttpClient _http;
        private readonly AiSettings _settings;
        private readonly string? _apiKey;

        public OpenAiCompatibleDayCompiler(HttpClient http, AiSettings settings, string? apiKey, DayPromptBuilder prompt) : base(prompt)
        {
            _http = http;
            _settings = settings;
            _apiKey = apiKey;

            //assign base address when not preset
            if (_http.BaseAddress == null && !string.IsNullOrWhiteSpace(settings.CompilerUrl))
            {
                var url = settings.CompilerUrl.TrimEnd('/') + "/";
                _http.BaseAddress = new Uri(url);
            }
        }

        public override string Model => _settings.CompilerModel;

        //-----------------------------
        //executes chat completion with fallback to json object
        protected override async Task<(string Json, int? OutputTokens)> CompleteAsync(string system, string user, string schema, CancellationToken ct)
        {
            using var schemaDoc = JsonDocument.Parse(schema);

            var structuredPayload = new Dictionary<string, object?>
            {
                ["model"] = _settings.CompilerModel,
                ["temperature"] = 0,
                ["response_format"] = new Dictionary<string, object>
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new Dictionary<string, object>
                    {
                        ["name"] = "day",
                        ["schema"] = schemaDoc.RootElement.Clone()
                    }
                },
                ["messages"] = new[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = user }
                }
            };

            var response = await SendRequestAsync(structuredPayload, ct);

            //fallback to json object format on bad request
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var objectPayload = new Dictionary<string, object?>
                {
                    ["model"] = _settings.CompilerModel,
                    ["temperature"] = 0,
                    ["response_format"] = new Dictionary<string, object>
                    {
                        ["type"] = "json_object"
                    },
                    ["messages"] = new[]
                    {
                        new { role = "system", content = system },
                        new { role = "user", content = user }
                    }
                };

                response = await SendRequestAsync(objectPayload, ct);
            }

            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            var choice = root.GetProperty("choices")[0];
            var content = choice.GetProperty("message").GetProperty("content").GetString() ?? "{}";
            int? outputTokens = null;

            //read completion tokens from usage object when present
            if (root.TryGetProperty("usage", out var usageProp) &&
                usageProp.TryGetProperty("completion_tokens", out var compTokensProp) &&
                compTokensProp.TryGetInt32(out var count))
            {
                outputTokens = count;
            }

            return (content, outputTokens);
        }

        //-----------------------------
        //sends a json post with optional bearer authorization
        private async Task<HttpResponseMessage> SendRequestAsync(object payload, CancellationToken ct)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = JsonContent.Create(payload)
            };

            //attach authorization header only when key is provided
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey.Trim());
            }

            try
            {
                return await _http.SendAsync(request, ct);
            }
            //map timeout to plain guidance
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new AiUnavailableException("This took too long. Try a smaller model in Settings › Voice and AI.", ex);
            }
            //map transport failure
            catch (HttpRequestException ex)
            {
                throw new AiUnavailableException($"Could not reach the AI service at {_settings.CompilerUrl}. Check the address and connection.", ex);
            }
        }

        //-----------------------------
        //lists models available from the provider
        public override async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "models");
            //attach authorization header only when key is provided
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey.Trim());
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct);
            }
            //map timeout
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new AiUnavailableException("This took too long. Try a smaller model in Settings › Voice and AI.", ex);
            }
            //map connection failure
            catch (HttpRequestException ex)
            {
                throw new AiUnavailableException($"Could not reach the AI service at {_settings.CompilerUrl}.", ex);
            }

            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var result = new List<string>();

            //extract model identifiers from data array
            if (doc.RootElement.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
            {
                //gather each model id
                foreach (var item in dataProp.EnumerateArray())
                {
                    //keep id property if present
                    if (item.TryGetProperty("id", out var idProp) && idProp.GetString() is { } id)
                    {
                        result.Add(id);
                    }
                }
            }

            return result;
        }

        //-----------------------------
        //tests connectivity and credentials
        public override async Task<string?> TestAsync(CancellationToken ct)
        {
            try
            {
                var models = await ListModelsAsync(ct);
                //warn when returned model catalog is empty
                if (models.Count == 0)
                {
                    return "No models returned by service";
                }

                return null;
            }
            //return plain error on test failure
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
