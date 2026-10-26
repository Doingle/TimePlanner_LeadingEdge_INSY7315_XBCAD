using TimePlanner.Core.Ai;
using Xunit;

namespace TimePlanner.Core.Tests.Ai
{
    //-----------------------------
    //unit tests for free time calculation
    public class FreeTimeTests
    {
        //-----------------------------
        //returns entire window when no busy time exists
        [Fact]
        public void Within_NoBusyTime_ReturnsFullWindow()
        {
            var window = new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 30, 0));
            var free = FreeTime.Within(window, []);

            Assert.Single(free);
            Assert.Equal(window.Start, free[0].Start);
            Assert.Equal(window.End, free[0].End);
        }

        //-----------------------------
        //cuts busy time from start middle and end
        [Fact]
        public void Within_BusyAtStartMiddleAndEnd_LeavesExpectedSlices()
        {
            var window = new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 12, 0, 0));
            var busy = new[]
            {
                new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 9, 30, 0)),
                new TimeRange(new DateTime(2026, 10, 22, 10, 15, 0), new DateTime(2026, 10, 22, 10, 45, 0)),
                new TimeRange(new DateTime(2026, 10, 22, 11, 45, 0), new DateTime(2026, 10, 22, 12, 0, 0))
            };

            var free = FreeTime.Within(window, busy);

            Assert.Equal(2, free.Count);
            Assert.Equal(new DateTime(2026, 10, 22, 9, 30, 0), free[0].Start);
            Assert.Equal(new DateTime(2026, 10, 22, 10, 15, 0), free[0].End);
            Assert.Equal(new DateTime(2026, 10, 22, 10, 45, 0), free[1].Start);
            Assert.Equal(new DateTime(2026, 10, 22, 11, 45, 0), free[1].End);
        }

        //-----------------------------
        //busy time covering whole window returns empty
        [Fact]
        public void Within_BusyCoveringWholeWindow_ReturnsEmpty()
        {
            var window = new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 30, 0));
            var busy = new[]
            {
                new TimeRange(new DateTime(2026, 10, 22, 8, 30, 0), new DateTime(2026, 10, 22, 11, 0, 0))
            };

            var free = FreeTime.Within(window, busy);

            Assert.Empty(free);
        }

        //-----------------------------
        //pieces shorter than one minute are dropped
        [Fact]
        public void Within_PiecesUnderOneMinute_Dropped()
        {
            var window = new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0));
            var busy = new[]
            {
                new TimeRange(new DateTime(2026, 10, 22, 9, 0, 30), new DateTime(2026, 10, 22, 10, 0, 0))
            };

            var free = FreeTime.Within(window, busy);

            Assert.Empty(free);
        }

        //-----------------------------
        //intersect clips a piece across two separate free ranges
        [Fact]
        public void Intersect_AcrossTwoFreeRanges_ReturnsTwoPieces()
        {
            var free = new[]
            {
                new TimeRange(new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 9, 30, 0)),
                new TimeRange(new DateTime(2026, 10, 22, 10, 0, 0), new DateTime(2026, 10, 22, 11, 0, 0))
            };

            var piece = new TimeRange(new DateTime(2026, 10, 22, 9, 15, 0), new DateTime(2026, 10, 22, 10, 30, 0));
            var pieces = FreeTime.Intersect(piece, free);

            Assert.Equal(2, pieces.Count);
            Assert.Equal(new DateTime(2026, 10, 22, 9, 15, 0), pieces[0].Start);
            Assert.Equal(new DateTime(2026, 10, 22, 9, 30, 0), pieces[0].End);
            Assert.Equal(new DateTime(2026, 10, 22, 10, 0, 0), pieces[1].Start);
            Assert.Equal(new DateTime(2026, 10, 22, 10, 30, 0), pieces[1].End);
        }
    }
}
//------------------------------EOF-----------------------------\\
