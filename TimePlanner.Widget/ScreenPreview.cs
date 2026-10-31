// Design-review and test tooling: compiled into debug builds only, never into a release build.
#if DEBUG
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using TimePlanner.Widget.Models;
using TimePlanner.Core.Services;

namespace TimePlanner.Widget
{
    public sealed record ScreenPreview(string Id, string Section, string Title, Func<PreviewSession, Task> Show, bool WithEntries = true);

    public static class ScreenPreviews
    {
        public static IReadOnlyList<ScreenPreview> All { get; } =
        [
            new("setup", "Setup", "Setup", Do(s => s.At(8, 30).Flow.ShowSetup())),
            new("help", "Setup", "Help", Do(s => { s.Flow.ShowSetup(); s.Flow.ShowHelp(); })),
            new("settings", "Setup", "Settings, reached from the timer", async s =>
            {
                (await s.StartDayAtAsync(8, 30)).Session.Settings.CheckInIntervalMinutes = 45;
                s.At(9, 0).Flow.ShowSettings();
            }),
            new("manage-lists", "Setup", "Manage lists: projects, one hidden", async s =>
            {
                await HideAtlasAsync(s);
                (await s.Flow.ShowManageListsAsync()).Open(showHidden: true);
            }),
            new("manage-lists-add", "Setup", "Manage lists: adding a project", async s =>
                (await s.Flow.ShowManageListsAsync()).Open(addingUnder: "Contoso")),
            new("manage-lists-activities", "Setup", "Manage lists: activities", async s =>
                (await s.Flow.ShowManageListsAsync()).Open(activities: true)),
            new("advanced-hidden", "Setup", "Advanced options, hidden when idle", Do(s =>
            {
                var preferences = s.Flow.Session.Preferences;
                preferences.IdleVisibility = IdleVisibility.Hidden;
                preferences.IdleShape = IdleShape.Ring;
                preferences.SoundOnCheckIn = false;
                s.At(8, 30).Flow.ShowSetup().ShowAdvanced();
            })),

            new("idle-pill", "Idle and timer", "Resting: pill", s => Idle(s, IdleShape.Pill)),
            new("idle-ring", "Idle and timer", "Resting: ring", s => Idle(s, IdleShape.Ring)),
            new("idle-dot", "Idle and timer", "Resting: dot", s => Idle(s, IdleShape.Dot)),
            new("timer", "Idle and timer", "Timer, running", async s => (await Afternoon(s, 15, 23)).ShowTimer(activate: true)),
            new("tray-menu", "Idle and timer", "Tray menu", async s => (await Afternoon(s, 15, 23)).ShowTimer(activate: true)),
            // Without the sample entries, which run to 3:05 PM and so would come after a morning start
            new("timer-paused", "Idle and timer", "Timer, paused", async s =>
            {
                await s.StartDayAtAsync(9, 57);
                await s.At(10, 15).Flow.Scheduler.PauseAsync();
                await s.At(10, 20).Flow.Scheduler.RefreshAsync();
                s.Flow.ShowTimer(activate: true);
            }, WithEntries: false),

            new("timer-large", "Large view", "Timer, running", Large(async s => (await Afternoon(s, 15, 23)).ShowTimer(activate: true))),
            new("timer-paused-large", "Large view", "Timer, paused", Large(async s =>
            {
                await s.StartDayAtAsync(9, 57);
                await s.At(10, 15).Flow.Scheduler.PauseAsync();
                await s.At(10, 20).Flow.Scheduler.RefreshAsync();
                s.Flow.ShowTimer(activate: true);
            }), WithEntries: false),

            new("log-entry", "Log entry", "Activity chosen", async s => await (await Afternoon(s, 16, 35)).LogTimeAsync(LogEntryMode.CheckIn)),
            new("log-now", "Log entry", "Opened early with Log now", async s =>
            {
                await Answer(await Afternoon(s, 16, 35));
                await s.At(16, 57).Flow.LogTimeAsync(LogEntryMode.LogNow);
            }),
            new("menu-open", "Log entry", "Menu open", s => OpenMenu(s)),
            new("menu-coding", "Log entry", "Coding opened in place", s => OpenMenu(s, expand: ["Coding"])),
            new("menu-adding", "Log entry", "Adding an item to Coding", s => OpenMenu(s, addingAt: ["Coding"])),
            new("no-activity", "Log entry", "Saved without an activity", async s =>
            {
                var flow = await Afternoon(s, 16, 35);
                flow.Session.Activity = [];
                // Spend every snooze, so the check-in has to be answered
                while (await flow.Snooze.SnoozeAsync())
                    s.Clock.Now = flow.Snooze.SnoozedUntil!.Value;
                var view = await flow.LogTimeAsync(LogEntryMode.CheckIn);
                view.Activity.Validate();
            }),

            new("entry-saved", "After saving", "Entry saved", async s => await Answer(await Afternoon(s, 16, 35))),

            new("end-day", "Ending the day", "Time still unlogged", async s => (await Afternoon(s, 16, 17)).AskEndDay()),
            new("end-day-logged", "Ending the day", "Nothing left to log", async s =>
            {
                var flow = await Afternoon(s, 16, 35);
                await Answer(flow);
                flow.AskEndDay();
            }),

            new("day-ended", "Day ended", "Day ended", async s => await (await Afternoon(s, 16, 35)).EndDayAsync()),
            new("day-ended-empty", "Day ended", "Day ended with nothing logged", async s => await (await Afternoon(s, 16, 35)).EndDayAsync(),
                WithEntries: false),
            new("timesheet", "Day ended", "Timesheet", async s =>
            {
                var flow = await Afternoon(s, 16, 35);
                flow.ShowTimesheet(await flow.EndDayAsync());
            }),

            new("timesheet-wide", "Day ended", "Timesheet, wide window", async s =>
            {
                var flow = await Afternoon(s, 16, 35);
                await flow.EndDayAsync();
                await flow.ShowWideTimesheetAsync(DateOnly.FromDateTime(PreviewSession.Day), flow.ShowFromTray);
            }),
            new("timesheet-large", "Large view", "Timesheet", Large(async s =>
            {
                var flow = await Afternoon(s, 16, 35);
                flow.ShowTimesheet(await flow.EndDayAsync());
            })),
            new("timesheet-wide-large", "Large view", "Timesheet, wide window", Large(async s =>
            {
                var flow = await Afternoon(s, 16, 35);
                await flow.EndDayAsync();
                await flow.ShowWideTimesheetAsync(DateOnly.FromDateTime(PreviewSession.Day), flow.ShowFromTray);
            })),

            new("check-in", "Check-in alert", "Check-in alert", async s => (await Afternoon(s, 16, 35)).CheckIn()),

            new("ai-settings", "Voice and AI", "Voice and AI settings", async s =>
                (await Afternoon(s, 16, 35)).ShowAiSettings(() => s.Flow.ShowSettings())),
            new("ai-advanced", "Voice and AI", "Voice and AI, advanced options", async s =>
                (await Afternoon(s, 16, 35)).ShowAiSettings(() => s.Flow.ShowSettings(), openAdvanced: true)),
            new("voice-note", "Voice and AI", "Recording a voice note", async s =>
            {
                var flow = await Afternoon(s, 16, 35);
                flow.CheckIn();
                flow.StartVoiceNote();
            }),
            new("day-build", "Voice and AI", "Build my day", async s =>
                await (await Afternoon(s, 16, 35)).ShowDayBuildAsync(DateOnly.FromDateTime(PreviewSession.Day), s.Flow.ShowFromTray)),
        ];

