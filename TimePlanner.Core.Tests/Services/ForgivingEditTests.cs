using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Repositories.Interfaces;
using TimePlanner.Core.Services;
using TimePlanner.Core.Services.Models;
using TimePlanner.Core.Tests.Support;
using Xunit;

namespace TimePlanner.Core.Tests.Services
{
    //-----------------------------
    //tests for forgiving time edits: times are compared to the minute, and the entry being edited wins while the entries around it adjust
    public class ForgivingEditTests : IDisposable
    {
        private static readonly string[] Project = ["Leading Edge (Internal)", "TimePlanner"];
        private static readonly DateTime Day = new(2026, 9, 28);
        private static readonly DateTime Now = At(17, 0);

        private readonly CoreTestHost _host = new();

        public void Dispose() => _host.Dispose();

        private static DateTime At(int hour, int minute, int second = 0) => Day.AddHours(hour).AddMinutes(minute).AddSeconds(second);

        private static EntryEdit Edit(DateTime start, DateTime end, string note) => new(start, end, Project, ["Coding"], note);

        private async Task<(IServiceScope Scope, TimesheetEditService Edits, ITimeEntryRepository Repo, int UserId)> StartAsync()
        {
            var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", At(8, 0));
            return (scope, scope.ServiceProvider.GetRequiredService<TimesheetEditService>(), scope.ServiceProvider.GetRequiredService<ITimeEntryRepository>(), user.UserId);
        }

        private static async Task<int> AddAsync(TimesheetEditService edits, int userId, DateTime start, DateTime end, string note)
        {
            var result = await edits.AddEntryAsync(userId, Edit(start, end, note), Now);
            Assert.True(result.Ok, result.Error);
            return result.EntryId!.Value;
        }

        private static async Task<(DateTime Start, DateTime End)> TimesAsync(ITimeEntryRepository repo, int id)
        {
            var entry = (await repo.GetByIdAsync(id))!;
            return (entry.StartTime, entry.EndTime);
        }

        //-----------------------------
        //an entry a check in ended at 10:00:37 no longer blocks one that starts at 10:00, which is what the editor shows
        [Fact]
        public async Task EntryEndingWithSeconds_DoesNotBlockTheNextOneStartingOnThatMinute()
        {
            var (scope, edits, repo, userId) = await StartAsync();
            using (scope)
            {
                var first = await AddAsync(edits, userId, At(9, 0), At(10, 0), "first");
                var stored = (await repo.GetByIdAsync(first))!;
                stored.EndTime = At(10, 0, 37);
                await repo.UpdateAsync(stored);

                var added = await edits.AddEntryAsync(userId, Edit(At(10, 0), At(11, 0), "second"), Now);

                Assert.True(added.Ok, added.Error);
            }
        }

        //-----------------------------
        //an edit's own seconds are dropped, so the editor and the stored times agree
        [Fact]
        public async Task EditedTimes_AreStoredToTheMinute()
        {
            var (scope, edits, repo, userId) = await StartAsync();
            using (scope)
            {
                var id = await AddAsync(edits, userId, At(9, 0), At(10, 0), "work");

                var result = await edits.UpdateEntryAsync(userId, id, Edit(At(9, 0, 15), At(10, 30, 59), "work"), Now);

                Assert.True(result.Ok, result.Error);
                Assert.Equal((At(9, 0), At(10, 30)), await TimesAsync(repo, id));
            }
        }

