using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace TimePlanner.Dashboard.Security
{
    //-----------------------------
    //the dashboard's transport and browser level protections in one place, each one answers a row of the threat matrix in the design document
    public static class SecurityExtensions
    {
        public const string LoginLimiter = "login";
        public const string ImportLimiter = "import";

        //the largest request body any endpoint accepts, individual endpoints may ask for less
        public const long MaxRequestBytes = 2_000_000;

        //-----------------------------
        //registers hsts, antiforgery, kestrel limits, rate limits and optional proxy handling
        public static void AddTimePlannerSecurity(this WebApplicationBuilder builder)
        {
            // browsers are told to use https only for a year, a mistake here is expensive to undo so preload and subdomains stay off until hosting is settled
            builder.Services.AddHsts(o => o.MaxAge = TimeSpan.FromDays(365));

            // the antiforgery cookie only travels over https and never on cross site requests. X-Frame-Options is set once, by the headers middleware below.
            // Always means a form is refused outright over plain http, so a host behind a proxy that ends tls must set Proxy:TrustForwardedHeaders.
            // Development follows the request instead, so the http launch profile keeps working
            var antiForgerySecure = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            builder.Services.AddAntiforgery(o =>
            {
                o.Cookie.SecurePolicy = antiForgerySecure;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.SuppressXFrameOptionsHeader = true;
            });

            // pages and downloads can hold other people's time data, so browsers and proxies must not keep a copy (back button after logout, shared computers)
            builder.Services.Configure<MvcOptions>(o => o.Filters.Add(new ResponseCacheAttribute { NoStore = true, Location = ResponseCacheLocation.None }));

            builder.WebHost.ConfigureKestrel(o =>
            {
                o.AddServerHeader = false;
                o.Limits.MaxRequestBodySize = MaxRequestBytes;
            });

            // behind a reverse proxy every request arrives from the proxy's address, so without this the rate limits would count all users as one
            // and https redirects would loop. The options only take effect when Proxy:TrustForwardedHeaders switches the middleware on, which should be
            // only when the app is reachable through the proxy alone
            builder.Services.Configure<ForwardedHeadersOptions>(o =>
            {
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                o.KnownIPNetworks.Clear();
                o.KnownProxies.Clear();
            });

            builder.Services.AddRateLimiter(o =>
            {
                o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                o.OnRejected = async (context, token) =>
                {
                    var response = context.HttpContext.Response;
                    response.Headers.RetryAfter = "60";
                    if (context.HttpContext.Request.Path.StartsWithSegments("/api"))
                    {
                        await response.WriteAsJsonAsync(new ProblemDetails { Status = 429, Title = "Too many requests, try again in a minute." },
                            options: null, contentType: "application/problem+json", cancellationToken: token);
                    }
                    else
                    {
                        response.ContentType = "text/plain; charset=utf-8";
                        await response.WriteAsync("Too many requests, try again in a minute.", token);
                    }
                };

                // sign in attempts are limited per address, on top of the per account lockout, which slows a password guesser that tries many accounts
                o.AddPolicy(LoginLimiter, http => RateLimitPartition.GetFixedWindowLimiter(
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => PerMinute(http, "RateLimiting:LoginPerMinute", 10)));

                // imports and uploads are the heaviest requests, limited per signed in user so one person cannot starve the others
                o.AddPolicy(ImportLimiter, http => RateLimitPartition.GetFixedWindowLimiter(
                    http.User.FindFirst("sub")?.Value ?? http.User.Identity?.Name ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => PerMinute(http, "RateLimiting:ImportPerMinute", 20)));
            });
        }

        //the limit is read for each partition when it is first needed, so it follows the configuration the host was started with
        private static FixedWindowRateLimiterOptions PerMinute(HttpContext http, string setting, int fallback) => new()
        {
            PermitLimit = http.RequestServices.GetRequiredService<IConfiguration>().GetValue(setting, fallback),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        };

        //-----------------------------
        //adds the response headers every reply carries. The content security policy only allows scripts, styles and images from this site, so an
        //injected script tag would not run even if output encoding were missed somewhere. In Development it is report-only, so browser tooling is not broken
        public static void UseTimePlannerSecurityHeaders(this WebApplication app)
        {
            var cspHeader = app.Environment.IsDevelopment() ? "Content-Security-Policy-Report-Only" : "Content-Security-Policy";
            const string csp = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; " +
                               "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

            app.Use(async (context, next) =>
            {
                context.Response.OnStarting(() =>
                {
                    var headers = context.Response.Headers;
                    headers["X-Content-Type-Options"] = "nosniff";
                    headers["X-Frame-Options"] = "DENY";
                    headers["Referrer-Policy"] = "no-referrer";
                    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
                    headers["Cross-Origin-Opener-Policy"] = "same-origin";
                    headers["Cross-Origin-Resource-Policy"] = "same-origin";

                    // the api docs page ships inline scripts, so it is the one place without the policy
                    if (!context.Request.Path.StartsWithSegments("/swagger"))
                        headers[cspHeader] = csp;
                    if (context.Request.Path.StartsWithSegments("/api"))
                        headers.CacheControl = "no-store";
                    return Task.CompletedTask;
                });
                await next();
            });
        }
    }
}
//------------------------------EOF-----------------------------\\
