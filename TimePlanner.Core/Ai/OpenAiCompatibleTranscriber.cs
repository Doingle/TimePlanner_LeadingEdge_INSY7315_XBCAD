using System.Net.Http.Headers;
using System.Text.Json;

namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //transcription using openai compatible audio endpoint
    public sealed class OpenAiCompatibleTranscriber : ITranscriber
    {
        private readonly HttpClient _http;
        private readonly AiSettings _settings;
        private readonly string? _apiKey;

        public OpenAiCompatibleTranscriber(HttpClient http, AiSettings settings, string? apiKey)
        {
            _http = http;
            _settings = settings;
            _apiKey = apiKey;

            //assign base address when not preset
            if (_http.BaseAddress == null && !string.IsNullOrWhiteSpace(settings.TranscriberUrl))
            {
                var url = settings.TranscriberUrl.TrimEnd('/') + "/";
                _http.BaseAddress = new Uri(url);
            }
        }

        //-----------------------------
        //posts wav audio to transcriptions endpoint
        public async Task<string> TranscribeAsync(string wavPath, TranscriptionHints hints, CancellationToken ct)
        {
            using var form = new MultipartFormDataContent();

            var fileBytes = await File.ReadAllBytesAsync(wavPath, ct);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            form.Add(fileContent, "file", Path.GetFileName(wavPath));

            form.Add(new StringContent(_settings.TranscriberModel), "model");
            form.Add(new StringContent(hints.Language), "language");

            var vocab = string.Join(", ", hints.Vocabulary);
            //cut vocabulary prompt to limit
            if (vocab.Length > 400)
            {
                vocab = vocab[..400];
            }

            form.Add(new StringContent(vocab), "prompt");

            var request = new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions")
            {
                Content = form
            };

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
                throw new AiUnavailableException("Transcription timed out. Check the service settings.", ex);
            }
            //map transport error
            catch (HttpRequestException ex)
            {
                throw new AiUnavailableException($"Could not reach transcription endpoint at {_settings.TranscriberUrl}.", ex);
            }

            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return doc.RootElement.GetProperty("text").GetString() ?? "";
        }

        //-----------------------------
        //tests connectivity to transcriber endpoint
        public async Task<string?> TestAsync(CancellationToken ct)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "models");
            //attach authorization header only when key is provided
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey.Trim());
            }

            try
            {
                var response = await _http.SendAsync(request, ct);
                //non success status indicates configuration problem
                if (!response.IsSuccessStatusCode)
                {
                    return $"Transcriber returned HTTP {(int)response.StatusCode}";
                }

                return null;
            }
            //return plain error on failure
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