        //-----------------------------
        //logged times drop their seconds, so one entry ends exactly where the next begins
        [Fact]
        public async Task LoggedEntries_AreJoinedAtTheMinute()
        {
            using var scope = _host.CreateScope();
            var sp = scope.ServiceProvider;
            var user = await sp.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", At(8, 0, 12));
            await sp.GetRequiredService<DaySessionService>().StartDayAsync(user.UserId, At(8, 0, 12));
            var entries = sp.GetRequiredService<EntryService>();
            var project = (await entries.GetActiveProjectsAsync()).First();

            var first = (await entries.LogEntryAsync(new LogEntryRequest(user.UserId, project.ProjectID, 2, "one", EntryMethod.AutoPrompted, At(9, 0, 37)))).Single();
            var second = (await entries.LogEntryAsync(new LogEntryRequest(user.UserId, project.ProjectID, 2, "two", EntryMethod.AutoPrompted, At(10, 0, 50)))).Single();

            Assert.Equal((At(8, 0), At(9, 0)), (first.StartTime, first.EndTime));
            Assert.Equal((At(9, 0), At(10, 0)), (second.StartTime, second.EndTime));
        }

        //-----------------------------
        //shortening Task 1 asks first, names the neighbour's new times, and saves both together once confirmed
        [Fact]
        public async Task Shortening_MovesTheSharedEdge_OnlyAfterConfirmation()
        {
            var (scope, edits, repo, userId) = await StartAsync();
            using (scope)
            {
                var task1 = await AddAsync(edits, userId, At(13, 30), At(14, 30), "task 1");
                var task2 = await AddAsync(edits, userId, At(14, 30), At(15, 0), "task 2");
                var shorter = Edit(At(13, 30), At(14, 15), "task 1");

                var preview = await edits.PreviewUpdateAsync(userId, task1, shorter, Now);

                Assert.True(preview.NeedsConfirmation);
                var change = Assert.Single(preview.Changes);
                Assert.Equal(task2, change.EntryId);
                Assert.Equal((At(14, 15), At(15, 0)), (change.NewStart!.Value, change.NewEnd!.Value));
                Assert.Contains("TimePlanner", change.Label);

                //nothing is stored by the preview, or by an edit that was not confirmed
                Assert.Equal((At(14, 30), At(15, 0)), await TimesAsync(repo, task2));
                var refused = await edits.UpdateEntryAsync(userId, task1, shorter, Now);
                Assert.False(refused.Ok);
                Assert.Equal("This overlaps another entry.", refused.Error);
                Assert.Equal((At(13, 30), At(14, 30)), await TimesAsync(repo, task1));

                var saved = await edits.UpdateEntryAsync(userId, task1, shorter, Now, confirmed: true);

                Assert.True(saved.Ok, saved.Error);
                Assert.Equal((At(13, 30), At(14, 15)), await TimesAsync(repo, task1));
                Assert.Equal((At(14, 15), At(15, 0)), await TimesAsync(repo, task2));
            }
        }

        //-----------------------------
        //lengthening Task 1 shortens Task 2 the same way
        [Fact]
        public async Task Lengthening_ShortensTheNextEntry()
        {
            var (scope, edits, repo, userId) = await StartAsync();
            using (scope)
            {
                var task1 = await AddAsync(edits, userId, At(13, 30), At(14, 30), "task 1");
                var task2 = await AddAsync(edits, userId, At(14, 30), At(15, 0), "task 2");

                var saved = await edits.UpdateEntryAsync(userId, task1, Edit(At(13, 30), At(14, 45), "task 1"), Now, confirmed: true);

                Assert.True(saved.Ok, saved.Error);
                Assert.Equal((At(14, 45), At(15, 0)), await TimesAsync(repo, task2));
            }
        }

        //-----------------------------
        //moving the start moves the edge it shares with the entry before
        [Fact]
        public async Task MovingTheStart_MovesTheEntryBefore()
        {
            var (scope, edits, repo, userId) = await StartAsync();
            using (scope)
            {
                var before = await AddAsync(edits, userId, At(12, 30), At(13, 30), "before");
                var task = await AddAsync(edits, userId, At(13, 30), At(14, 30), "task");

                var saved = await edits.UpdateEntryAsync(userId, task, Edit(At(13, 45), At(14, 30), "task"), Now, confirmed: true);

                Assert.True(saved.Ok, saved.Error);
                Assert.Equal((At(12, 30), At(13, 45)), await TimesAsync(repo, before));
            }
        }

