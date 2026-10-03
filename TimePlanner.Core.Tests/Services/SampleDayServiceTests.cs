using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Services;
using TimePlanner.Core.Services.Models;
using TimePlanner.Core.Tests.Support;
using Xunit;

namespace TimePlanner.Core.Tests.Services
{
    //-----------------------------
    //unit and integration tests for loading sample day csv files
    public class SampleDayServiceTests : IDisposable
    {
        private readonly CoreTestHost _host;

        public SampleDayServiceTests()
        {
            _host = new CoreTestHost();
        }

        public void Dispose()
        {
            _host.Dispose();
        }

        //-----------------------------
        //reading sample day csv returns nine parsed rows
        [Fact]
        public void Read_ReturnsNineRows()
        {
            using var reader = new StreamReader("SampleData/sample-day.csv");
            var rows = SampleDayService.Read(reader);

            Assert.Equal(9, rows.Count);
        }

        //-----------------------------
        //sample file values agree with app duration rounding and billing rules
        [Fact]
        public void File_AgreesWithAppRules()
        {
            using var reader = new StreamReader("SampleData/sample-day.csv");
            var rows = SampleDayService.Read(reader);

            //verifies each row duration and billable label match business logic
            foreach (var row in rows)
            {
                var expectedHours = Math.Round((row.End - row.Start).TotalHours, 2);
                Assert.Equal(expectedHours, row.Hours);

                var (company, _) = SampleDayService.SplitClientProject(row.ClientProject);
                var rootActivity = row.ActivityPath.Split(new[] { " > " }, StringSplitOptions.None)[0];
                var rootBillable = rootActivity != "Learning";

                string expectedBillable;

                //if checks whether the row belongs to the internal company
                if (BillingRules.IsInternal(company))
                {
                    expectedBillable = "Internal";
                }
                else if (BillingRules.IsBillable(company, rootBillable))
                {
                    expectedBillable = "Yes";
                }
                else
                {
                    expectedBillable = "No";
                }

                Assert.Equal(expectedBillable, row.Billable);
            }
        }

        //-----------------------------
        //splitting client project text parses company and project names correctly
        [Fact]
        public void SplitClientProject_SplitsCorrectly()
        {
            var (c1, p1) = SampleDayService.SplitClientProject("Acme Logistics / Fleet Portal");
            Assert.Equal("Acme Logistics", c1);
            Assert.Equal("Fleet Portal", p1);

            var (c2, p2) = SampleDayService.SplitClientProject("Leading Edge (Internal)");
            Assert.Equal("Leading Edge (Internal)", c2);
            Assert.Equal("Internal", p2);
        }

        //-----------------------------
        //calculating target day returns the most recent Monday to Friday before now
        [Fact]
        public void TargetDay_ReturnsPreviousWeekday()
        {
            var monday = SampleDayService.TargetDay(new DateTime(2026, 10, 5, 10, 0, 0));
            Assert.Equal(new DateOnly(2026, 10, 2), monday);

            var wednesday = SampleDayService.TargetDay(new DateTime(2026, 10, 7, 10, 0, 0));
            Assert.Equal(new DateOnly(2026, 10, 6), wednesday);

            var sunday = SampleDayService.TargetDay(new DateTime(2026, 10, 4, 10, 0, 0));
            Assert.Equal(new DateOnly(2026, 10, 2), sunday);
        }

        //-----------------------------
        //loading sample day creates entries breaks and gaps in the database
        [Fact]
        public async Task LoadAsync_LoadsNineEntriesAndCalculatesBreaksAndGaps()
        {
            var now = new DateTime(2026, 10, 5, 10, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", now);

            var service = scope.ServiceProvider.GetRequiredService<SampleDayService>();
            var result = await service.LoadAsync(user.UserId, "SampleData/sample-day.csv", now);

            Assert.True(result.Loaded);
            Assert.Equal(9, result.Entries);
            Assert.Equal(new DateOnly(2026, 10, 2), result.Day);

            var editService = scope.ServiceProvider.GetRequiredService<TimesheetEditService>();
            var slots = await editService.GetDayAsync(user.UserId, result.Day, now);

            var entries = slots.Where(s => s.Kind == SlotKind.Entry).ToList();
            var breaks = slots.Where(s => s.Kind == SlotKind.Break).ToList();
            var gaps = slots.Where(s => s.Kind == SlotKind.Gap).ToList();

            Assert.Equal(9, entries.Count);
            Assert.Single(breaks);
            Assert.Single(gaps);

            var lunchBreak = breaks[0];
            Assert.Equal(new DateTime(2026, 10, 2, 12, 30, 0), lunchBreak.Start);
            Assert.Equal(new DateTime(2026, 10, 2, 13, 15, 0), lunchBreak.End);

            var shortGap = gaps[0];
            Assert.Equal(new DateTime(2026, 10, 2, 11, 15, 0), shortGap.Start);
            Assert.Equal(new DateTime(2026, 10, 2, 11, 25, 0), shortGap.End);
        }

        //-----------------------------
        //loading sample day a second time is rejected without duplicating entries
        [Fact]
        public async Task LoadAsync_SecondTimeReturnsLoadedFalse()
        {
            var now = new DateTime(2026, 10, 5, 10, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", now);

            var service = scope.ServiceProvider.GetRequiredService<SampleDayService>();
            var result1 = await service.LoadAsync(user.UserId, "SampleData/sample-day.csv", now);
            Assert.True(result1.Loaded);

            var result2 = await service.LoadAsync(user.UserId, "SampleData/sample-day.csv", now);
            Assert.False(result2.Loaded);
            Assert.Contains("already has entries", result2.Message);
        }

        //-----------------------------
        //loading missing csv file returns loaded false with error message
        [Fact]
        public async Task LoadAsync_MissingFileReturnsLoadedFalse()
        {
            var now = new DateTime(2026, 10, 5, 10, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", now);

            var service = scope.ServiceProvider.GetRequiredService<SampleDayService>();
            var result = await service.LoadAsync(user.UserId, "missing-file.csv", now);

            Assert.False(result.Loaded);
            Assert.Equal("The sample day file was not found.", result.Message);
        }
    }
}
//------------------------------EOF-----------------------------\\
