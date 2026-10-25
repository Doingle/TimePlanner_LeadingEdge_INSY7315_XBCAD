using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using TimePlanner.Core.Ai;
using TimePlanner.Core.Services.Models;
using Whisper.net;

namespace TimePlanner.AiEval
{
    //-----------------------------
    //evaluation harness for compilation and speech models
    public static class Program
    {
        //-----------------------------
        //entry point dispatching compile or transcribe command
        public static async Task Main(string[] args)
        {
            //require at least one command argument
            if (args.Length == 0)
            {
                PrintUsage();
                return;
            }

            var command = args[0].ToLowerInvariant();
            //execute selected subcommand
            switch (command)
            {
                case "compile":
                    await RunCompileAsync(args.Skip(1).ToArray());
                    break;

                case "transcribe":
                    await RunTranscribeAsync(args.Skip(1).ToArray());
                    break;

                default:
                    Console.WriteLine($"Unknown command: {command}");
                    PrintUsage();
                    break;
            }
        }

        //-----------------------------
        //prints command line parameter options
        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run --project tools/TimePlanner.AiEval -- compile --model <model> [--url <url>] [--runs <n>] [--golden <dir>] [--cpu]");
            Console.WriteLine("  dotnet run --project tools/TimePlanner.AiEval -- transcribe --model <path.bin> --audio <dir>");
        }

