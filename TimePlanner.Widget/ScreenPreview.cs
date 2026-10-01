// Design-review and test tooling: compiled into debug builds only, never into a release build.
#if DEBUG
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget
{
    /// <summary>A screen in a given state: one of the figures in the spec's Screens section.</summary>
    public sealed record ScreenPreview(string Id, string Section, string Title, Func<PreviewSession, Task> Show, bool WithEntries = true);

    public static class ScreenPreviews
    {
        public static IReadOnlyList<ScreenPreview> All { get; } =
        [
            new("setup", "Setup", "Setup", Do(s => s.At(8, 30).Flow.ShowSetup())),
            new("settings", "Setup", "Settings, reached from the timer", Do(s =>
            {
                s.StartDayAt(8, 30).Session.Settings.CheckInIntervalMinutes = 45;
                s.At(9, 0).Flow.ShowSettings();
            })),
            new("advanced-hidden", "Setup", "Advanced options, hidden when idle", Do(s =>
            {
                var preferences = s.Flow.Session.Preferences;
                preferences.IdleVisibility = IdleVisibility.Hidden;
                preferences.IdleShape = IdleShape.Ring;
                preferences.SoundOnCheckIn = false;
                s.At(8, 30).Flow.ShowSetup().ShowAdvanced();
            })),

            new("idle-pill", "Idle and timer", "Resting: pill", Do(s => Idle(s, IdleShape.Pill))),
            new("idle-ring", "Idle and timer", "Resting: ring", Do(s => Idle(s, IdleShape.Ring))),
            new("idle-dot", "Idle and timer", "Resting: dot", Do(s => Idle(s, IdleShape.Dot))),
            new("timer", "Idle and timer", "Timer, running", Do(s => Afternoon(s, 15, 23).ShowTimer(activate: true))),
            new("timer-paused", "Idle and timer", "Timer, paused", Do(s =>
            {
                s.StartDayAt(9, 57);
                s.At(10, 15).Flow.Scheduler.Pause();
                s.At(10, 20).Flow.ShowTimer(activate: true);
            })),

            new("log-entry", "Log entry", "Activity chosen", s => Afternoon(s, 16, 35).LogTimeAsync(LogEntryMode.CheckIn)),
            new("log-now", "Log entry", "Opened early with Log now", s =>
            {
                Afternoon(s, 16, 35).Scheduler.Logged(s.Clock.Now);   
                return s.At(16, 57).Flow.LogTimeAsync(LogEntryMode.LogNow);
            }),
            new("menu-open", "Log entry", "Menu open", s => OpenMenu(s)),
            new("menu-coding", "Log entry", "Hovering Coding opens the next level", s => OpenMenu(s, expand: ["Coding"])),
            new("menu-adding", "Log entry", "Adding an item to Coding", s => OpenMenu(s, addingAt: ["Coding"])),
            new("no-activity", "Log entry", "Saved without an activity", async s =>
            {
                var flow = Afternoon(s, 16, 35);
                flow.Session.Activity = [];
                while (flow.Snooze.Snooze())
                {
                    // spend every snooze, so the check-in has to be answered
                }
                var view = await flow.LogTimeAsync(LogEntryMode.CheckIn);
                view.Activity.Validate();
            }),

            new("entry-saved", "After saving", "Entry saved", s =>
            {
                var flow = Afternoon(s, 16, 35);
                return flow.SaveEntryAsync(LogEntryMode.CheckIn, flow.GetLogPeriod(LogEntryMode.CheckIn),
                    flow.Session.ProjectId ?? 0, flow.Session.Activity, note: null);
            }),

            new("end-day", "Ending the day", "Time still unlogged", Do(s => Afternoon(s, 16, 17).AskEndDay())),
            new("end-day-logged", "Ending the day", "Nothing left to log", Do(s =>
            {
                Afternoon(s, 16, 35).Scheduler.Logged(s.Clock.Now);
                s.Flow.AskEndDay();
            })),

            new("day-ended", "Day ended", "Day ended", s => Afternoon(s, 16, 35).EndDayAsync()),
            new("day-ended-empty", "Day ended", "Day ended with nothing logged", s => Afternoon(s, 16, 35).EndDayAsync(),
                WithEntries: false),
            new("timesheet", "Day ended", "Timesheet", async s =>
            {
                var flow = Afternoon(s, 16, 35);
                flow.ShowTimesheet(await flow.EndDayAsync());
            }),

            new("check-in", "Check-in alert", "Check-in alert", Do(s => Afternoon(s, 16, 35).CheckIn())),
        ];

        public static ScreenPreview? Find(string id) =>
            All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

        private static Func<PreviewSession, Task> Do(Action<PreviewSession> show) => s =>
        {
            show(s);
            return Task.CompletedTask;
        };

        private static WidgetFlow Afternoon(PreviewSession s, int hour, int minute)
        {
            s.StartDayAt(15, 5);
            return s.At(hour, minute).Flow;
        }

        private static void Idle(PreviewSession s, IdleShape shape)
        {
            var flow = Afternoon(s, 15, 23);
            flow.Session.Preferences.IdleShape = shape;
            flow.ShowIdle();
        }

        private static async Task OpenMenu(PreviewSession s, IReadOnlyList<string>? expand = null, IReadOnlyList<string>? addingAt = null)
        {
            var flow = Afternoon(s, 16, 35);
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