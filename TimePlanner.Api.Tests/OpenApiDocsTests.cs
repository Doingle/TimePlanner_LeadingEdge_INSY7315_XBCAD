using System.Text.Json;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //a host that serves the api docs, which are otherwise only on in Development
    public class DocsFactory : ApiFactory
    {
        protected override Dictionary<string, string?> Settings()
        {
            var settings = base.Settings();
            settings["Api:EnableDocs"] = "true";
            return settings;
        }
    }

    //-----------------------------
    //keeps the swagger page complete: every endpoint has to say what it can answer with
    public class OpenApiDocsTests
    {
        //signing in and out is open to everyone, so these are the only endpoints that cannot answer 401
        private static readonly string[] Anonymous = ["/api/v1/auth/login", "/api/v1/auth/refresh", "/api/v1/auth/logout"];

        [Fact]
        public async Task EveryEndpoint_ListsItsResponses()
        {
            using var factory = new DocsFactory();
            using var doc = JsonDocument.Parse(await factory.NewClient().GetStringAsync("/swagger/v1/swagger.json"));

            var gaps = new List<string>();
            var checkedCount = 0;
            foreach (var path in doc.RootElement.GetProperty("paths").EnumerateObject().Where(p => p.Name.StartsWith("/api/")))
            {
                foreach (var operation in path.Value.EnumerateObject().Where(o => o.Value.ValueKind == JsonValueKind.Object))
                {
                    checkedCount++;
                    var name = $"{operation.Name.ToUpperInvariant()} {path.Name}";
                    var codes = operation.Value.GetProperty("responses").EnumerateObject().Select(r => r.Name).ToHashSet();
                    var takesInput = operation.Value.TryGetProperty("requestBody", out _)
                        || (operation.Value.TryGetProperty("parameters", out var parameters)
                            && parameters.EnumerateArray().Any(p => p.GetProperty("in").GetString() == "query"));

                    if (!codes.Any(c => c.StartsWith('2')))
                        gaps.Add($"{name} lists no success response");
                    if (!Anonymous.Contains(path.Name) && !codes.Contains("401"))
                        gaps.Add($"{name} does not list 401");
                    if (!path.Name.StartsWith("/api/v1/auth/") && !codes.Contains("403"))
                        gaps.Add($"{name} does not list 403");
                    if (takesInput && !codes.Contains("400"))
                        gaps.Add($"{name} takes input but does not list 400");
                }
            }

            Assert.True(checkedCount > 0, "the docs listed no api endpoints at all");
            Assert.True(gaps.Count == 0, "These endpoints are missing responses in the docs:\n" + string.Join("\n", gaps));
        }
    }
}