        //-----------------------------
        //an edit that runs into an entry that was not touching it shortens that entry, the case where fixing a time used to be refused
        [Fact]
        public async Task EditOverlappingAnEntryThatWasNotTouching_ShortensIt()
        {
            var (scope, edits, repo, userId) = await StartAsync();
            using (scope)
            {
                var edited = await AddAsync(edits, userId, At(9, 0), At(10, 0), "edited");
                var wrong = await AddAsync(edits, userId, At(10, 30), At(11, 30), "wrong");

                var preview = await edits.PreviewUpdateAsync(userId, edited, Edit(At(9, 0), At(11, 0), "edited"), Now);
                Assert.Equal((At(11, 0), At(11, 30)), (preview.Changes.Single().NewStart!.Value, preview.Changes.Single().NewEnd!.Value));

                var saved = await edits.UpdateEntryAsync(userId, edited, Edit(At(9, 0), At(11, 0), "edited"), Now, confirmed: true);

                Assert.True(saved.Ok, saved.Error);
                Assert.Equal((At(11, 0), At(11, 30)), await TimesAsync(repo, wrong));
            }
        }

        //-----------------------------
        //entries that were never touching keep their gap
        [Fact]
        public async Task EntriesThatDoNotTouch_KeepTheirGap()
        {
            var (scope, edits, repo, userId) = await StartAsync();
            using (scope)
            {
                var first = await AddAsync(edits, userId, At(13, 30), At(14, 30), "first");
                var second = await AddAsync(edits, userId, At(15, 0), At(16, 0), "second");

                var shorter = Edit(At(13, 30), At(14, 15), "first");
                var preview = await edits.PreviewUpdateAsync(userId, first, shorter, Now);
                var saved = await edits.UpdateEntryAsync(userId, first, shorter, Now);

                Assert.False(preview.NeedsConfirmation);
                Assert.True(saved.Ok, saved.Error);
                Assert.Equal((At(15, 0), At(16, 0)), await TimesAsync(repo, second));
            }
        }

        //-----------------------------
        //an edit that would cover a whole entry asks before removing it, and the entry only goes once confirmed
        [Fact]
        public async Task CoveringAWholeEntry_AsksBeforeRemovingIt()
        {
            var (scope, edits, repo, userId) = await StartAsync();
            using (scope)
            {
                var task1 = await AddAsync(edits, userId, At(13, 30), At(14, 30), "task 1");
                var task2 = await AddAsync(edits, userId, At(14, 30), At(15, 0), "task 2");
                var longer = Edit(At(13, 30), At(15, 30), "task 1");

                var preview = await edits.PreviewUpdateAsync(userId, task1, longer, Now);

                var removal = Assert.Single(preview.Changes);
                Assert.True(removal.IsRemoval);
                Assert.False((await edits.UpdateEntryAsync(userId, task1, longer, Now)).Ok);
                Assert.NotNull(await repo.GetByIdAsync(task2));

                var saved = await edits.UpdateEntryAsync(userId, task1, longer, Now, confirmed: true);

                Assert.True(saved.Ok, saved.Error);
                Assert.Null(await repo.GetByIdAsync(task2));
                Assert.Equal((At(13, 30), At(15, 30)), await TimesAsync(repo, task1));
                Assert.Equal("task 2", Assert.Single(saved.RemovedNeighbours!).Note);
            }
        }

        //-----------------------------
        //an edit that would have to cut another entry in two is refused with a reason
        [Fact]
        public async Task EditInsideAnotherEntry_IsRefused()
        {
            var (scope, edits, _, userId) = await StartAsync();
            using (scope)
            {
                await AddAsync(edits, userId, At(9, 0), At(12, 0), "long");
                var other = await AddAsync(edits, userId, At(13, 0), At(14, 0), "other");

                var preview = await edits.PreviewUpdateAsync(userId, other, Edit(At(10, 0), At(11, 0), "other"), Now);

                Assert.False(preview.Ok);
                Assert.StartsWith("This sits inside another entry", preview.Error);
            }
        }

