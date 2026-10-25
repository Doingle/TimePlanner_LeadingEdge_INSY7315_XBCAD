using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //status response from dashboard ai endpoint
    public sealed record DashboardAiStatus(bool Available, string? Model);

    //-----------------------------
    //day compiler that delegates build to dashboard api
    public sealed class DashboardDayCompiler : IDayCompiler
    {
        private readonly HttpClient _http;
        private readonly string? _token;

        public string Model => "Organisation AI";

        //-----------------------------
        //initializes compiler with dashboard client and bearer token
        public DashboardDayCompiler(HttpClient http, string? token, string? baseUrl = null)
        {
            _http = http;
            _token = token;

            //set base address when not preset
            if (_http.BaseAddress == null && !string.IsNullOrWhiteSpace(baseUrl))
            {
                _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            }
        }

        //-----------------------------
        //sends compilation request to dashboard endpoint
        public async Task<DayBuildResult> BuildAsync(DayBuildRequest request, CancellationToken ct)
        {
            HttpResponseMessage response;
            //send build request to dashboard
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "api/v1/ai/build-day")
                {
                    Content = JsonContent.Create(request)
                };

                //attach bearer token when present
                if (!string.IsNullOrWhiteSpace(_token))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                }

                response = await _http.SendAsync(req, ct);
            }
            //map network exceptions
            catch (HttpRequestException ex)
            {
                throw new AiUnavailableException("Can't reach the dashboard.", ex);
            }
            //map timeout cancellations
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new AiUnavailableException("Can't reach the dashboard.", ex);
            }

            //map unauthorized session
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new AiUnavailableException("Sign in to the dashboard again.");
            }

            //map unconfigured server ai
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                throw new AiUnavailableException("Your organisation hasn't set up AI yet.");
            }

            //ensure successful status
            if (!response.IsSuccessStatusCode)
            {
                throw new AiUnavailableException("Can't reach the dashboard.");
            }

            var result = await response.Content.ReadFromJsonAsync<DayBuildResult>(cancellationToken: ct);
            //ensure response parsed properly
            if (result == null)
            {
                throw new AiUnavailableException("Can't reach the dashboard.");
            }

            return result;
        }

        //-----------------------------
        //retrieves model name configured on the dashboard
        public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
        {
            //query dashboard status endpoint
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "api/v1/ai/status");
                //attach bearer token when present
                if (!string.IsNullOrWhiteSpace(_token))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                }

                var response = await _http.SendAsync(req, ct);
                //return empty list on failure
                if (!response.IsSuccessStatusCode)
                {
                    return Array.Empty<string>();
                }

                var status = await response.Content.ReadFromJsonAsync<DashboardAiStatus>(cancellationToken: ct);
                //return server model when available
                if (status?.Available == true && !string.IsNullOrWhiteSpace(status.Model))
                {
                    return [status.Model];
                }

                return Array.Empty<string>();
            }
            //ignore communication errors
            catch
            {
                return Array.Empty<string>();
            }
        }

        //-----------------------------
        //tests connectivity and configuration of dashboard ai
        public async Task<string?> TestAsync(CancellationToken ct)
        {
            //probe status endpoint on dashboard
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "api/v1/ai/status");
                //attach bearer token when present
                if (!string.IsNullOrWhiteSpace(_token))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                }

                var response = await _http.SendAsync(req, ct);
                //check for expired auth
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return "Sign in to the dashboard again.";
                }

                //check for unconfigured ai
                if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    return "Your organisation hasn't set up AI yet.";
                }

                //check for server error
                if (!response.IsSuccessStatusCode)
                {
                    return "Can't reach the dashboard.";
                }

                var status = await response.Content.ReadFromJsonAsync<DashboardAiStatus>(cancellationToken: ct);
                //verify ai availability flag
                if (status?.Available == true)
                {
                    return null;
                }

                return "Your organisation hasn't set up AI yet.";
            }
            //handle connection failure
            catch (HttpRequestException)
            {
                return "Can't reach the dashboard.";
            }
            //handle unexpected errors
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
