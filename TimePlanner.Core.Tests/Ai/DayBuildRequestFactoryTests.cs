using TimePlanner.Core.Ai;
using TimePlanner.Core.Services.Models;
using Xunit;

namespace TimePlanner.Core.Tests.Ai
{
    //-----------------------------
    //unit tests for request factory assembly
    public class DayBuildRequestFactoryTests
    {
        //-----------------------------
        //assigns sequential identifiers to windows projects and activities
        [Fact]
        public void Create_AssignsIdentifiersInOrder()
        {
            var day = new DateOnly(2026, 10, 22);
            var notes = new[]
            {
                new VoiceNote
                {
                    Day = day,
                    Status = VoiceNoteStatus.Transcribed,
                    Transcript = "Note one",
                    WindowStart = new DateTime(2026, 10, 22, 9, 0, 0),
                    WindowEnd = new DateTime(2026, 10, 22, 10, 0, 0)
                },
                new VoiceNote
                {
                    Day = day,
                    Status = VoiceNoteStatus.Transcribed,
                    Transcript = "Note two",
                    WindowStart = new DateTime(2026, 10, 22, 10, 0, 0),
                    WindowEnd = new DateTime(2026, 10, 22, 11, 0, 0)
                }
            };

            var projects = new[]
            {
                ("Leading Edge (Internal)", "Internal"),
                ("Acme Foods", "Ordering App")
            };

            var activities = new[]
            {
                (IReadOnlyList<string>)new[] { "Coding" },
                (IReadOnlyList<string>)new[] { "Coding", "Bug fixing" }
            };

            var request = DayBuildRequestFactory.Create(day, notes, [], projects, activities);

            Assert.Equal("W1", request.Windows[0].Id);
            Assert.Equal("W2", request.Windows[1].Id);
            Assert.Equal("P1", request.Projects[0].Id);
            Assert.Equal("P2", request.Projects[1].Id);
            Assert.Equal("A1", request.Activities[0].Id);
            Assert.Equal("A2", request.Activities[1].Id);
        }

        //-----------------------------
        //ignores notes from other days or non transcribed status
        [Fact]
        public void Create_IncludesOnlyTranscribedNotesForTargetDay()
        {
            var targetDay = new DateOnly(2026, 10, 22);
            var otherDay = new DateOnly(2026, 10, 23);

            var notes = new[]
            {
                new VoiceNote
                {
                    Day = targetDay,
                    Status = VoiceNoteStatus.Transcribed,
                    Transcript = "Valid note",
                    WindowStart = new DateTime(2026, 10, 22, 9, 0, 0),
                    WindowEnd = new DateTime(2026, 10, 22, 10, 0, 0)
                },
                new VoiceNote
                {
                    Day = targetDay,
                    Status = VoiceNoteStatus.Recorded,
                    Transcript = null,
                    WindowStart = new DateTime(2026, 10, 22, 10, 0, 0),
                    WindowEnd = new DateTime(2026, 10, 22, 11, 0, 0)
                },
                new VoiceNote
                {
                    Day = otherDay,
                    Status = VoiceNoteStatus.Transcribed,
                    Transcript = "Different day",
                    WindowStart = new DateTime(2026, 10, 23, 9, 0, 0),
                    WindowEnd = new DateTime(2026, 10, 23, 10, 0, 0)
                }
            };

            var request = DayBuildRequestFactory.Create(targetDay, notes, [], [], []);

            Assert.Single(request.Windows);
            Assert.Equal("Valid note", request.Windows[0].Transcript);
        }

        //-----------------------------
        //breaks and entries subtract unlogged time from windows
        [Fact]
        public void Create_BreaksAndEntriesRemoveFreeTime()
        {
            var day = new DateOnly(2026, 10, 22);
            var notes = new[]
            {
                new VoiceNote
                {
                    Day = day,
                    Status = VoiceNoteStatus.Transcribed,
                    Transcript = "Work",
                    WindowStart = new DateTime(2026, 10, 22, 9, 0, 0),
                    WindowEnd = new DateTime(2026, 10, 22, 11, 0, 0)
                }
            };

            var slots = new[]
            {
                new TimesheetSlot(SlotKind.Entry, new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 9, 30, 0), 1, "C", "P", [], null, true),
                new TimesheetSlot(SlotKind.Break, new DateTime(2026, 10, 22, 10, 0, 0), new DateTime(2026, 10, 22, 10, 15, 0), null, "", "", [], null, false)
            };

            var request = DayBuildRequestFactory.Create(day, notes, slots, [], []);

            Assert.Single(request.Windows);
            var free = request.Windows[0].Free;
            Assert.Equal(2, free.Count);
            Assert.Equal(new DateTime(2026, 10, 22, 9, 30, 0), free[0].Start);
            Assert.Equal(new DateTime(2026, 10, 22, 10, 0, 0), free[0].End);
            Assert.Equal(new DateTime(2026, 10, 22, 10, 15, 0), free[1].Start);
            Assert.Equal(new DateTime(2026, 10, 22, 11, 0, 0), free[1].End);
        }

        //-----------------------------
        //fully logged window still included with empty free list
        [Fact]
        public void Create_FullyLoggedWindow_IncludedWithEmptyFreeTime()
        {
            var day = new DateOnly(2026, 10, 22);
            var notes = new[]
            {
                new VoiceNote
                {
                    Day = day,
                    Status = VoiceNoteStatus.Transcribed,
                    Transcript = "Already logged",
                    WindowStart = new DateTime(2026, 10, 22, 9, 0, 0),
                    WindowEnd = new DateTime(2026, 10, 22, 10, 0, 0)
                }
            };

            var slots = new[]
            {
                new TimesheetSlot(SlotKind.Entry, new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0), 1, "C", "P", [], null, true)
            };

            var request = DayBuildRequestFactory.Create(day, notes, slots, [], []);

            Assert.Single(request.Windows);
            Assert.Empty(request.Windows[0].Free);
        }
    }
}
//------------------------------EOF-----------------------------\\
