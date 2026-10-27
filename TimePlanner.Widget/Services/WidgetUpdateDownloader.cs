using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;

namespace TimePlanner.Widget.Services
{
    //result of update download operation
    public sealed record UpdateDownloadResult(bool Success, string? ErrorMessage, string? InstallerPath);

    //-----------------------------
    //downloads and verifies widget release installers
    public sealed class WidgetUpdateDownloader
    {
        public const string ReleaseDownloadBase = "https://github.com/Doingle/TimePlanner_LeadingEdge_INSY7315_XBCAD/releases/download/";

        private readonly HttpClient _http;
        private readonly string _downloadsFolder;

        public WidgetUpdateDownloader(HttpClient http, string? downloadsFolder = null)
        {
            _http = http;
            _downloadsFolder = downloadsFolder
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimePlanner", "downloads");
        }

        //-----------------------------
        //builds installer download url from parsed version
        public static string InstallerUrl(Version v)
        {
            var versionText = $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
            return $"{ReleaseDownloadBase}v{versionText}/TimePlanner-Setup-{versionText}.exe";
        }

        //-----------------------------
        //downloads installer and checksum verifies sha256 and reports progress
        public async Task<UpdateDownloadResult> DownloadAsync(Version v, IProgress<double> progress, CancellationToken ct)
        {
            var installerUrl = InstallerUrl(v);
            var shaUrl = installerUrl + ".sha256";
            var versionText = $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
            var targetFile = Path.Combine(_downloadsFolder, $"TimePlanner-Setup-{versionText}.exe");

            //delete older setup files in downloads folder
            try
            {
                //check if downloads folder exists
                if (Directory.Exists(_downloadsFolder))
                {
                    foreach (var older in Directory.GetFiles(_downloadsFolder, "TimePlanner-Setup-*.exe"))
                    {
                        try
                        {
                            File.Delete(older);
                        }
                        catch
                        {
                            //ignore older file deletion error
                        }
                    }
                }
                else
                {
                    Directory.CreateDirectory(_downloadsFolder);
                }
            }
            catch
            {
                //ignore folder initialization error
            }

            try
            {
                //download installer executable with progress reporting
                using (var response = await _http.GetAsync(installerUrl, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    //check response status code
                    if (!response.IsSuccessStatusCode)
                    {
                        TryDeleteFile(targetFile);
                        return new UpdateDownloadResult(false, "Couldn't download the update. Check your connection and try again.", null);
                    }

                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                    await using var fileStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None);

                    var buffer = new byte[81920];
                    long totalRead = 0;
                    int read;
                    //read stream in chunks until end of content
                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read, ct);
                        totalRead += read;
                        //report progress when total length is known
                        if (totalBytes > 0)
                        {
                            progress.Report(Math.Clamp((double)totalRead / totalBytes, 0.0, 1.0));
                        }
                    }
                }

                string shaContent;
                //download sha256 checksum file
                using (var shaResponse = await _http.GetAsync(shaUrl, ct))
                {
                    //check checksum response status
                    if (!shaResponse.IsSuccessStatusCode)
                    {
                        TryDeleteFile(targetFile);
                        return new UpdateDownloadResult(false, "Couldn't download the update. Check your connection and try again.", null);
                    }
                    shaContent = await shaResponse.Content.ReadAsStringAsync(ct);
                }

                var tokens = shaContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                //check token parsing
                if (tokens.Length == 0)
                {
                    TryDeleteFile(targetFile);
                    return new UpdateDownloadResult(false, "The download didn't match the release, so it wasn't used.", null);
                }
                var expectedHash = tokens[0].Trim().ToLowerInvariant();

                string computedHash;
                //compute sha256 hash of downloaded installer
                using (var sha = SHA256.Create())
                await using (var fileStream = File.OpenRead(targetFile))
                {
                    var hashBytes = await sha.ComputeHashAsync(fileStream, ct);
                    computedHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
                }

                //verify computed hash matches expected hash
                if (!string.Equals(computedHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteFile(targetFile);
                    return new UpdateDownloadResult(false, "The download didn't match the release, so it wasn't used.", null);
                }

                return new UpdateDownloadResult(true, null, targetFile);
            }
            catch (OperationCanceledException)
            {
                TryDeleteFile(targetFile);
                throw;
            }
            catch
            {
                TryDeleteFile(targetFile);
                return new UpdateDownloadResult(false, "Couldn't download the update. Check your connection and try again.", null);
            }
        }

        //-----------------------------
        //starts downloaded installer silently and shuts down widget
        public static void StartInstaller(string path, Action? shutdown = null)
        {
            var psi = new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART")
            {
                UseShellExecute = false
            };
            Process.Start(psi);
            //trigger shutdown
            if (shutdown != null)
            {
                shutdown();
            }
            else
            {
                Application.Current?.Shutdown();
            }
        }

        //-----------------------------
        //safely deletes file if present
        private static void TryDeleteFile(string path)
        {
            //check if file exists before deleting
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    //ignore deletion failure
                }
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
