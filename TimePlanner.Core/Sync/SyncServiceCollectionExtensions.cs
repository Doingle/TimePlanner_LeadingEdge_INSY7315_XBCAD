using Microsoft.Extensions.DependencyInjection;

namespace TimePlanner.Core.Sync
{
    //-----------------------------
    //registers sending days for the widget
    public static class SyncServiceCollectionExtensions
    {
        //hosted dashboard used when no address is given
        public const string DefaultDashboardUrl = "https://timeplanner-dashboard-dj-dpd2byfyhfd4gthc.southafricanorth-01.azurewebsites.net/";

        //environment variable a developer sets to use a local dashboard
        public const string DashboardUrlVariable = "TIMEPLANNER_DASHBOARD_URL";

        //-----------------------------
        //picks the address from code then environment then default
        public static string ResolveDashboardUrl(string? explicitUrl, string? environmentUrl)
        {
            //an address passed in code wins
            if (!string.IsNullOrWhiteSpace(explicitUrl))
            {
                return explicitUrl;
            }

            //a developer override comes next
            if (!string.IsNullOrWhiteSpace(environmentUrl))
            {
                return environmentUrl;
            }

            return DefaultDashboardUrl;
        }

        //-----------------------------
        //adds the client stores and send service
        public static IServiceCollection AddTimePlannerSync(this IServiceCollection services, string? dashboardUrl = null)
        {
            var resolved = ResolveDashboardUrl(dashboardUrl, Environment.GetEnvironmentVariable(DashboardUrlVariable));
            var baseUri = ValidateDashboardUrl(resolved);
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimePlanner");

            //waking hosts can be slow so the timeout is generous
            services.AddSingleton(new DashboardClient(new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(90) }));
            services.AddSingleton<ISendHistoryStore>(new FileSendHistoryStore(Path.Combine(folder, "sent-days.json")));

            //dpapi exists only on windows
            if (OperatingSystem.IsWindows())
            {
                services.AddSingleton<ITokenStore>(new DpapiTokenStore(Path.Combine(folder, "dashboard-token.bin")));
            }
            else
            {
                services.AddSingleton<ITokenStore, InMemoryTokenStore>();
            }

            services.AddScoped<DaySendService>();
            return services;
        }

        //-----------------------------
        //requires https except on localhost
        public static Uri ValidateDashboardUrl(string url)
        {
            //only absolute addresses work
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException("The dashboard address is not a valid url.", nameof(url));
            }

            var local = uri.IsLoopback;

            //plain http would expose the token
            if (uri.Scheme != Uri.UriSchemeHttps && !(local && uri.Scheme == Uri.UriSchemeHttp))
            {
                throw new ArgumentException("The dashboard address must use https.", nameof(url));
            }

            //relative api paths need a trailing slash
            return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
        }
    }
}
//------------------------------EOF-----------------------------\\
