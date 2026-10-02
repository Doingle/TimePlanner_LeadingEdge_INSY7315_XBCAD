using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Repositories.Interfaces;
using TimePlanner.Core.Services;
using TimePlanner.Core.Services.Models;
using TimePlanner.Core.Tests.Support;
using Xunit;

namespace TimePlanner.Core.Tests.Services
{
    //-----------------------------
    //unit and integration tests for timesheet edit service
    public class TimesheetEditServiceTests : IDisposable
    {
        private readonly CoreTestHost _host;

        public TimesheetEditServiceTests()
        {
            _host = new CoreTestHost();
        }

        public void Dispose()
        {
            _host.Dispose();
        }

        //-----------------------------
        //timesheet day lists entries gaps and breaks in order
        [Fact]
        public async Task GetDay_ShowsEntryGapAndBreak()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            var now = new DateTime(2026, 9, 28, 17, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var dayService = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await dayService.StartDayAsync(user.UserId, baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var activeProjects = await entryService.GetActiveProjectsAsync();
            var internalProj = activeProjects.First();

            await entryService.LogEntryAsync(new LogEntryRequest(user.UserId, internalProj.ProjectID, 2, "work", EntryMethod.Manual, baseTime.AddHours(1)));

            await dayService.PauseAsync(user.UserId, baseTime.AddHours(1.5));
            await dayService.ResumeAsync(user.UserId, baseTime.AddHours(2.0));

            var editService = scope.ServiceProvider.GetRequiredService<TimesheetEditService>();
            await editService.AddEntryAsync(user.UserId, new EntryEdit(baseTime.AddHours(2.0), baseTime.AddHours(3.0), ["Leading Edge (Internal)", "TimePlanner"], ["Coding"], null), now);

            await dayService.EndDayAsync(user.UserId, baseTime.AddHours(4.0));

            var day = DateOnly.FromDateTime(baseTime);
            var slots = await editService.GetDayAsync(user.UserId, day, now);

            Assert.Equal(5, slots.Count);
            Assert.Equal(SlotKind.Entry, slots[0].Kind);
            Assert.Equal(baseTime, slots[0].Start);
            Assert.Equal(baseTime.AddHours(1), slots[0].End);

            Assert.Equal(SlotKind.Gap, slots[1].Kind);
            Assert.Equal(baseTime.AddHours(1), slots[1].Start);
            Assert.Equal(baseTime.AddHours(1.5), slots[1].End);

            Assert.Equal(SlotKind.Break, slots[2].Kind);
            Assert.Equal(baseTime.AddHours(1.5), slots[2].Start);
            Assert.Equal(baseTime.AddHours(2.0), slots[2].End);

            Assert.Equal(SlotKind.Entry, slots[3].Kind);
            Assert.Equal(baseTime.AddHours(2.0), slots[3].Start);
            Assert.Equal(baseTime.AddHours(3.0), slots[3].End);

            Assert.Equal(SlotKind.Gap, slots[4].Kind);
            Assert.Equal(baseTime.AddHours(3.0), slots[4].Start);
            Assert.Equal(baseTime.AddHours(4.0), slots[4].End);
        }

        //-----------------------------
        //gaps shorter than five minutes are ignored
        [Fact]
        public async Task GetDay_IgnoresGapsUnderFiveMinutes()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            var now = new DateTime(2026, 9, 28, 17, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var dayService = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await dayService.StartDayAsync(user.UserId, baseTime);

            var editService = scope.ServiceProvider.GetRequiredService<TimesheetEditService>();
            await editService.AddEntryAsync(user.UserId, new EntryEdit(baseTime.AddMinutes(3), baseTime.AddHours(1), ["Leading Edge (Internal)", "TimePlanner"], ["Coding"], null), now);

            var slots = await editService.GetDayAsync(user.UserId, DateOnly.FromDateTime(baseTime), now);
            Assert.DoesNotContain(slots, s => s.Kind == SlotKind.Gap && s.Start == baseTime);
        }

        //-----------------------------
        //internal entries are marked not billable
        [Fact]
        public async Task GetDay_MarksInternalNotBillable()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            var now = new DateTime(2026, 9, 28, 17, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var dayService = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await dayService.StartDayAsync(user.UserId, baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var activeProjects = await entryService.GetActiveProjectsAsync();
            var internalProj = activeProjects.First();

            await entryService.LogEntryAsync(new LogEntryRequest(user.UserId, internalProj.ProjectID, 2, "work", EntryMethod.Manual, baseTime.AddHours(1)));

            var editService = scope.ServiceProvider.GetRequiredService<TimesheetEditService>();
            var slots = await editService.GetDayAsync(user.UserId, DateOnly.FromDateTime(baseTime), now);

            var entrySlot = slots.First(s => s.Kind == SlotKind.Entry);
            Assert.False(entrySlot.Billable);
        }

        //-----------------------------
        //adding entry fills gap and creates new project if missing
        [Fact]
        public async Task Add_FillsAGap()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            var now = new DateTime(2026, 9, 28, 17, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var editService = scope.ServiceProvider.GetRequiredService<TimesheetEditService>();
            var edit = new EntryEdit(baseTime.AddHours(1), baseTime.AddHours(1.5), ["Acme", "Portal"], ["Coding"], "gap filled");

            var result = await editService.AddEntryAsync(user.UserId, edit, now);
            Assert.True(result.Ok);
            Assert.NotNull(result.EntryId);

            var entryRepo = scope.ServiceProvider.GetRequiredService<ITimeEntryRepository>();
            var saved = await entryRepo.GetByIdAsync(result.EntryId.Value);

            Assert.NotNull(saved);
            Assert.Equal(EntryMethod.Manual, saved.Method);
        }

        //-----------------------------
        //overlapping entry addition is rejected
        [Fact]
        public async Task Add_RejectsOverlap()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            var now = new DateTime(2026, 9, 28, 17, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var dayService = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await dayService.StartDayAsync(user.UserId, baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var activeProjects = await entryService.GetActiveProjectsAsync();
            var internalProj = activeProjects.First();

            await entryService.LogEntryAsync(new LogEntryRequest(user.UserId, internalProj.ProjectID, 2, "work", EntryMethod.Manual, baseTime.AddHours(1)));

            var editService = scope.ServiceProvider.GetRequiredService<TimesheetEditService>();
            var edit = new EntryEdit(baseTime.AddMinutes(30), baseTime.AddHours(1.5), ["Acme", "Portal"], ["Coding"], null);

            var result = await editService.AddEntryAsync(user.UserId, edit, now);
            Assert.False(result.Ok);
            Assert.Equal("This overlaps another entry.", result.Error);
        }

        //-----------------------------
        //future and backwards times are rejected
        [Fact]
        public async Task Add_RejectsFutureAndBackwardsTimes()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            var now = new DateTime(2026, 9, 28, 17, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var editService = scope.ServiceProvider.GetRequiredService<TimesheetEditService>();

            var futureEdit = new EntryEdit(now.AddHours(1), now.AddHours(2), ["Acme", "Portal"], ["Coding"], null);
            var futureResult = await editService.AddEntryAsync(user.UserId, futureEdit, now);
            Assert.False(futureResult.Ok);
            Assert.Equal("An entry can't end in the future.", futureResult.Error);

            var backwardsEdit = new EntryEdit(baseTime.AddHours(2), baseTime.AddHours(1), ["Acme", "Portal"], ["Coding"], null);
            var backwardsResult = await editService.AddEntryAsync(user.UserId, backwardsEdit, now);
            Assert.False(backwardsResult.Ok);
            Assert.Equal("End must be after start.", backwardsResult.Error);
        }

        //-----------------------------
        //unknown top level activity throws exception
        [Fact]
        public async Task Add_RejectsNewTopLevelActivity()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            var now = new DateTime(2026, 9, 28, 17, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var editService = scope.ServiceProvider.GetRequiredService<TimesheetEditService>();
            var edit = new EntryEdit(baseTime, baseTime.AddHours(1), ["Acme", "Portal"], ["Gardening"], null);

            await Assert.ThrowsAsync<ArgumentException>(() => editService.AddEntryAsync(user.UserId, edit, now));
        }

        //-----------------------------
        //updating entry changes times and returns previous edit values
        [Fact]
        public async Task Update_ChangesTimesAndReturnsPrevious()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            var now = new DateTime(2026, 9, 28, 17, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var dayService = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await dayService.StartDayAsync(user.UserId, baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var activeProjects = await entryService.GetActiveProjectsAsync();
            var internalProj = activeProjects.First();

            var entries = await entryService.LogEntryAsync(new LogEntryRequest(user.UserId, internalProj.ProjectID, 2, "work", EntryMethod.Manual, baseTime.AddHours(1)));
            var entryId = entries.First().TimeEntryId;

            var editService = scope.ServiceProvider.GetRequiredService<TimesheetEditService>();
            var updateEdit = new EntryEdit(baseTime, baseTime.AddMinutes(45), ["Leading Edge (Internal)", "TimePlanner"], ["Coding"], "updated");

            var result = await editService.UpdateEntryAsync(user.UserId, entryId, updateEdit, now);
            Assert.True(result.Ok);
            Assert.NotNull(result.Previous);
            Assert.Equal(baseTime.AddHours(1), result.Previous.End);

            var entryRepo = scope.ServiceProvider.GetRequiredService<ITimeEntryRepository>();
            var updatedEntry = await entryRepo.GetByIdAsync(entryId);
            Assert.NotNull(updatedEntry);
            Assert.Equal(baseTime.AddMinutes(45), updatedEntry.EndTime);
        }

        //-----------------------------
        //updating entry allows keeping same times without overlap error
        [Fact]
        public async Task Update_AllowsKeepingItsOwnTime()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            var now = new DateTime(2026, 9, 28, 17, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var dayService = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await dayService.StartDayAsync(user.UserId, baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var activeProjects = await entryService.GetActiveProjectsAsync();
            var internalProj = activeProjects.First();

            var entries = await entryService.LogEntryAsync(new LogEntryRequest(user.UserId, internalProj.ProjectID, 2, "work", EntryMethod.Manual, baseTime.AddHours(1)));
            var entryId = entries.First().TimeEntryId;

            var editService = scope.ServiceProvider.GetRequiredService<TimesheetEditService>();
            var sameEdit = new EntryEdit(baseTime, baseTime.AddHours(1), ["Leading Edge (Internal)", "TimePlanner"], ["Coding"], "same times");

            var result = await editService.UpdateEntryAsync(user.UserId, entryId, sameEdit, now);
            Assert.True(result.Ok);
        }

        //-----------------------------
        //updating entry owned by another user is rejected
        [Fact]
        public async Task Update_RejectsOtherUsersEntry()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            var now = new DateTime(2026, 9, 28, 17, 0, 0);
            using var scope = _host.CreateScope();
            var user1 = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester1", baseTime);

            var dayService = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await dayService.StartDayAsync(user1.UserId, baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var activeProjects = await entryService.GetActiveProjectsAsync();
            var internalProj = activeProjects.First();

            var entries = await entryService.LogEntryAsync(new LogEntryRequest(user1.UserId, internalProj.ProjectID, 2, "work", EntryMethod.Manual, baseTime.AddHours(1)));
            var entryId = entries.First().TimeEntryId;

            var editService = scope.ServiceProvider.GetRequiredService<TimesheetEditService>();
            var edit = new EntryEdit(baseTime, baseTime.AddHours(1), ["Leading Edge (Internal)", "TimePlanner"], ["Coding"], "hack");

            var result = await editService.UpdateEntryAsync(user1.UserId + 999, entryId, edit, now);
            Assert.False(result.Ok);
            Assert.Equal("Entry not found.", result.Error);
        }

        //-----------------------------
        //deleting an entry and restoring via add brings back entry
        [Fact]
        public async Task Delete_ThenUndoByAdd()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            var now = new DateTime(2026, 9, 28, 17, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var dayService = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await dayService.StartDayAsync(user.UserId, baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var activeProjects = await entryService.GetActiveProjectsAsync();
            var internalProj = activeProjects.First();

            var entries = await entryService.LogEntryAsync(new LogEntryRequest(user.UserId, internalProj.ProjectID, 2, "work", EntryMethod.Manual, baseTime.AddHours(1)));
            var entryId = entries.First().TimeEntryId;

            var editService = scope.ServiceProvider.GetRequiredService<TimesheetEditService>();
            var deleteResult = await editService.DeleteEntryAsync(user.UserId, entryId);

            Assert.True(deleteResult.Ok);
            Assert.NotNull(deleteResult.Previous);

            var entryRepo = scope.ServiceProvider.GetRequiredService<ITimeEntryRepository>();
            Assert.Null(await entryRepo.GetByIdAsync(entryId));

            var undoResult = await editService.AddEntryAsync(user.UserId, deleteResult.Previous, now);
            Assert.True(undoResult.Ok);

            var restoredEntry = await entryRepo.GetByIdAsync(undoResult.EntryId!.Value);
            Assert.NotNull(restoredEntry);
            Assert.Equal(deleteResult.Previous.Start, restoredEntry.StartTime);
            Assert.Equal(deleteResult.Previous.End, restoredEntry.EndTime);
        }

        //-----------------------------
        //find or add path creates missing nested levels
        [Fact]
        public async Task FindOrAddPath_AddsMissingLowerLevels()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var activityService = scope.ServiceProvider.GetRequiredService<ActivityService>();
            var path = new[] { "Coding", "Feature work", "Frontend" };

            var id1 = await activityService.FindOrAddPathAsync(path);
            var id2 = await activityService.FindOrAddPathAsync(path);

            Assert.Equal(id1, id2);

            var lookup = await activityService.GetActivityLookupAsync();
            Assert.Equal(path, lookup[id1].Path);
        }
    }
}
//------------------------------EOF-----------------------------\\