        public static ScreenPreview? Find(string id) =>
            All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

        private static Func<PreviewSession, Task> Do(Action<PreviewSession> show) => s =>
        {
            show(s);
            return Task.CompletedTask;
        };

        // The same figure with "Larger text and buttons" on
        private static Func<PreviewSession, Task> Large(Func<PreviewSession, Task> show) => s =>
        {
            s.Flow.Session.Preferences.LargerText = true;
            return show(s);
        };

        // The spec's afternoon: the day running since the 3:05 PM check-in, at the given time
        private static async Task<WidgetFlow> Afternoon(PreviewSession s, int hour, int minute)
        {
            await s.StartDayAtAsync(15, 5);
            await s.At(hour, minute).Flow.Scheduler.RefreshAsync();
            return s.Flow;
        }

        private static Task Answer(WidgetFlow flow) =>
            flow.SaveEntryAsync(LogEntryMode.CheckIn, flow.GetLogPeriod(),
                new[] { LocalSetupService.InternalCompanyName, LocalSetupService.InternalProjectName },
                flow.Session.Activity, note: null);

        private static async Task HideAtlasAsync(PreviewSession s)
        {
            var atlas = (await s.Flow.GetManagedListsAsync()).Clients.First(c => c.Name == "Atlas Group");
            await s.Flow.SetListItemHiddenAsync(ListKind.Client, atlas.Id, hidden: true);
        }

        private static async Task Idle(PreviewSession s, IdleShape shape)
        {
            var flow = await Afternoon(s, 15, 23);
            flow.Session.Preferences.IdleShape = shape;
            flow.ShowIdle();
        }

        private static async Task OpenMenu(PreviewSession s, IReadOnlyList<string>? expand = null, IReadOnlyList<string>? addingAt = null)
        {
            var flow = await Afternoon(s, 16, 35);
            flow.Session.Activity = [];
            var view = await flow.LogTimeAsync(LogEntryMode.CheckIn);
            _ = view.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                if (PresentationSource.FromVisual(view) != null)
                    view.Activity.Open(expand, addingAt);
            });
        }
    }
}
#endif
