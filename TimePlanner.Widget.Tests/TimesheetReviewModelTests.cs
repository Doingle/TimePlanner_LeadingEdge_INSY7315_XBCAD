using TimePlanner.Core.Ai;
using TimePlanner.Core.Services.Models;
using TimePlanner.Core.Sync;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Tests;

public class TimesheetReviewModelTests
{
    private static readonly DateOnly Day = new(2026, 10, 26);
    private static readonly DateTime SentAt = Day.ToDateTime(new TimeOnly(12, 0));

    private static TimesheetSlot Slot(int hour, SlotKind kind = SlotKind.Entry, int? id = 1) =>
        new(kind, Day.ToDateTime(new TimeOnly(hour, 0)), Day.ToDateTime(new TimeOnly(hour + 1, 0)),
            id, "Company", "Project", [], null, false);

    private static VoiceNote Note(VoiceNoteStatus status, int hour = 9) =>
        new() { Day = Day, Status = status, WindowStart = Day.ToDateTime(new TimeOnly(hour, 0)) };

    private static TimesheetReviewModel Create(IReadOnlyList<TimesheetSlot>? slots = null,
        SentDay? sent = null, PendingDay? pending = null, DateOnly? today = null, bool canSend = true) =>
        new(Day, today ?? Day, slots ?? [], new(Day, [], 1.5, canSend), sent, pending);

    [Theory]
    [InlineData(false, null, "Waiting to send · will retry")]
    [InlineData(true, null, "Not sent · the dashboard refused it")]
    [InlineData(true, "Invalid project", "Not sent · Invalid project")]
    public void PendingSendTakesPriorityOverAnEarlierChangedSend(bool rejected, string? message, string expected)
    {
        var model = Create(sent: new(Day, SentAt, 1, 1, SentAt.AddMinutes(1)),
            pending: new(Day, SentAt, 1, SentAt, rejected, message));
        Assert.Equal(expected, model.Status);
        Assert.Equal("Send again", model.SendLabel);
    }

    [Fact]
    public void UnsentDayUsesPreviewEligibility()
    {
        var model = Create(canSend: false);
        Assert.Equal("Not sent", model.Status);
        Assert.Equal("Send day", model.SendLabel);
        Assert.False(model.CanSend);
    }

    [Theory]
    [InlineData(-1, "Sent 12:00 PM")]
    [InlineData(0, "Sent 12:00 PM")]
    [InlineData(1, "Changed since sent · send again")]
    public void OnlyEditsAfterSendingMarkTheDayChanged(int minutes, string expected)
    {
        Assert.Equal(expected, Create(sent: new(Day, SentAt, 1, 1, SentAt.AddMinutes(minutes))).Status);
    }

    [Fact]
    public void EarlierSendIncludesItsDate()
    {
        Assert.Equal($"Sent {Format.Date(Day)}, 12:00 PM", Create(sent: new(Day, SentAt, 1, 1), today: Day.AddDays(1)).Status);
    }

    [Theory]
    [InlineData(0, "Today, Monday 26 Oct", false)]
    [InlineData(1, "Yesterday, Monday 26 Oct", true)]
    [InlineData(2, "Monday 26 Oct", true)]
    public void HeaderCountsOnlyLoggedEntriesAndControlsPastDayNavigation(int daysLater, string label, bool past)
    {
        var model = Create([Slot(9), Slot(10, SlotKind.Gap), Slot(11, SlotKind.Break)], today: Day.AddDays(daysLater));
        Assert.Equal($"{label} · 1h", model.Subtitle);
        Assert.Equal(past, model.IsPastDay);
    }

    [Fact]
    public void FailedNotesWinOverTranscribingUnbuiltAndAiEntries()
    {
        var failed = Note(VoiceNoteStatus.Failed);
        var model = Create();
        model.UpdateAi([Note(VoiceNoteStatus.Recorded), Note(VoiceNoteStatus.Transcribed), failed, Note(VoiceNoteStatus.Failed)], [1]);
        Assert.Equal("2 voice notes couldn't be turned into text", model.Banner!.Text);
        Assert.Equal("View", model.Banner.ButtonLabel);
        Assert.Equal(failed.Id, model.Banner.NoteId);
    }

