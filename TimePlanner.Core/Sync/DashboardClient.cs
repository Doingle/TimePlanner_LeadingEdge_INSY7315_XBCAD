using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace TimePlanner.Core.Sync
{
    //-----------------------------
    //calls the hosted dashboard api
    public class DashboardClient
    {
        private readonly HttpClient _http;

        public DashboardClient(HttpClient http) => _http = http;

        //the site this client talks to
        public Uri? Address => _http.BaseAddress;

        //-----------------------------
        //signs in and returns a token outcome
        public async Task<SignInOutcome> SignInAsync(string email, string password)
        {
            HttpResponseMessage response;

            try
            {
                response = await _http.PostAsJsonAsync("api/v1/auth/login", new { email, password });
            }
            //no connection or a timeout means offline
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return new SignInOutcome(SignInStatus.Offline, null, "The dashboard could not be reached.");
            }

            using (response)
            {
                //a token comes back on success
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadFromJsonAsync<TokenBody>();
                    var token = new StoredToken(body!.AccessToken, body.ExpiresAtUtc, email, body.RefreshToken, body.RefreshExpiresAtUtc);
                    return new SignInOutcome(SignInStatus.SignedIn, token, null);
                }

                //wrong email or password
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return new SignInOutcome(SignInStatus.InvalidDetails, null, "Invalid email or password.");
                }

                //the account must set a new password on the website first
                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    return new SignInOutcome(SignInStatus.PasswordChangeRequired, null, await TitleAsync(response));
                }

                return new SignInOutcome(SignInStatus.Failed, null, $"Sign in failed ({(int)response.StatusCode}).");
            }
        }

        //-----------------------------
        //swaps a refresh token for new access and refresh tokens
        public async Task<SignInOutcome> RefreshAsync(StoredToken current)
        {
            HttpResponseMessage response;

            try
            {
                response = await _http.PostAsJsonAsync("api/v1/auth/refresh", new { refreshToken = current.RefreshToken });
            }
            //no connection or a timeout means offline
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return new SignInOutcome(SignInStatus.Offline, null, "The dashboard could not be reached.");
            }

            using (response)
            {
                //a new token pair comes back on success
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadFromJsonAsync<TokenBody>();
                    var token = new StoredToken(body!.AccessToken, body.ExpiresAtUtc, current.Email, body.RefreshToken, body.RefreshExpiresAtUtc);
                    return new SignInOutcome(SignInStatus.SignedIn, token, null);
                }

                //the refresh token was revoked or invalid
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return new SignInOutcome(SignInStatus.InvalidDetails, null, "Please sign in again.");
                }

                return new SignInOutcome(SignInStatus.Failed, null, $"Refresh failed ({(int)response.StatusCode}).");
            }
        }

        //-----------------------------
        //revokes the refresh token on the server when signing out
        public async Task LogoutAsync(string refreshToken)
        {
            try
            {
                using var response = await _http.PostAsJsonAsync("api/v1/auth/logout", new { refreshToken });
            }
            //signing out locally still happens when the dashboard is unreachable
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
            }
        }

        //-----------------------------
        //uploads one day and replaces it on the server
        public async Task<SendOutcome> SendDayAsync(string accessToken, IReadOnlyList<DayEntryPayload> entries)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/timesheets/import")
            {
                Content = JsonContent.Create(new { entries, replaceDays = true })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            HttpResponseMessage response;

            try
            {
                response = await _http.SendAsync(request);
            }
            //no connection or a timeout means offline
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return SendOutcome.Of(SendStatus.Offline, "The dashboard could not be reached. The day is still saved here.");
            }

            using (response)
            {
                //stored on the server
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ImportBody>();
                    return new SendOutcome(SendStatus.Sent, result?.Created ?? 0, Array.Empty<string>(), null);
                }

                //the server checked the rows and refused them
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    var result = await response.Content.ReadFromJsonAsync<ImportBody>();
                    var errors = (result?.Errors ?? new List<ImportError>()).Select(e => $"Row {e.Row}: {e.Message}").ToList();
                    return new SendOutcome(SendStatus.Rejected, 0, errors, "The dashboard rejected this day.");
                }

                //the token expired or was revoked
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return SendOutcome.Of(SendStatus.NeedsSignIn, "Please sign in again.");
                }

                //too many sends in a short time
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    return SendOutcome.Of(SendStatus.Failed, "Too many sends. Try again in a minute.");
                }

                //the account is not allowed to import
                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    return SendOutcome.Of(SendStatus.Failed, await TitleAsync(response));
                }

                return SendOutcome.Of(SendStatus.Failed, $"The dashboard answered {(int)response.StatusCode}.");
            }
        }

        //-----------------------------
        //reads the title from a problem details body
        private static async Task<string> TitleAsync(HttpResponseMessage response)
        {
            try
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

                //problem details carry a readable title
                if (doc.RootElement.TryGetProperty("title", out var title))
                {
                    return title.GetString() ?? "Request refused.";
                }
            }
            //a non json body has no title
            catch (JsonException)
            {
            }

            return "Request refused.";
        }

        private sealed record TokenBody(string AccessToken, string TokenType, DateTime ExpiresAtUtc, string? RefreshToken, DateTime? RefreshExpiresAtUtc);

        private sealed record ImportError(int Row, string Message);

        private sealed record ImportBody(int Created, int Skipped, List<ImportError>? Errors);
    }
}
//------------------------------EOF-----------------------------\\