        //-----------------------------
        //evaluates day builder against golden dataset
        private static async Task RunCompileAsync(string[] args)
        {
            var cpuOnly = args.Any(a => string.Equals(a, "--cpu", StringComparison.OrdinalIgnoreCase));
            Console.WriteLine(cpuOnly ? "Mode: CPU only" : "Mode: Ollama default");

            var model = GetArg(args, "--model") ?? "qwen2.5:3b";
            var url = GetArg(args, "--url") ?? "http://localhost:11434";
            var runsStr = GetArg(args, "--runs") ?? "3";
            var goldenDir = GetArg(args, "--golden") ?? FindGoldenDir();
            var runs = int.TryParse(runsStr, out var r) ? r : 3;

            Console.WriteLine($"Evaluating model: {model} at {url} (runs: {runs})");
            Console.WriteLine($"Golden set directory: {goldenDir}");

            //ensure directory containing test days exists
            if (!Directory.Exists(goldenDir))
            {
                Console.WriteLine($"Golden set directory not found: {goldenDir}");
                return;
            }

            var files = Directory.GetFiles(goldenDir, "day-*.json").OrderBy(f => f).ToList();
            //require golden day fixtures
            if (files.Count == 0)
            {
                Console.WriteLine("No day-*.json files found");
                return;
            }

            var settings = new AiSettings
            {
                Compiler = CompilerKind.Ollama,
                CompilerModel = model,
                CompilerUrl = url,
                ContextTokens = 8192,
                GpuLayers = cpuOnly ? 0 : null
            };

            var prompt = DayPromptBuilder.Default;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
            var compiler = new OllamaDayCompiler(http, settings, prompt);

            Console.WriteLine();
            Console.WriteLine("| day | valid JSON % | project match % | activity match % (top level) | time overlap % | flags | avg seconds | avg output tokens |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|");

            double totalValid = 0;
            double totalProject = 0;
            double totalActivity = 0;
            double totalOverlap = 0;
            var totalFlags = 0;
            double totalSeconds = 0;
            double totalTokens = 0;
            var rowCount = 0;

            //process each golden day file
            foreach (var file in files)
            {
                var dayName = Path.GetFileNameWithoutExtension(file);
                var json = await File.ReadAllTextAsync(file);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var day = DateOnly.Parse(root.GetProperty("day").GetString()!);
                var projects = new List<(string Company, string Project)>();
                //load catalog projects
                foreach (var p in root.GetProperty("projects").EnumerateArray())
                {
                    projects.Add((p[0].GetString()!, p[1].GetString()!));
                }

                var activities = new List<IReadOnlyList<string>>();
                //load catalog activities
                foreach (var a in root.GetProperty("activities").EnumerateArray())
                {
                    activities.Add(a.EnumerateArray().Select(s => s.GetString()!).ToList());
                }

                var slots = new List<TimesheetSlot>();
                //load busy time slots
                if (root.TryGetProperty("busy", out var busyProp))
                {
                    //create break slot for each busy period
                    foreach (var b in busyProp.EnumerateArray())
                    {
                        var bStart = DateTime.Parse(b[0].GetString()!);
                        var bEnd = DateTime.Parse(b[1].GetString()!);
                        slots.Add(new TimesheetSlot(SlotKind.Break, bStart, bEnd, null, "", "", [], null, false));
                    }
                }

                var notes = new List<VoiceNote>();
                //load windows as transcribed notes
                foreach (var w in root.GetProperty("windows").EnumerateArray())
                {
                    var wStart = DateTime.Parse(w.GetProperty("start").GetString()!);
                    var wEnd = DateTime.Parse(w.GetProperty("end").GetString()!);
                    var transcript = w.GetProperty("transcript").GetString()!;

                    notes.Add(new VoiceNote
                    {
                        Day = day,
                        WindowStart = wStart,
                        WindowEnd = wEnd,
                        Transcript = transcript,
                        Status = VoiceNoteStatus.Transcribed
                    });
                }

                var expectedEntries = new List<GoldenEntry>();
                //load expected timesheet entries
                foreach (var exp in root.GetProperty("expected").EnumerateArray())
                {
                    var sTime = ParseDayTime(day, exp.GetProperty("start").GetString()!);
                    var eTime = ParseDayTime(day, exp.GetProperty("end").GetString()!);
                    var prj = exp.GetProperty("project").GetString()!;
                    var act = exp.GetProperty("activity").GetString()!;
                    expectedEntries.Add(new GoldenEntry(sTime, eTime, prj, act));
                }

                var request = DayBuildRequestFactory.Create(day, notes, slots, projects, activities);

                var runValid = 0;
                double runProjectMatches = 0;
                double runActivityMatches = 0;
                double runOverlapPercent = 0;
                var runFlags = 0;
                double runSeconds = 0;
                double runTokens = 0;

                //repeat evaluation runs
                for (var i = 0; i < runs; i++)
                {
                    DayBuildResult result;
                    try
                    {
                        result = await compiler.BuildAsync(request, CancellationToken.None);
                        runValid++;
                    }
                    //catch failed compilation calls
                    catch (Exception)
                    {
                        continue;
                    }

                    runFlags += result.Flags.Count;
                    runSeconds += result.Took.TotalSeconds;
                    runTokens += result.OutputTokens ?? 0;

                    var dayOverlap = CalculateOverlap(expectedEntries, result.Entries);
                    runOverlapPercent += dayOverlap;

                    var dayProjectMatch = CalculateProjectMatch(expectedEntries, result.Entries);
                    runProjectMatches += dayProjectMatch;

                    var dayActivityMatch = CalculateActivityMatch(expectedEntries, result.Entries);
                    runActivityMatches += dayActivityMatch;
                }

                var validPct = runs > 0 ? (runValid * 100.0 / runs) : 0;
                var projPct = runValid > 0 ? (runProjectMatches / runValid) : 0;
                var actPct = runValid > 0 ? (runActivityMatches / runValid) : 0;
                var overlapPct = runValid > 0 ? (runOverlapPercent / runValid) : 0;
                var avgSec = runValid > 0 ? (runSeconds / runValid) : 0;
                var avgTok = runValid > 0 ? (runTokens / runValid) : 0;

                Console.WriteLine($"| {dayName} | {validPct:F0}% | {projPct:F0}% | {actPct:F0}% | {overlapPct:F0}% | {runFlags} | {avgSec:F1}s | {avgTok:F0} |");

                totalValid += validPct;
                totalProject += projPct;
                totalActivity += actPct;
                totalOverlap += overlapPct;
                totalFlags += runFlags;
                totalSeconds += avgSec;
                totalTokens += avgTok;
                rowCount++;
            }

            //print summary row when multiple rows exist
            if (rowCount > 0)
            {
                var avgValid = totalValid / rowCount;
                var avgProj = totalProject / rowCount;
                var avgAct = totalActivity / rowCount;
                var avgOverlap = totalOverlap / rowCount;
                var avgSec = totalSeconds / rowCount;
                var avgTok = totalTokens / rowCount;

                Console.WriteLine($"| TOTAL | {avgValid:F0}% | {avgProj:F0}% | {avgAct:F0}% | {avgOverlap:F0}% | {totalFlags} | {avgSec:F1}s | {avgTok:F0} |");
            }
        }

