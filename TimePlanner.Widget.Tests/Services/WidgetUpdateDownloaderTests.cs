using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using TimePlanner.Widget.Services;
using Xunit;

namespace TimePlanner.Widget.Tests.Services
{
    //-----------------------------
    //unit tests for widget update downloader
    public class WidgetUpdateDownloaderTests : IDisposable
    {
        private readonly string _downloadsDir;

        //-----------------------------
        //initializes isolated test directory
        public WidgetUpdateDownloaderTests()
        {
            _downloadsDir = Path.Combine(Path.GetTempPath(), "TimePlannerTests_Updates", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_downloadsDir);
        }

        //-----------------------------
        //cleans up temporary downloads directory
        public void Dispose()
        {
            //clean up temporary folder
            if (Directory.Exists(_downloadsDir))
            {
                try
                {
                    Directory.Delete(_downloadsDir, recursive: true);
                }
                catch
                {
                    //ignore cleanup failure
                }
            }
        }

        //-----------------------------
        //installer url builds expected release download location
        [Fact]
        public void InstallerUrl_BuildsExpectedReleaseLocation()
        {
            var version = new Version(1, 2, 3);
            var url = WidgetUpdateDownloader.InstallerUrl(version);

            Assert.Equal("https://github.com/Doingle/TimePlanner_LeadingEdge_INSY7315_XBCAD/releases/download/v1.2.3/TimePlanner-Setup-1.2.3.exe", url);
        }

        //-----------------------------
        //matching hash returns success and downloaded file exists
        [Fact]
        public async Task DownloadAsync_MatchingHash_ReturnsSuccessAndKeepsFile()
        {
            var content = "dummy installer binary content";
            var contentBytes = Encoding.UTF8.GetBytes(content);
            string hexHash;
            using (var sha = SHA256.Create())
            {
                hexHash = Convert.ToHexString(sha.ComputeHash(contentBytes)).ToLowerInvariant();
            }

            var handler = new FakeUpdateHttpHandler
            {
                ExeContent = contentBytes,
                ShaContent = $"{hexHash}  TimePlanner-Setup-1.2.3.exe\n"
            };
            using var http = new HttpClient(handler);
            var downloader = new WidgetUpdateDownloader(http, _downloadsDir);

            var progress = new Progress<double>();
            var result = await downloader.DownloadAsync(new Version(1, 2, 3), progress, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Null(result.ErrorMessage);
            Assert.NotNull(result.InstallerPath);
            Assert.True(File.Exists(result.InstallerPath));
        }

        //-----------------------------
        //wrong hash returns failure and deletes target file
        [Fact]
        public async Task DownloadAsync_WrongHash_FailsAndDeletesFile()
        {
            var contentBytes = Encoding.UTF8.GetBytes("tampered content");
            var handler = new FakeUpdateHttpHandler
            {
                ExeContent = contentBytes,
                ShaContent = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  TimePlanner-Setup-1.2.3.exe"
            };
            using var http = new HttpClient(handler);
            var downloader = new WidgetUpdateDownloader(http, _downloadsDir);

            var progress = new Progress<double>();
            var result = await downloader.DownloadAsync(new Version(1, 2, 3), progress, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("The download didn't match the release, so it wasn't used.", result.ErrorMessage);
            Assert.Empty(Directory.GetFiles(_downloadsDir, "TimePlanner-Setup-*.exe"));
        }

        //-----------------------------
        //missing checksum 404 returns failure and deletes target file
        [Fact]
        public async Task DownloadAsync_MissingSha404_FailsAndDeletesFile()
        {
            var contentBytes = Encoding.UTF8.GetBytes("installer bytes");
            var handler = new FakeUpdateHttpHandler
            {
                ExeContent = contentBytes,
                ShaStatusCode = HttpStatusCode.NotFound
            };
            using var http = new HttpClient(handler);
            var downloader = new WidgetUpdateDownloader(http, _downloadsDir);

            var progress = new Progress<double>();
            var result = await downloader.DownloadAsync(new Version(1, 2, 3), progress, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("Couldn't download the update. Check your connection and try again.", result.ErrorMessage);
            Assert.Empty(Directory.GetFiles(_downloadsDir, "TimePlanner-Setup-*.exe"));
        }

        //-----------------------------
        //installer returning 404 returns failure
        [Fact]
        public async Task DownloadAsync_Installer404_Fails()
        {
            var handler = new FakeUpdateHttpHandler
            {
                ExeStatusCode = HttpStatusCode.NotFound
            };
            using var http = new HttpClient(handler);
            var downloader = new WidgetUpdateDownloader(http, _downloadsDir);

            var progress = new Progress<double>();
            var result = await downloader.DownloadAsync(new Version(1, 2, 3), progress, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("Couldn't download the update. Check your connection and try again.", result.ErrorMessage);
        }

        //-----------------------------
        //fake http message handler simulating release downloads
        private sealed class FakeUpdateHttpHandler : HttpMessageHandler
        {
            public byte[]? ExeContent { get; set; }
            public HttpStatusCode ExeStatusCode { get; set; } = HttpStatusCode.OK;
            public string? ShaContent { get; set; }
            public HttpStatusCode ShaStatusCode { get; set; } = HttpStatusCode.OK;

            //-----------------------------
            //handles simulated requests
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var uri = request.RequestUri!.ToString();
                //handle checksum request
                if (uri.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                {
                    //check if failure response configured
                    if (ShaStatusCode != HttpStatusCode.OK)
                    {
                        return Task.FromResult(new HttpResponseMessage(ShaStatusCode));
                    }
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(ShaContent ?? string.Empty, Encoding.UTF8, "text/plain")
                    });
                }

                //handle installer executable request
                if (ExeStatusCode != HttpStatusCode.OK)
                {
                    return Task.FromResult(new HttpResponseMessage(ExeStatusCode));
                }

                var message = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(ExeContent ?? Array.Empty<byte>())
                };
                return Task.FromResult(message);
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
