using System.IO;
using Whisper.net.Ggml;

namespace TimePlanner.Widget.Services.Ai
{
    //-----------------------------
    //whisper model descriptor for catalog displays
    public sealed record WhisperModelOption(string File, string Label, string About);

    //-----------------------------
    //catalog of supported local speech recognition models
    public static class WhisperModelCatalog
    {
        public static readonly IReadOnlyList<WhisperModelOption> Models =
        [
            new("ggml-base.en.bin", "Fast (base)", "about 150 MB"),
            new("ggml-small.en-q5_1.bin", "Balanced (small)", "about 190 MB"),
            new("ggml-large-v3-turbo-q5_0.bin", "Most accurate (large turbo)", "about 570 MB")
        ];
    }

    //-----------------------------
    //manages downloading and caching of ggml model binaries
    public sealed class WhisperModelManager
    {
        public string ModelsFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TimePlanner",
            "models");

        //-----------------------------
        //copies a model file the user already has
        public async Task ImportAsync(string sourcePath, string file, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                throw new InvalidOperationException($"Choose the file named {file}.");
            }

            var sourceFileName = Path.GetFileName(sourcePath);
            if (!string.Equals(sourceFileName, file, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Choose the file named {file}.");
            }

            var fileInfo = new FileInfo(sourcePath);
            if (fileInfo.Length < 50L * 1024 * 1024)
            {
                throw new InvalidOperationException($"Choose the file named {file}.");
            }

            Directory.CreateDirectory(ModelsFolder);

            var targetPath = Path.Combine(ModelsFolder, file);
            var partPath = Path.Combine(ModelsFolder, file + ".part");

            if (File.Exists(partPath))
            {
                try
                {
                    File.Delete(partPath);
                }
                catch (Exception)
                {
                }
            }

            try
            {
                using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var destinationStream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await sourceStream.CopyToAsync(destinationStream, ct);
                }

                File.Move(partPath, targetPath, overwrite: true);
            }
            catch (Exception)
            {
                if (File.Exists(partPath))
                {
                    try
                    {
                        File.Delete(partPath);
                    }
                    catch (Exception)
                    {
                    }
                }

                throw;
            }
        }

        //-----------------------------
        //checks whether model file exists locally
        public bool IsDownloaded(string file)
        {
            var path = Path.Combine(ModelsFolder, file);
            return File.Exists(path);
        }

        //-----------------------------
        //downloads model with progress and part file safety
        public async Task DownloadAsync(string file, IProgress<double>? progress, CancellationToken ct)
        {
            Directory.CreateDirectory(ModelsFolder);

            var targetPath = Path.Combine(ModelsFolder, file);
            var partPath = Path.Combine(ModelsFolder, file + ".part");

            var (ggmlType, quantType, expectedBytes) = ResolveModelParameters(file);

            //remove partial download from prior attempts
            if (File.Exists(partPath))
            {
                try
                {
                    File.Delete(partPath);
                }
                catch (Exception)
                {
                }
            }

            try
            {
                using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(ggmlType, quantType, ct);
                using (var fs = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    long totalRead = 0;
                    int read;

                    //stream chunks to disk updating progress
                    while ((read = await modelStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                    {
                        await fs.WriteAsync(buffer, 0, read, ct);
                        totalRead += read;

                        //report fraction of expected total
                        if (expectedBytes > 0 && progress != null)
                        {
                            var pct = Math.Min(1.0, (double)totalRead / expectedBytes);
                            progress.Report(pct);
                        }
                    }
                }

                File.Move(partPath, targetPath, overwrite: true);
                progress?.Report(1.0);
            }
            //clean up incomplete download on cancellation or failure
            catch (Exception)
            {
                //delete dangling partial file
                if (File.Exists(partPath))
                {
                    try
                    {
                        File.Delete(partPath);
                    }
                    catch (Exception)
                    {
                    }
                }

                throw;
            }
        }

        //-----------------------------
        //maps file name to whisper ggml download parameters
        private static (GgmlType Type, QuantizationType Quantization, long ExpectedBytes) ResolveModelParameters(string file)
        {
            return file switch
            {
                "ggml-base.en.bin" => (GgmlType.BaseEn, QuantizationType.NoQuantization, 140L * 1024 * 1024),
                "ggml-large-v3-turbo-q5_0.bin" => (GgmlType.LargeV3Turbo, QuantizationType.Q5_0, 550L * 1024 * 1024),
                _ => (GgmlType.SmallEn, QuantizationType.Q5_1, 190L * 1024 * 1024)
            };
        }
    }
}
//------------------------------EOF-----------------------------\\
