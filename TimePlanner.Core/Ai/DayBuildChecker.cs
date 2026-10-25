using System.Globalization;
using System.Text.RegularExpressions;
using TimePlanner.Core.Services;

namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //validates and sanitises model suggestions
    public static class DayBuildChecker
    {
        //-----------------------------
        //checks model reply against free time and catalog
        public static (IReadOnlyList<SuggestedEntry> Entries, IReadOnlyList<BuildFlag> Flags) Check(DayBuildRequest request, ModelReply reply)
        {
            var windowMap = request.Windows.ToDictionary(w => w.Id);
            var projectMap = request.Projects.ToDictionary(p => p.Id);
            var activityMap = request.Activities.ToDictionary(a => a.Id);

            var flags = new List<BuildFlag>();

            //keep model flags matching known windows or null
            foreach (var f in reply.Flags)
            {
                //ignore empty message text
                if (string.IsNullOrWhiteSpace(f.Msg))
                {
                    continue;
                }

                //skip flags for unknown windows
                if (f.W != null && !windowMap.ContainsKey(f.W))
                {
                    continue;
                }

                var msg = f.Msg.Trim();
                //trim long messages to limit
                if (msg.Length > 160)
                {
                    msg = msg[..160];
                }

                flags.Add(new BuildFlag(f.W, msg));
            }

            var accepted = new List<SuggestedEntry>();
            var occupiedRanges = new List<TimeRange>();

            //validate each proposed entry
            foreach (var entry in reply.Entries)
            {
                //skip entries referencing an unknown window
                if (entry.W == null || !windowMap.TryGetValue(entry.W, out var window))
                {
                    continue;
                }

                var parsedStartOk = TryParseTime(entry.S, request.Day, out var startRaw);
                var parsedEndOk = TryParseTime(entry.E, request.Day, out var endRaw);
                var start = TimeRules.ToMinute(startRaw);
                var end = TimeRules.ToMinute(endRaw);
                var validTimeRange = parsedStartOk && parsedEndOk && end > start;

                var timeLabel = validTimeRange
                    ? $"{start:HH:mm}–{end:HH:mm}"
                    : $"{window.Start:HH:mm}–{window.End:HH:mm}";

                //check for unresolved project or activity
                var isP0 = string.Equals(entry.P, "P0", StringComparison.OrdinalIgnoreCase);
                var isA0 = string.Equals(entry.A, "A0", StringComparison.OrdinalIgnoreCase);
                CatalogProject? project = null;
                CatalogActivity? activity = null;
                var hasKnownProject = !isP0 && entry.P != null && projectMap.TryGetValue(entry.P, out project);
                var hasKnownActivity = !isA0 && entry.A != null && activityMap.TryGetValue(entry.A, out activity);

                //bad time syntax or backwards range flags the window
                if (!validTimeRange)
                {
                    //add invalid time flag if window has no flag yet
                    if (!flags.Any(f => f.WindowId == window.Id))
                    {
                        flags.Add(new BuildFlag(window.Id, $"Couldn't parse the time for {timeLabel}. Log it yourself."));
                    }

                    continue;
                }

                var wStart = TimeRules.ToMinute(window.Start);
                var wEnd = TimeRules.ToMinute(window.End);

                //clip proposed range to window bounds
                var clippedStart = start < wStart ? wStart : start;
                var clippedEnd = end > wEnd ? wEnd : end;

                //skip if clipped range is invalid
                if (clippedEnd <= clippedStart)
                {
                    continue;
                }

                var clippedPiece = new TimeRange(clippedStart, clippedEnd);
                var freePieces = FreeTime.Intersect(clippedPiece, window.Free);

                var note = CleanNote(entry.Note);

                //split across free spans and subtract earlier suggestions
                foreach (var freePiece in freePieces)
                {
                    var availablePieces = FreeTime.Within(freePiece, occupiedRanges);

                    //add each non overlapping slice
                    foreach (var piece in availablePieces)
                    {
                        var pStart = TimeRules.ToMinute(piece.Start);
                        var pEnd = TimeRules.ToMinute(piece.End);

                        //drop pieces shorter than one minute
                        if (pEnd - pStart < TimeSpan.FromMinutes(1))
                        {
                            continue;
                        }

                        var pieceRange = new TimeRange(pStart, pEnd);
                        accepted.Add(new SuggestedEntry(
                            window.Id,
                            pStart,
                            pEnd,
                            project != null ? [project.Company, project.Project] : Array.Empty<string>(),
                            activity != null ? activity.Path : Array.Empty<string>(),
                            note));

                        occupiedRanges.Add(pieceRange);
                    }
                }
            }

            //check each window for missing suggestions
            foreach (var w in request.Windows)
            {
                var hasEntry = accepted.Any(e => e.WindowId == w.Id);
                var hasFlag = flags.Any(f => f.WindowId == w.Id);

                //unhandled window with transcript gets a fallback flag
                if (!hasEntry && !hasFlag && !string.IsNullOrWhiteSpace(w.Transcript))
                {
                    flags.Add(new BuildFlag(w.Id, $"Nothing could be built for {w.Start:HH:mm}–{w.End:HH:mm}. Log it yourself."));
                }
            }

            //flag windows that had no free time
            foreach (var w in request.Windows)
            {
                //a window with no free time is already logged
                if (w.Free.Count == 0)
                {
                    flags.RemoveAll(f => f.WindowId == w.Id);
                    flags.Add(new BuildFlag(w.Id, $"{w.Start:HH:mm}–{w.End:HH:mm} is already logged, so this note was not used."));
                }
            }

            return (accepted.OrderBy(e => e.Start).ToList(), flags);
        }

        //-----------------------------
        //parses invariant time string into full datetime
        private static bool TryParseTime(string? text, DateOnly day, out DateTime dt)
        {
            dt = default;
            //empty text cannot be parsed
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var formats = new[] { "H:mm", "HH:mm" };
            //match either single or double digit hours
            if (DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                dt = day.ToDateTime(TimeOnly.FromDateTime(parsed));
                return true;
            }

            return false;
        }

        //-----------------------------
        //cleans leading punctuation whitespace and limits length
        private static string CleanNote(string? note)
        {
            //empty note returns blank string
            if (string.IsNullOrWhiteSpace(note))
            {
                return "";
            }

            var cleaned = Regex.Replace(note.Trim(), @"\s+", " ");
            cleaned = cleaned.TrimStart('-', '=', '+', '@').Trim();

            //return immediately when within size limit
            if (cleaned.Length <= AiLimits.Note)
            {
                return cleaned;
            }

            var max = AiLimits.Note - 1;
            var cut = cleaned[..max];
            var lastSpace = cut.LastIndexOf(' ');

            //break at word boundary when possible
            if (lastSpace > 0)
            {
                return cut[..lastSpace].TrimEnd() + "…";
            }

            return cut + "…";
        }
    }
}
//------------------------------EOF-----------------------------\\
