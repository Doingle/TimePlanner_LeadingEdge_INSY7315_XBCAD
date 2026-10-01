using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Services;
using TimePlanner.Core.Tests.Support;
using Xunit;

namespace TimePlanner.Core.Tests.Services
{
    //-----------------------------
    //unit tests for TimeEntryFactory
    public class TimeEntryFactoryTests : IDisposable
    {
        private readonly CoreTestHost _host;
        private readonly TimeEntryFactory _factory;

        public TimeEntryFactoryTests()
        {
            _host = new CoreTestHost();
            _factory = new TimeEntryFactory();
        }

        public void Dispose()
        {
            _host.Dispose();
        }

        //-----------------------------
        //manual method keeps times and note
        [Fact]
        public void CreateManual_SetsManualMethodAndNote()
        {
            var start = new DateTime(2026, 9, 28, 8, 0, 0);
            var end = start.AddHours(1);

            var entry = _factory.CreateManual(1, 2, start, end, "testing note");

            Assert.Equal(EntryMethod.Manual, entry.Method);
            Assert.Equal("testing note", entry.Note);
            Assert.Equal(start, entry.StartTime);
            Assert.Equal(end, entry.EndTime);
        }

        //-----------------------------
        //prompted method sets auto prompted method
        [Fact]
        public void CreateAutoPrompted_SetsPromptedMethod()
        {
            var start = new DateTime(2026, 9, 28, 8, 0, 0);
            var end = start.AddHours(1);

            var entry = _factory.CreateAutoPrompted(1, 2, start, end, "note");

            Assert.Equal(EntryMethod.AutoPrompted, entry.Method);
        }

        //-----------------------------
        //tracked method sets auto tracked method and null note
        [Fact]
        public void CreateAutoTracked_HasNoNote()
        {
            var start = new DateTime(2026, 9, 28, 8, 0, 0);
            var end = start.AddHours(1);

            var entry = _factory.CreateAutoTracked(1, 2, start, end);

            Assert.Equal(EntryMethod.AutoTracked, entry.Method);
            Assert.Null(entry.Note);
        }

        //-----------------------------
        //equal start and end throws an exception
        [Fact]
        public void Create_RejectsEndNotAfterStart()
        {
            var start = new DateTime(2026, 9, 28, 8, 0, 0);

            Assert.Throws<ArgumentException>(() => _factory.CreateManual(1, 2, start, start, "note"));
        }

        //-----------------------------
        //whitespace note becomes null
        [Fact]
        public void Create_StoresBlankNoteAsNull()
        {
            var start = new DateTime(2026, 9, 28, 8, 0, 0);
            var end = start.AddHours(1);

            var entry = _factory.CreateManual(1, 2, start, end, "   ");

            Assert.Null(entry.Note);
        }

        //-----------------------------
        //note over max length throws an exception
        [Fact]
        public void Create_RejectsTooLongNote()
        {
            var start = new DateTime(2026, 9, 28, 8, 0, 0);
            var end = start.AddHours(1);
            var longNote = new string('a', TimeEntryFactory.MaxNoteLength + 1);

            Assert.Throws<ArgumentException>(() => _factory.CreateManual(1, 2, start, end, longNote));
        }
    }
}
//------------------------------EOF-----------------------------\\
