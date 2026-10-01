using System;
using System.Collections.Generic;
using System.Globalization;
using System.Media;
using System.Text;
using System.Windows;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Widget.Models;
using TimePlanner.Widget.Services;
using TimePlanner.Widget.Views;

namespace TimePlanner.Widget
{
    public sealed class WidgetFlow
    {
        private enum Screen
        {
            None,
            Setup,
            Idle,
            Timer,
            Settings,
            CheckIn,
            LogEntry,
            EntrySaved,
            EndDay,
            DayEnded,
            Timesheet,
        }

        private readonly IWidgetHost _host;
        private readonly TimeLogService _log;
        private readonly CsvExportService _csv;
        private readonly PreferencesStore _preferences;
        private Screen _screen;
        private DaySummary? _endedDay;

        public WidgetFlow(IWidgetHost host, WidgetSession session, CheckInScheduler scheduler, SnoozeManager snooze,
            TimeLogService log, CsvExportService csv, PreferencesStore preferences)
        {
            _host = host;
            Session = session;
            Scheduler = scheduler;
            Snooze = snooze;
            _log = log;
            _csv = csv;
            _preferences = preferences;

            scheduler.Ticked += (_, _) =>
            {
                if (_screen is Screen.Idle or Screen.Timer && Scheduler.IsDue)
                    CheckIn(announce: true);
            };
        }

        public WidgetSession Session { get; }

        public CheckInScheduler Scheduler { get; }

        public SnoozeManager Snooze { get; }

        public async Task LoadAsync()
        {
            Session.User = await _log.SignInAsync();
            Session.Preferences = _preferences.Load();
            (Session.ProjectId, Session.Activity) = await _log.GetLastChoiceAsync(Session.User.UserId);
        }

        public SetupView ShowSetup()
        {
            var view = new SetupView(this, isSettings: false);
            Present(Screen.Setup, view);
            return view;
        }

        public void StartTracking()
        {
            _endedDay = null;
            Scheduler.Start();
            ShowIdle();
        }

        public void ShowFromTray()
        {
            if (Scheduler.IsRunning)
                ShowTimer(activate: true);
            else if (_endedDay != null)
                ShowDayEnded(_endedDay);
            else
                ShowSetup();
        }

        public void ShowIdle()
        {
            if (Scheduler.IsDue)
            {
                CheckIn(announce: true);
                return;
            }

            if (Session.Preferences.IdleVisibility == IdleVisibility.Hidden)
            {
                _screen = Screen.Idle;
                _host.HideWidget();
            }
            else
            {
                Present(Screen.Idle, new IdleView(this), activate: false);
            }
        }

        public void ShowTimer(bool activate = false)
        {
            if (Scheduler.IsDue)
                CheckIn();
            else
                Present(Screen.Timer, new TimerView(this), activate);
        }

        public void ShowSettings() => Present(Screen.Settings, new SettingsView(this));

        public void CheckIn(bool announce = false)
        {
            Present(Screen.CheckIn, new CheckInAlertView(this), activate: false);
            if (announce && Session.Preferences.SoundOnCheckIn)
                SystemSounds.Asterisk.Play();
        }

        public void SnoozeCheckIn()
        {
            Snooze.Snooze();
            ShowIdle();
        }


        public LogPeriod GetLogPeriod(LogEntryMode mode)
        {
            var start = Scheduler.PeriodStart;
            var end = mode == LogEntryMode.CheckIn && Scheduler.CheckInAt is { } due && due <= Scheduler.Now ? due : Scheduler.Now;
            return new LogPeriod(start, end, Scheduler.WorkedBetween(start, end));
        }

        public async Task<LogEntryView> LogTimeAsync(LogEntryMode mode)
        {
            var period = GetLogPeriod(mode);
            var projects = await _log.GetProjectsAsync();
            var project = projects.FirstOrDefault(p => p.ProjectID == Session.ProjectId) ?? projects.FirstOrDefault();
            var activities = project != null ? await GetActivitiesAsync(project.ProjectID) : ActivityChoices.Empty();

            var view = new LogEntryView(this, mode, period, projects, project, activities);
            Present(Screen.LogEntry, view);
            return view;
        }

        public Task<ActivityChoices> GetActivitiesAsync(int projectId) => _log.GetActivitiesAsync(Session.User.UserId, projectId);

        public void CancelLogEntry(LogEntryMode mode)
        {
            if (mode == LogEntryMode.EndOfDay)
                AskEndDay();
            else
                ShowTimer(activate: true);
        }

        public async Task SaveEntryAsync(LogEntryMode mode, LogPeriod period, int projectId, IReadOnlyList<string> activity, string? note)
        {
            var end = mode == LogEntryMode.CheckIn ? period.End : Scheduler.Now;
            var method = mode == LogEntryMode.CheckIn ? EntryMethod.AutoPrompted : EntryMethod.Manual;
            await _log.SaveAsync(Session.User.UserId, projectId, activity, period.Start, end, note, method,
                Scheduler.BreaksBetween(period.Start, end));

            Scheduler.Logged(end);
            Session.ProjectId = projectId;
            Session.Activity = activity;

            if (mode == LogEntryMode.EndOfDay)
                await EndDayAsync();   
            else
                Present(Screen.EntrySaved, new EntrySavedView(this));
        }

        public void KeepTracking() => ShowIdle();


        public void AskEndDay() => Present(Screen.EndDay, new EndDayView(this));

        public async Task<DaySummary> EndDayAsync()
        {
            Scheduler.EndDay();
            var day = await _log.GetDayAsync(Session.User.UserId, Scheduler.Now);
            ShowDayEnded(day);
            return day;
        }

        public void ShowDayEnded(DaySummary day)
        {
            _endedDay = day;
            Present(Screen.DayEnded, new DayEndedView(this, day));
        }

        public void ResumeDay()
        {
            _endedDay = null;
            Scheduler.ResumeDay();
            ShowIdle();
        }

        public void DismissDayEnded()
        {
            _screen = Screen.None;
            _host.HideWidget();
        }

        public void ShowTimesheet(DaySummary day) => Present(Screen.Timesheet, new TimesheetReviewView(this, day));

        public async Task ExportCsvAsync(DaySummary day)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export CSV",
                Filter = "CSV files (*.csv)|*.csv",
                FileName = $"timesheet-{day.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };
            if (dialog.ShowDialog(Application.Current.MainWindow) == true)
                await _csv.WriteAsync(dialog.FileName, day.Entries, Session.User);
        }

        public async Task ChangeIntervalAsync(int minutes)
        {
            Session.Settings.CheckInIntervalMinutes = minutes;
            Scheduler.IntervalChanged();
            await _log.SaveSettingsAsync(Session.Settings);
        }

        public void SavePreferences() => _preferences.Save(Session.Preferences);

        private void Present(Screen screen, FrameworkElement view, bool activate = true)
        {
            _screen = screen;
            _host.Present(view, activate);
        }
    }
}
