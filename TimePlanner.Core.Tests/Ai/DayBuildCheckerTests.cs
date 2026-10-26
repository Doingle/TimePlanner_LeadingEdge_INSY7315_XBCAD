using TimePlanner.Core.Ai;
using Xunit;

namespace TimePlanner.Core.Tests.Ai
{
    //-----------------------------
    //unit tests for suggestion checking and sanitisation
    public class DayBuildCheckerTests
    {
        private readonly DateOnly _day = new(2026, 10, 22);
        private readonly List<CatalogProject> _projects = [new("P1", "Acme", "App")];
        private readonly List<CatalogActivity> _activities = [new("A1", ["Coding"])];

        //-----------------------------
        //keeps stated time inside the window
        [Fact]
        public void Check_StatedTimeInsideWindow_IsKept()
        {
            var window = new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 30, 0),
                [new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 30, 0))], "update");

            var request = new DayBuildRequest(_day, [window], _projects, _activities);
            var reply = new ModelReply
            {
                Entries = [new() { W = "W1", S = "09:15", E = "10:00", P = "P1", A = "A1", Note = "Task" }]
            };

            var (entries, flags) = DayBuildChecker.Check(request, reply);

            Assert.Single(entries);
            Assert.Equal(new DateTime(2026, 10, 22, 9, 15, 0), entries[0].Start);
            Assert.Equal(new DateTime(2026, 10, 22, 10, 0, 0), entries[0].End);
            Assert.Empty(flags);
        }

        //-----------------------------
        //clips time extending beyond window boundaries
        [Fact]
        public void Check_TimeOutsideWindow_IsClipped()
        {
            var window = new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0),
                [new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0))], "update");

            var request = new DayBuildRequest(_day, [window], _projects, _activities);
            var reply = new ModelReply
            {
                Entries = [new() { W = "W1", S = "08:30", E = "10:30", P = "P1", A = "A1", Note = "Task" }]
            };

            var (entries, _) = DayBuildChecker.Check(request, reply);

            Assert.Single(entries);
            Assert.Equal(new DateTime(2026, 10, 22, 9, 0, 0), entries[0].Start);
            Assert.Equal(new DateTime(2026, 10, 22, 10, 0, 0), entries[0].End);
        }

        //-----------------------------
        //splits proposed entry around already logged busy time
        [Fact]
        public void Check_TimeAlreadyLogged_SplitsAroundBusyTime()
        {
            var window = new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 11, 0, 0),
                [
                    new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 9, 30, 0)),
                    new TimeRange(new DateTime(2026, 10, 22, 10, 30, 0), new DateTime(2026, 10, 22, 11, 0, 0))
                ], "update");

            var request = new DayBuildRequest(_day, [window], _projects, _activities);
            var reply = new ModelReply
            {
                Entries = [new() { W = "W1", S = "09:00", E = "11:00", P = "P1", A = "A1", Note = "Task" }]
            };

            var (entries, _) = DayBuildChecker.Check(request, reply);

            Assert.Equal(2, entries.Count);
            Assert.Equal(new DateTime(2026, 10, 22, 9, 0, 0), entries[0].Start);
            Assert.Equal(new DateTime(2026, 10, 22, 9, 30, 0), entries[0].End);
            Assert.Equal(new DateTime(2026, 10, 22, 10, 30, 0), entries[1].Start);
            Assert.Equal(new DateTime(2026, 10, 22, 11, 0, 0), entries[1].End);
        }

        //-----------------------------
        //trims later suggestion to avoid overlap with earlier suggestion
        [Fact]
        public void Check_OverlappingSuggestions_LaterOneTrimmed()
        {
            var window = new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 11, 0, 0),
                [new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 11, 0, 0))], "update");

            var request = new DayBuildRequest(_day, [window], _projects, _activities);
            var reply = new ModelReply
            {
                Entries =
                [
                    new() { W = "W1", S = "09:00", E = "10:15", P = "P1", A = "A1", Note = "First" },
                    new() { W = "W1", S = "10:00", E = "11:00", P = "P1", A = "A1", Note = "Second" }
                ]
            };

            var (entries, _) = DayBuildChecker.Check(request, reply);

            Assert.Equal(2, entries.Count);
            Assert.Equal(new DateTime(2026, 10, 22, 9, 0, 0), entries[0].Start);
            Assert.Equal(new DateTime(2026, 10, 22, 10, 15, 0), entries[0].End);
            Assert.Equal(new DateTime(2026, 10, 22, 10, 15, 0), entries[1].Start);
            Assert.Equal(new DateTime(2026, 10, 22, 11, 0, 0), entries[1].End);
        }

        //-----------------------------
        //unmatched project produces suggestion with empty project path and no flags
        [Fact]
        public void Check_P0OrUnknownId_ProducesUnmatchedSuggestion()
        {
            var window = new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0),
                [new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0))], "update");

            var request = new DayBuildRequest(_day, [window], _projects, _activities);
            var reply = new ModelReply
            {
                Entries = [new() { W = "W1", S = "09:00", E = "10:00", P = "P0", A = "A1", Note = "Task" }]
            };

            var (entries, flags) = DayBuildChecker.Check(request, reply);

            Assert.Single(entries);
            Assert.Empty(entries[0].ProjectPath);
            Assert.Empty(flags);
        }

        //-----------------------------
        //skips bad time text and adds flag
        [Fact]
        public void Check_BadTimeText_FlagsProblem()
        {
            var window = new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0),
                [new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0))], "update");

            var request = new DayBuildRequest(_day, [window], _projects, _activities);
            var reply = new ModelReply
            {
                Entries = [new() { W = "W1", S = "invalid", E = "10:00", P = "P1", A = "A1", Note = "Task" }]
            };

            var (entries, flags) = DayBuildChecker.Check(request, reply);

            Assert.Empty(entries);
            Assert.Single(flags);
            Assert.Contains("Couldn't parse the time", flags[0].Message);
        }

        //-----------------------------
        //cuts note over 500 characters at word boundary with ellipsis
        [Fact]
        public void Check_NoteOver500Chars_IsCut()
        {
            var window = new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0),
                [new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0))], "update");

            var longNote = string.Join(" ", Enumerable.Repeat("development", 60));
            var request = new DayBuildRequest(_day, [window], _projects, _activities);
            var reply = new ModelReply
            {
                Entries = [new() { W = "W1", S = "09:00", E = "10:00", P = "P1", A = "A1", Note = longNote }]
            };

            var (entries, _) = DayBuildChecker.Check(request, reply);

            Assert.Single(entries);
            Assert.True(entries[0].Note.Length <= AiLimits.Note);
            Assert.EndsWith("…", entries[0].Note);
        }

        //-----------------------------
        //removes leading punctuation like equals sign from note
        [Fact]
        public void Check_NoteStartingWithEquals_IsCleaned()
        {
            var window = new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0),
                [new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0))], "update");

            var request = new DayBuildRequest(_day, [window], _projects, _activities);
            var reply = new ModelReply
            {
                Entries = [new() { W = "W1", S = "09:00", E = "10:00", P = "P1", A = "A1", Note = "==+@ Worked on fixes" }]
            };

            var (entries, _) = DayBuildChecker.Check(request, reply);

            Assert.Single(entries);
            Assert.Equal("Worked on fixes", entries[0].Note);
        }

        //-----------------------------
        //fully logged window gets already logged flag
        [Fact]
        public void Check_FullyLoggedWindow_ReceivesAlreadyLoggedFlag()
        {
            var window = new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0),
                [], "update");

            var request = new DayBuildRequest(_day, [window], _projects, _activities);
            var reply = new ModelReply();

            var (entries, flags) = DayBuildChecker.Check(request, reply);

            Assert.Empty(entries);
            Assert.Single(flags);
            Assert.Contains("is already logged, so this note was not used", flags[0].Message);
        }

        //-----------------------------
        //empty reply produces nothing could be built flag
        [Fact]
        public void Check_EmptyReply_ReceivesNothingCouldBeBuiltFlag()
        {
            var window = new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0),
                [new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0))], "spoke something");

            var request = new DayBuildRequest(_day, [window], _projects, _activities);
            var reply = new ModelReply();

            var (entries, flags) = DayBuildChecker.Check(request, reply);

            Assert.Empty(entries);
            Assert.Single(flags);
            Assert.Contains("Nothing could be built", flags[0].Message);
        }
    }
}
//------------------------------EOF-----------------------------\\
