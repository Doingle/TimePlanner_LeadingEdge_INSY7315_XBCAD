using System;
using System.Collections.Generic;
using System.Linq;
using TimePlanner.Core.Ai;
using TimePlanner.Core.Services.Models;
using TimePlanner.Core.Sync;

namespace TimePlanner.Widget.Models
{
    // Presentation decisions for a day's review, independent of WPF controls and navigation.
    public sealed class TimesheetReviewModel
    {
        private readonly DateOnly _day;
        private readonly IReadOnlyList<TimesheetSlot> _slots;
        private readonly DayPreview _preview;
        private HashSet<int> _aiEntryIds = new();
        private IReadOnlyList<VoiceNote> _allNotes = Array.Empty<VoiceNote>();
        private List<VoiceNote> _unbuiltNotes = new();

        public TimesheetReviewModel(DateOnly day, DateOnly today, IReadOnlyList<TimesheetSlot> slots,
            DayPreview preview, SentDay? sent, PendingDay? pending = null)
        {
            _day = day;
            _slots = slots;
            _preview = preview;
            IsPastDay = day < today;

            var totalLogged = slots.Where(s => s.Kind == SlotKind.Entry).Sum(s => (s.End - s.Start).TotalMinutes);
            Subtitle = $"{DayLabel(day, today)} · {Format.Duration(TimeSpan.FromMinutes(totalLogged))}";

            //sets send status caption. A send that is still on its way comes first, whatever was sent before
            if (pending is { Rejected: false })
            {
                Status = "Waiting to send · will retry";
            }
            else if (pending is { Rejected: true })
            {
                Status = $"Not sent · {pending.Message ?? "the dashboard refused it"}";
            }
            else if (sent == null)
            {
                Status = "Not sent";
            }
            else if (sent.ChangedAt > sent.SentAt)
            {
                Status = "Changed since sent · send again";
            }
            else
            {
                Status = DateOnly.FromDateTime(sent.SentAt) == today
                    ? $"Sent {Format.Clock(sent.SentAt)}"
                    : $"Sent {Format.Date(DateOnly.FromDateTime(sent.SentAt))}, {Format.Clock(sent.SentAt)}";
            }

            //a day sent before goes again, which replaces it on the dashboard
            SendLabel = sent != null ? "Send again" : "Send day";

            //the date bar: the day on its button, the total, and the send state as a short badge that warns until the day is sent
            DayText = day.ToString("ddd d MMM", System.Globalization.CultureInfo.InvariantCulture);
            LongDayText = Format.Date(day);
            Total = Format.Duration(TimeSpan.FromMinutes(totalLogged));
            (Badge, BadgeWarns) = pending is { Rejected: false } ? ("Waiting to send", true)
                : pending is { Rejected: true } ? ("Not sent", true)
                : sent == null ? ("Not sent yet", true)
                : sent.ChangedAt > sent.SentAt ? ("Changed since sent", true)
                : (Status, false);
            ShowsStatus = pending != null;
        }

        // A send on its way or refused has more to say than its badge, so its full status shows under the date bar
        public bool ShowsStatus { get; }

        public string Subtitle { get; }
        public string Status { get; }
        public string DayText { get; }
        public string LongDayText { get; }
        public string Total { get; }
        public string Badge { get; }
        public bool BadgeWarns { get; }
        public string SendLabel { get; }
        public bool IsPastDay { get; }
        public bool CanSend => _preview.CanSend;
        private static string DayLabel(DateOnly day, DateOnly today) =>
            (today.DayNumber - day.DayNumber) switch
            {
                0 => $"Today, {Format.Date(day)}",
                1 => $"Yesterday, {Format.Date(day)}",
                _ => Format.Date(day),
            };

        public void UpdateAi(IReadOnlyList<VoiceNote> notes, IEnumerable<int> aiEntryIds)
        {
            _allNotes = notes;
            _unbuiltNotes = notes.Where(n => n.Status != VoiceNoteStatus.Built).ToList();
            _aiEntryIds = aiEntryIds.ToHashSet();
        }

