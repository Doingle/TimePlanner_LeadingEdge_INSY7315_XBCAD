using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //covers the browser level protections: response headers, the content security policy, cookie flags, caching and host filtering
    [Collection("Api")]
    public class SecurityHeaderTests
    {
        private readonly ApiFactory _factory;

        public SecurityHeaderTests(ApiFactory factory) => _factory = factory;

        private async Task<HttpClient> SignedInBrowserAsync()
        {
            var user = await _factory.CreateLinkedUserAsync();
            var browser = _factory.NewClient();
            await ApiFactory.PostLoginAsync(browser, user.Email, user.Password);
            return browser;
        }

        private static string Header(HttpResponseMessage response, string name) =>
            response.Headers.TryGetValues(name, out var values) ? string.Join("|", values) : string.Empty;

        private static void AssertSecurityHeaders(HttpResponseMessage response)
        {
            Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
            Assert.Equal("DENY", Header(response, "X-Frame-Options"));
            Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
            Assert.Contains("camera=()", Header(response, "Permissions-Policy"));
            Assert.Equal("same-origin", Header(response, "Cross-Origin-Opener-Policy"));
            Assert.Equal("same-origin", Header(response, "Cross-Origin-Resource-Policy"));
            Assert.False(response.Headers.Contains("Server"), "the Server banner should not be sent");
        }

        [Fact]
        public async Task Pages_CarryTheSecurityHeaders_AndAStrictContentSecurityPolicy()
        {
            var response = await _factory.NewClient().GetAsync("/Account/Login");
            var csp = Header(response, "Content-Security-Policy");

            AssertSecurityHeaders(response);
            Assert.Contains("default-src 'self'", csp);
            Assert.Contains("script-src 'self'", csp);
            Assert.Contains("object-src 'none'", csp);
            Assert.Contains("frame-ancestors 'none'", csp);
            Assert.Contains("form-action 'self'", csp);
            Assert.DoesNotContain("unsafe-inline", csp);
            Assert.DoesNotContain("unsafe-eval", csp);
        }

        [Fact]
        public async Task ApiResponses_AreNotCacheable_AndCarryTheHeadersToo()
        {
            var response = await _factory.NewClient().GetAsync("/api/v1/auth/me");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            AssertSecurityHeaders(response);
            Assert.Contains("no-store", Header(response, "Cache-Control"));
        }

        [Fact]
        public async Task TheHealthCheck_CarriesTheHeaders()
        {
            var response = await _factory.NewClient().GetAsync("/health");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertSecurityHeaders(response);
        }

        [Fact]
        public async Task AuthenticatedPagesAndDownloads_AreNotCacheable()
        {
            var browser = await SignedInBrowserAsync();

            Assert.Contains("no-store", Header(await browser.GetAsync("/Report"), "Cache-Control"));
            Assert.Contains("no-store", Header(await browser.GetAsync("/CsvUpload"), "Cache-Control"));
            Assert.Contains("no-store", Header(await browser.GetAsync($"/Report/Export?from={DateTime.Today:yyyy-MM-dd}&to={DateTime.Today:yyyy-MM-dd}"), "Cache-Control"));
        }

        [Fact]
        public async Task TheAntiForgeryCookie_IsSecureHttpOnlyAndStrict()
        {
            var response = await _factory.NewClient().GetAsync("/Account/Login");
            var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.Contains("Antiforgery")).ToLowerInvariant();

            Assert.Contains("secure", cookie);
            Assert.Contains("httponly", cookie);
            Assert.Contains("samesite=strict", cookie);
        }

        //-----------------------------
        //the in-memory test server has no Kestrel, so what Kestrel is told to do is checked at its configuration instead of in a response
        [Fact]
        public void TheWebServer_HidesItsBanner_AndCapsTheRequestSize()
        {
            var kestrel = _factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

            Assert.False(kestrel.AddServerHeader);
            Assert.Equal(2_000_000, kestrel.Limits.MaxRequestBodySize);
        }

        [Fact]
        public void TheLoginAndAntiForgeryCookies_AreConfiguredSecureAndHttpOnly()
        {
            var login = _factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme).Cookie;
            var antiForgery = _factory.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie;

            Assert.Equal(CookieSecurePolicy.Always, login.SecurePolicy);
            Assert.True(login.HttpOnly);
            Assert.Equal(SameSiteMode.Lax, login.SameSite);
            Assert.Equal(CookieSecurePolicy.Always, antiForgery.SecurePolicy);
            Assert.Equal(SameSiteMode.Strict, antiForgery.SameSite);
        }

        [Fact]
        public async Task TheSessionCookie_IsSecureAndHttpOnly()
        {
            var user = await _factory.CreateLinkedUserAsync();
            var browser = _factory.NewClient();

            var login = await ApiFactory.PostLoginAsync(browser, user.Email, user.Password);
            var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".AspNetCore.Identity.Application")).ToLowerInvariant();

            Assert.Contains("secure", cookie);
            Assert.Contains("httponly", cookie);
            Assert.Contains("samesite=lax", cookie);
        }

        [Fact]
        public async Task ARequestForAnotherHostName_IsRejected()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/health");
            request.Headers.Host = "evil.example";

            var response = await _factory.NewClient().SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        //-----------------------------
        //the policy only allows scripts and styles from this site, so a page that ships an inline script, an inline style or an inline event handler would
        //silently break in the browser. This guard fails the build first, telling whoever added it to move it into a file
        [Fact]
        public async Task RenderedPages_HaveNoInlineScriptsStylesOrEventHandlers()
        {
            var browser = await SignedInBrowserAsync();
            var anonymous = _factory.NewClient();
            var pages = new[]
            {
                await anonymous.GetStringAsync("/Account/Login"),
                await browser.GetStringAsync("/"),
                await browser.GetStringAsync("/Home/Privacy"),
                await browser.GetStringAsync("/CsvUpload"),
                await browser.GetStringAsync("/Report"),
                await browser.GetStringAsync($"/Report?from={DateTime.Today.AddDays(-5):yyyy-MM-dd}&to={DateTime.Today:yyyy-MM-dd}")
            };

            foreach (var html in pages)
            {
                Assert.DoesNotMatch(@"<script(?![^>]*\bsrc\s*=)[^>]*>", html);
                Assert.DoesNotMatch(@"<style[\s>]", html);
                Assert.DoesNotMatch(@"\sstyle\s*=", html);
                Assert.DoesNotMatch(@"\son[a-z]+\s*=", html);
                Assert.DoesNotMatch(@"href\s*=\s*""javascript:", html);
            }
        }
    }
}