        //-----------------------------
        //adding an entry still has to fit between the others, but back to back is fine
        [Fact]
        public async Task AddingAnEntry_StillCannotOverlap()
        {
            var (scope, edits, _, userId) = await StartAsync();
            using (scope)
            {
                await AddAsync(edits, userId, At(9, 0), At(10, 0), "first");

                var overlapping = await edits.AddEntryAsync(userId, Edit(At(9, 30), At(10, 30), "second"), Now);
                var backToBack = await edits.AddEntryAsync(userId, Edit(At(10, 0), At(11, 0), "third"), Now);

                Assert.False(overlapping.Ok);
                Assert.Equal("This overlaps another entry.", overlapping.Error);
                Assert.True(backToBack.Ok, backToBack.Error);
            }
        }

        //-----------------------------
        //undo puts the edited entry and the neighbour it moved back together, even the exact times they had
        [Fact]
        public async Task Restore_PutsBothEntriesBack()
        {
            var (scope, edits, repo, userId) = await StartAsync();
            using (scope)
            {
                var task1 = await AddAsync(edits, userId, At(13, 30), At(14, 30), "task 1");
                var task2 = await AddAsync(edits, userId, At(14, 30), At(15, 0), "task 2");
                var saved = await edits.UpdateEntryAsync(userId, task1, Edit(At(13, 30), At(14, 15), "task 1"), Now, confirmed: true);

                var undone = await edits.RestoreAsync(userId, task1, saved.Previous!, saved.ChangedNeighbours!, saved.RemovedNeighbours!);

                Assert.True(undone.Ok, undone.Error);
                Assert.Equal((At(13, 30), At(14, 30)), await TimesAsync(repo, task1));
                Assert.Equal((At(14, 30), At(15, 0)), await TimesAsync(repo, task2));
            }
        }

        //-----------------------------
        //undo brings back an entry the edit removed
        [Fact]
        public async Task Restore_BringsBackARemovedEntry()
        {
            var (scope, edits, repo, userId) = await StartAsync();
            using (scope)
            {
                var task1 = await AddAsync(edits, userId, At(13, 30), At(14, 30), "task 1");
                await AddAsync(edits, userId, At(14, 30), At(15, 0), "task 2");
                var saved = await edits.UpdateEntryAsync(userId, task1, Edit(At(13, 30), At(15, 30), "task 1"), Now, confirmed: true);

                var undone = await edits.RestoreAsync(userId, task1, saved.Previous!, saved.ChangedNeighbours!, saved.RemovedNeighbours!);

                Assert.True(undone.Ok, undone.Error);
                var day = await repo.GetForUserAsync(userId, Day, Day.AddDays(1));
                Assert.Equal(new[] { (At(13, 30), At(14, 30)), (At(14, 30), At(15, 0)) }, day.Select(e => (e.StartTime, e.EndTime)).ToArray());
                Assert.Equal(new[] { "task 1", "task 2" }, day.Select(e => e.Note).ToArray());
            }
        }

        //-----------------------------
        //undo refuses when something was added where the old times were, rather than overlapping it
        [Fact]
        public async Task Restore_RefusesWhenTheDayChangedSince()
        {
            var (scope, edits, repo, userId) = await StartAsync();
            using (scope)
            {
                var task = await AddAsync(edits, userId, At(13, 30), At(14, 30), "task");
                var saved = await edits.UpdateEntryAsync(userId, task, Edit(At(13, 30), At(14, 0), "task"), Now);
                await AddAsync(edits, userId, At(14, 0), At(14, 30), "added since");

                var undone = await edits.RestoreAsync(userId, task, saved.Previous!, saved.ChangedNeighbours!, saved.RemovedNeighbours!);

                Assert.False(undone.Ok);
                Assert.Equal("The timesheet has changed since, so this can't be undone.", undone.Error);
                Assert.Equal((At(13, 30), At(14, 0)), await TimesAsync(repo, task));
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