    [Theory]
    [InlineData(VoiceNoteStatus.Failed, "1 voice note couldn't be turned into text", "View")]
    [InlineData(VoiceNoteStatus.Recorded, "Turning 1 voice note into text…", null)]
    [InlineData(VoiceNoteStatus.Transcribed, "1 voice note isn't in your timesheet yet", "Build my day")]
    [InlineData(VoiceNoteStatus.Built, "Check the AI entries before sending", null)]
    public void BannerReflectsNextStep(VoiceNoteStatus status, string text, string? button)
    {
        var model = Create();
        model.UpdateAi([Note(status)], [1]);
        Assert.Equal(text, model.Banner!.Text);
        Assert.Equal(button, model.Banner.ButtonLabel);
    }

    [Fact]
    public void TranscribingWinsOverBuildingAndUpdatesReplaceOldAiState()
    {
        var model = Create([Slot(9)]);
        model.UpdateAi([Note(VoiceNoteStatus.Transcribed), Note(VoiceNoteStatus.Recorded), Note(VoiceNoteStatus.Recorded)], [1]);
        Assert.Equal("Turning 2 voice notes into text…", model.Banner!.Text);
        Assert.True(model.IsAiEntry(Slot(9)));
        model.UpdateAi([Note(VoiceNoteStatus.Built)], []);
        Assert.Null(model.Banner);
        Assert.Single(model.Items);
        Assert.False(model.IsAiEntry(Slot(9)));
    }

    [Fact]
    public void ItemsMergeSlotsAndUnbuiltNotesChronologically()
    {
        var gap = Slot(11, SlotKind.Gap, null);
        var entry = Slot(9);
        var note = Note(VoiceNoteStatus.Transcribed, 10);
        var model = Create([gap, entry]);
        model.UpdateAi([Note(VoiceNoteStatus.Built, 8), note], []);
        Assert.Collection(model.Items,
            item => Assert.Same(entry, item.Slot),
            item => Assert.Same(note, item.Note),
            item => Assert.Same(gap, item.Slot));
        Assert.Empty(Create().Items);
    }

    [Theory]
    [InlineData(1, "1 AI-built entry", "1 voice note hasn't", "it")]
    [InlineData(2, "2 AI-built entries", "2 voice notes haven't", "them")]
    public void ConfirmationWarnsAboutOnlyDisplayedAiEntriesAndUnbuiltNotes(int count, string ai, string notes, string pronoun)
    {
        var slots = Enumerable.Range(1, count).Select(id => Slot(8 + id, id: id)).ToArray();
        var model = Create(slots);
        model.UpdateAi(Enumerable.Range(1, count).Select(_ => Note(VoiceNoteStatus.Transcribed))
            .Append(Note(VoiceNoteStatus.Built)).ToArray(), [1, 2, 999]);
        Assert.Contains($"This day includes {ai}.", model.SendConfirmation);
        Assert.Contains($"{notes} been added yet. Send without {pronoun}?", model.SendConfirmation);
        Assert.Contains("Send 0 entries (", model.SendConfirmation);
        Assert.DoesNotContain("AI", model.SentConfirmation);
    }

    [Fact]
    public void ConfirmationWithoutAiOrNotesHasNoWarnings()
    {
        var model = Create([Slot(9)]);
        model.UpdateAi([], [999]);
        Assert.Equal($"Send 0 entries ({1.5:0.##} h) for Monday, Oct 26 to the dashboard?", model.SendConfirmation);
        Assert.Equal($"Sent 0 entries ({1.5:0.##} h) for Monday, Oct 26 to the dashboard.", model.SentConfirmation);
    }

    [Fact]
    public void NewEntryStartsAfterLastEntryIgnoringTrailingGapOrAtNineWhenEmpty()
    {
        var range = TimesheetReviewModel.NewEntryRange(Day, [Slot(9), Slot(11), Slot(14, SlotKind.Gap)]);
        Assert.Equal(Day.ToDateTime(new TimeOnly(12, 0)), range.Start);
        Assert.Equal(range.Start.AddMinutes(30), range.End);
        var empty = TimesheetReviewModel.NewEntryRange(Day, []);
        Assert.Equal(Day.ToDateTime(new TimeOnly(9, 0)), empty.Start);
        Assert.Equal(empty.Start.AddMinutes(30), empty.End);
    }
}