        public bool IsAiEntry(TimesheetSlot slot) => slot.EntryId.HasValue && _aiEntryIds.Contains(slot.EntryId.Value);

        public ReviewBanner? Banner
        {
            get
            {
                var failed = _allNotes.FirstOrDefault(n => n.Status == VoiceNoteStatus.Failed);
                if (failed != null)
                {
                    var count = _allNotes.Count(n => n.Status == VoiceNoteStatus.Failed);
                    return new(count == 1 ? "1 voice note couldn't be turned into text" : $"{count} voice notes couldn't be turned into text",
                        "View", "Open this voice note", failed.Id);
                }

                var transcribing = _allNotes.Count(n => n.Status == VoiceNoteStatus.Recorded);
                if (transcribing > 0)
                    return new(transcribing == 1 ? "Turning 1 voice note into text…" : $"Turning {transcribing} voice notes into text…");

                var unbuilt = _allNotes.Count(n => n.Status == VoiceNoteStatus.Transcribed);
                if (unbuilt > 0)
                    return new(unbuilt == 1 ? "1 voice note isn't in your timesheet yet" : $"{unbuilt} voice notes aren't in your timesheet yet",
                        "Build my day", "Turn today's voice notes into timesheet entries. You check them before anything is added.");

                return _aiEntryIds.Count > 0 ? new("Check the AI entries before sending") : null;
            }
        }

        public IReadOnlyList<(DateTime Start, TimesheetSlot? Slot, VoiceNote? Note)> Items
        {
            get
            {
                var items = new List<(DateTime Start, TimesheetSlot? Slot, VoiceNote? Note)>();
                foreach (var slot in _slots)
                {
                    items.Add((slot.Start, slot, null));
                }
                foreach (var note in _unbuiltNotes)
                {
                    items.Add((note.WindowStart, null, note));
                }
                items.Sort((a, b) => a.Start.CompareTo(b.Start));

                return items;
            }
        }

        public string SendConfirmation
        {
            get
            {
                var aiCount = _slots.Count(s => s.EntryId.HasValue && _aiEntryIds.Contains(s.EntryId.Value));
                var unbuiltCount = _unbuiltNotes.Count;

                var sb = new System.Text.StringBuilder();

                //warn about ai entries
                if (aiCount > 0)
                {
                    var entryWord = aiCount == 1 ? "AI-built entry" : "AI-built entries";
                    sb.AppendLine($"This day includes {aiCount} {entryWord}. AI can be inaccurate, so check them before sending.");
                }

                sb.Append($"Send {_preview.Rows.Count} entries ({_preview.Hours:0.##} h) for {Format.Day(_day.ToDateTime(TimeOnly.MinValue))} to the dashboard?");

                //warn about unbuilt notes
                if (unbuiltCount > 0)
                {
                    var noteWord = unbuiltCount == 1 ? "voice note hasn't" : "voice notes haven't";
                    var themWord = unbuiltCount == 1 ? "it" : "them";
                    sb.AppendLine();
                    sb.Append($"{unbuiltCount} {noteWord} been added yet. Send without {themWord}?");
                }

                return sb.ToString();
            }
        }

        public string SentConfirmation =>
            $"Sent {_preview.Rows.Count} entries ({_preview.Hours:0.##} h) for {Format.Day(_day.ToDateTime(TimeOnly.MinValue))} to the dashboard.";

        public static (DateTime Start, DateTime End) NewEntryRange(DateOnly day, IReadOnlyList<TimesheetSlot> slots)
        {
            var start = slots.LastOrDefault(s => s.Kind == SlotKind.Entry)?.End ?? day.ToDateTime(new TimeOnly(9, 0));
            return (start, start.AddMinutes(30));
        }
    }

    public sealed record ReviewBanner(string Text, string? ButtonLabel = null, string? ToolTip = null, Guid? NoteId = null);
}