        //-----------------------------
        //evaluates whisper transcription on test audio
        private static async Task RunTranscribeAsync(string[] args)
        {
            var modelPath = GetArg(args, "--model");
            var audioDir = GetArg(args, "--audio") ?? Path.Combine(FindGoldenDir(), "audio");

            //require path to ggml whisper model file
            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            {
                Console.WriteLine("Whisper model file not found. Specify --model <path to ggml bin>");
                return;
            }

            //require audio folder
            if (!Directory.Exists(audioDir))
            {
                Console.WriteLine($"Audio directory not found: {audioDir}");
                return;
            }

            var wavFiles = Directory.GetFiles(audioDir, "*.wav");
            //warn when audio folder is empty
            if (wavFiles.Length == 0)
            {
                Console.WriteLine("No .wav files found in audio directory");
                return;
            }

            Console.WriteLine($"Transcribing with model {Path.GetFileName(modelPath)}");
            using var factory = WhisperFactory.FromPath(modelPath);
            var threads = Math.Max(1, Environment.ProcessorCount / 2);

            Console.WriteLine("| file | seconds | WER |");
            Console.WriteLine("|---|---|---|");

            //transcribe each wav file
            foreach (var wav in wavFiles)
            {
                var txtPath = Path.ChangeExtension(wav, ".txt");
                var reference = File.Exists(txtPath) ? await File.ReadAllTextAsync(txtPath) : "";

                var sw = Stopwatch.StartNew();
                using var processor = factory.CreateBuilder()
                    .WithLanguage("en")
                    .WithThreads(threads)
                    .Build();

                using var stream = File.OpenRead(wav);
                var segments = new List<string>();

                //read all transcribed segments
                await foreach (var segment in processor.ProcessAsync(stream))
                {
                    segments.Add(segment.Text);
                }

                sw.Stop();
                var transcript = string.Join(" ", segments);
                var wer = CalculateWer(reference, transcript);

                Console.WriteLine($"| {Path.GetFileName(wav)} | {sw.Elapsed.TotalSeconds:F1}s | {wer * 100:F1}% |");
            }
        }

        //-----------------------------
        //calculates time overlap percentage between expected and suggestions
        private static double CalculateOverlap(List<GoldenEntry> expected, IReadOnlyList<SuggestedEntry> actual)
        {
            var totalExpectedMinutes = expected.Sum(e => (e.End - e.Start).TotalMinutes);
            //avoid division by zero
            if (totalExpectedMinutes <= 0)
            {
                return 100.0;
            }

            double coveredMinutes = 0;

            //compare each expected entry
            foreach (var exp in expected)
            {
                //find suggestions matching expected project
                var matching = actual.Where(a =>
                    $"{a.ProjectPath[0]} › {a.ProjectPath[1]}" == exp.Project ||
                    exp.Project.Contains(a.ProjectPath[1])).ToList();

                //accumulate overlap minutes
                foreach (var m in matching)
                {
                    var oStart = exp.Start > m.Start ? exp.Start : m.Start;
                    var oEnd = exp.End < m.End ? exp.End : m.End;

                    //add overlap span when valid
                    if (oEnd > oStart)
                    {
                        coveredMinutes += (oEnd - oStart).TotalMinutes;
                    }
                }
            }

            return Math.Min(100.0, (coveredMinutes / totalExpectedMinutes) * 100.0);
        }

