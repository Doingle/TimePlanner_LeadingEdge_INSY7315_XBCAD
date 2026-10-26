using System.Net.Http.Json;
using System.Text.Json;

namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //ollama server states
    public enum OllamaState { NotRunning, ModelMissing, Ready }

    //-----------------------------
    //status record for ollama instance
    public sealed record OllamaStatus(OllamaState State, string? Version);

    //-----------------------------
    //detects and pulls models for local ollama setup
    public sealed class OllamaSetup
    {
        private readonly HttpClient _http;
        private readonly AiSettings _settings;

        //-----------------------------
        //initializes setup helper and base url
        public OllamaSetup(HttpClient http, AiSettings settings)
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

        //-----------------------------
        //checks server health and model presence
        public async Task<OllamaStatus> CheckAsync(CancellationToken ct = default)
        {
            string? version = null;

            //probe version endpoint with three second timeout
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(3));

                var res = await _http.GetAsync("api/version", cts.Token);
                if (!res.IsSuccessStatusCode)
                {
                    return new OllamaStatus(OllamaState.NotRunning, null);
                }

                using var stream = await res.Content.ReadAsStreamAsync(cts.Token);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
                if (doc.RootElement.TryGetProperty("version", out var v))
                {
                    version = v.GetString();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return new OllamaStatus(OllamaState.NotRunning, null);
            }

            //check installed models list
            try
            {
                var tagsRes = await _http.GetAsync("api/tags", ct);
                if (!tagsRes.IsSuccessStatusCode)
                {
                    return new OllamaStatus(OllamaState.NotRunning, version);
                }

                using var stream = await tagsRes.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                if (doc.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in models.EnumerateArray())
                    {
                        var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                        var model = item.TryGetProperty("model", out var m) ? m.GetString() : null;

                        if ((name != null && MatchesModel(name, _settings.CompilerModel)) ||
                            (model != null && MatchesModel(model, _settings.CompilerModel)))
                        {
                            return new OllamaStatus(OllamaState.Ready, version);
                        }
                    }
                }

                return new OllamaStatus(OllamaState.ModelMissing, version);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return new OllamaStatus(OllamaState.NotRunning, version);
            }
        }

        //-----------------------------
        //pulls missing model reporting fractional progress
        public async Task PullAsync(IProgress<double>? progress, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/pull")
            {
                Content = JsonContent.Create(new { model = _settings.CompilerModel, stream = true })
            };

            //send request with streamed header reading
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new AiUnavailableException(string.IsNullOrWhiteSpace(body) ? "model pull failed" : body);
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            //read streamed progress updates line by line
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                //fail immediately when error field exists
                if (root.TryGetProperty("error", out var errProp))
                {
                    var message = errProp.GetString() ?? "model pull failed";
                    throw new AiUnavailableException(message);
                }

                //compute fractional progress for current layer
                if (root.TryGetProperty("total", out var totalProp) &&
                    root.TryGetProperty("completed", out var completedProp) &&
                    totalProp.TryGetDouble(out var total) &&
                    completedProp.TryGetDouble(out var completed) &&
                    total > 0)
                {
                    progress?.Report(completed / total);
                }

                //stop when completion status arrives
                if (root.TryGetProperty("status", out var statusProp) &&
                    string.Equals(statusProp.GetString(), "success", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
        }

        //-----------------------------
        //compares model tags allowing bare name match with latest
        private static bool MatchesModel(string candidate, string target)
        {
            if (string.Equals(candidate, target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            //bare candidate matches target ending in latest
            if (!candidate.Contains(':') && string.Equals(candidate + ":latest", target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            //bare target matches candidate ending in latest
            if (!target.Contains(':') && string.Equals(candidate, target + ":latest", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }
}
//------------------------------EOF-----------------------------\\