        //-----------------------------
        //calculates project matching percentage
        private static double CalculateProjectMatch(List<GoldenEntry> expected, IReadOnlyList<SuggestedEntry> actual)
        {
            //empty expected entries return full match
            if (expected.Count == 0)
            {
                return 100.0;
            }

            var matches = 0;
            //check project equality
            foreach (var exp in expected)
            {
                //find matching project in suggestions
                if (actual.Any(a => $"{a.ProjectPath[0]} › {a.ProjectPath[1]}" == exp.Project || exp.Project.Contains(a.ProjectPath[1])))
                {
                    matches++;
                }
            }

            return (matches * 100.0) / expected.Count;
        }

        //-----------------------------
        //calculates top level activity matching percentage
        private static double CalculateActivityMatch(List<GoldenEntry> expected, IReadOnlyList<SuggestedEntry> actual)
        {
            //empty expected entries return full match
            if (expected.Count == 0)
            {
                return 100.0;
            }

            var matches = 0;
            //check top level activity prefix
            foreach (var exp in expected)
            {
                var topExpected = exp.Activity.Split('›')[0].Trim();
                //find matching activity
                if (actual.Any(a => a.ActivityPath.Count > 0 && a.ActivityPath[0].Equals(topExpected, StringComparison.OrdinalIgnoreCase)))
                {
                    matches++;
                }
            }

            return (matches * 100.0) / expected.Count;
        }

        //-----------------------------
        //calculates word error rate using edit distance
        private static double CalculateWer(string reference, string hypothesis)
        {
            var refWords = CleanWords(reference);
            var hypWords = CleanWords(hypothesis);

            //handle empty reference word count
            if (refWords.Length == 0)
            {
                return hypWords.Length == 0 ? 0 : 1.0;
            }

            var d = new int[refWords.Length + 1, hypWords.Length + 1];

            //initialize distance matrix rows
            for (var i = 0; i <= refWords.Length; i++)
            {
                d[i, 0] = i;
            }

            //initialize distance matrix columns
            for (var j = 0; j <= hypWords.Length; j++)
            {
                d[0, j] = j;
            }

            //fill dynamic programming matrix
            for (var i = 1; i <= refWords.Length; i++)
            {
                //compute minimum edit cost
                for (var j = 1; j <= hypWords.Length; j++)
                {
                    var cost = refWords[i - 1] == hypWords[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return (double)d[refWords.Length, hypWords.Length] / refWords.Length;
        }

        //-----------------------------
        //normalizes text into clean lowercase words
        private static string[] CleanWords(string text)
        {
            var clean = Regex.Replace(text.ToLowerInvariant(), @"[^\w\s]", "");
            return clean.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        //-----------------------------
        //parses time string into full datetime on specified day
        private static DateTime ParseDayTime(DateOnly day, string time)
        {
            var t = TimeOnly.Parse(time);
            return day.ToDateTime(t);
        }

        //-----------------------------
        //locates the golden fixtures directory
        private static string FindGoldenDir()
        {
            var fromCwd = Path.Combine(Directory.GetCurrentDirectory(), "tools", "TimePlanner.AiEval", "golden");
            if (Directory.Exists(fromCwd))
            {
                return fromCwd;
            }

            var fromBase = Path.Combine(AppContext.BaseDirectory, "golden");
            if (Directory.Exists(fromBase))
            {
                return fromBase;
            }

            var current = AppContext.BaseDirectory;
            //search upwards for tools golden directory
            for (var i = 0; i < 5; i++)
            {
                var candidate = Path.Combine(current, "tools", "TimePlanner.AiEval", "golden");
                //return candidate if existing
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                var parent = Path.GetDirectoryName(current);
                //stop at drive root
                if (string.IsNullOrEmpty(parent))
                {
                    break;
                }

                current = parent;
            }

            return fromBase;
        }

        //-----------------------------
        //retrieves named command line argument value
        private static string? GetArg(string[] args, string name)
        {
            var index = Array.IndexOf(args, name);
            //return following token if available
            if (index >= 0 && index + 1 < args.Length)
            {
                return args[index + 1];
            }

            return null;
        }

        //-----------------------------
        //golden timesheet entry expected record
        private sealed record GoldenEntry(DateTime Start, DateTime End, string Project, string Activity);
    }
}
//------------------------------EOF-----------------------------\\
