using System;
using System.Collections.Generic;
using System.Globalization;
using System.Media;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Services;
using TimePlanner.Core.Services.Models;
using TimePlanner.Core.Sync;
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
            SignIn,
            EntryEditor,
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
            await Scheduler.InitialiseAsync();
        }

        /// <summary>The first screen: Setup, or the idle widget when today is still being tracked (the widget was restarted).</summary>
        public void ShowStart()
        {
            if (Scheduler.IsRunning)
                ShowIdle();
            else
                ShowSetup();
        }

        public SetupView ShowSetup()
        {
            var view = new SetupView(this, isSettings: false);
            Present(Screen.Setup, view);
            return view;
        }

        public async Task StartTrackingAsync()
        {
            _endedDay = null;
            await Scheduler.StartAsync();
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

        public async Task SnoozeCheckInAsync()
        {
            await Snooze.SnoozeAsync();
            ShowIdle();
        }

        /// <summary>The time still to log: from the end of the last entry (or the start of the day) until now, as Core logs it.</summary>
        public LogPeriod GetLogPeriod()
        {
            var start = Scheduler.PeriodStart;
            var end = Scheduler.Now;
            return new LogPeriod(start, end, Scheduler.WorkedBetween(start, end));
        }

        public async Task<LogEntryView> LogTimeAsync(LogEntryMode mode)
        {
            var period = GetLogPeriod();
            var projects = await _log.GetProjectsAsync();
            var project = projects.FirstOrDefault(p => p.ProjectID == Session.ProjectId) ?? projects.FirstOrDefault();
            var activities = project != null ? await GetActivitiesAsync(project.ProjectID) : await _log.GetActivityTreeAsync();

            var view = new LogEntryView(this, mode, period, projects, project, activities);
            Present(Screen.LogEntry, view);
            return view;
        }

        public Task<ActivityChoices> GetActivitiesAsync(int projectId) => _log.GetActivitiesAsync(Session.User.UserId, projectId);

        //-----------------------------
        //the activity tree with no recent list
        public Task<ActivityChoices> GetActivityTreeAsync() => _log.GetActivityTreeAsync();

        public void CancelLogEntry(LogEntryMode mode)
        {
            if (mode == LogEntryMode.EndOfDay)
                AskEndDay();
            else
                ShowTimer(activate: true);
        }

        public async Task SaveEntryAsync(LogEntryMode mode, LogPeriod period, IReadOnlyList<string> projectPath, IReadOnlyList<string> activity, string? note)
        {
            var end = Scheduler.Now;
            var method = mode == LogEntryMode.CheckIn ? EntryMethod.AutoPrompted : EntryMethod.Manual;
            var projectId = await _log.SaveAsync(Session.User.UserId, projectPath, activity, period.Start, end, note, method,
                Scheduler.BreaksBetween(period.Start, end));

            await Scheduler.LoggedAsync();
            Session.ProjectId = projectId;
            Session.Activity = activity;

            if (mode == LogEntryMode.EndOfDay)
                await EndDayAsync();   
            else
                Present(Screen.EntrySaved, new EntrySavedView(this));
        }

        //-----------------------------
        //removes a company by name
        public Task<RemoveOutcome> RemoveCompanyAsync(string company) => _log.RemoveCompanyAsync(company);

        //-----------------------------
        //removes a project by company and project name
        public Task<RemoveOutcome> RemoveProjectAsync(string company, string project) => _log.RemoveProjectAsync(company, project);

        //-----------------------------
        //removes an activity at a given path
        public Task<RemoveOutcome> RemoveActivityAsync(IReadOnlyList<string> path) => _log.RemoveActivityAsync(path);

        public void KeepTracking() => ShowIdle();


        public void AskEndDay() => Present(Screen.EndDay, new EndDayView(this));

        public async Task<DaySummary> EndDayAsync()
        {
            await Scheduler.EndDayAsync();
            var day = await _log.GetDayAsync(Session.User.UserId, Scheduler.Now);
            ShowDayEnded(day);
            return day;
        }

        public void ShowDayEnded(DaySummary day)
        {
            _endedDay = day;
            Present(Screen.DayEnded, new DayEndedView(this, day));
        }

        public async Task ResumeDayAsync()
        {
            _endedDay = null;
            await Scheduler.ResumeDayAsync();
            ShowIdle();
        }

        public void DismissDayEnded()
        {
            _screen = Screen.None;
            _host.HideWidget();
        }

        //-----------------------------
        //opens the timesheet for a day from day ended
        public void ShowTimesheet(DaySummary day) => _ = ShowTimesheetAsync(DateOnly.FromDateTime(day.Day), () => ShowDayEnded(day));

        //-----------------------------
        //loads and shows one day of the timesheet
        public async Task ShowTimesheetAsync(DateOnly day, Action back)
        {
            var slots = await _log.GetTimesheetAsync(Session.User.UserId, day);
            var (preview, sent) = await _log.GetSendStateAsync(Session.User.UserId, day);
            Present(Screen.Timesheet, new TimesheetReviewView(this, day, slots, preview, sent, back));
        }

        //-----------------------------
        //gets timesheet slots for a user and day
        public Task<IReadOnlyList<TimesheetSlot>> GetTimesheetAsync(int userId, DateOnly day) => _log.GetTimesheetAsync(userId, day);

        //-----------------------------
        //adds a manual timesheet entry
        public Task<EditResult> AddEntryAsync(EntryEdit edit) => _log.AddEntryAsync(Session.User.UserId, edit);

        //-----------------------------
        //updates an existing timesheet entry
        public Task<EditResult> UpdateEntryAsync(int entryId, EntryEdit edit) => _log.UpdateEntryAsync(Session.User.UserId, entryId, edit);

        //-----------------------------
        //deletes a timesheet entry
        public Task<EditResult> DeleteEntryAsync(int entryId) => _log.DeleteEntryAsync(Session.User.UserId, entryId);

        //-----------------------------
        //sends a day to the dashboard
        public Task<SendOutcome> SendDayAsync(DateOnly day) => _log.SendDayAsync(Session.User.UserId, day);

        //-----------------------------
        //the host name of the dashboard client
        public string DashboardHost => _log.DashboardHost;

        //-----------------------------
        //signs in to dashboard
        public Task<SignInOutcome> SignInToDashboardAsync(string email, string password) => _log.SignInToDashboardAsync(email, password);

        //-----------------------------
        //gets signed in dashboard email
        public Task<string?> GetDashboardEmailAsync() => _log.GetDashboardEmailAsync();

        //-----------------------------
        //signs out of dashboard
        public Task SignOutOfDashboardAsync() => _log.SignOutOfDashboardAsync();

        //-----------------------------
        //loads sample day for testing
        public Task<SampleDayResult> LoadSampleDayAsync() => _log.LoadSampleDayAsync(Session.User.UserId);

        //-----------------------------
        //opens the editor for an entry or a gap
        public async Task ShowEntryEditorAsync(DateOnly day, TimesheetSlot? entry, DateTime start, DateTime end, Action back)
        {
            var projects = await _log.GetProjectsAsync();
            var activities = await _log.GetActivityTreeAsync();
            Present(Screen.EntryEditor, new EntryEditView(this, day, entry, start, end, projects, activities, back));
        }

        //-----------------------------
        //asks for dashboard details then runs the next step
        public void ShowSignIn(Func<Task> afterSignIn, Action back) => Present(Screen.SignIn, new SignInView(this, afterSignIn, back));

        //-----------------------------
        //exports a day as csv
        public async Task ExportDayAsync(DateOnly day)
        {
            var summary = await _log.GetDayAsync(Session.User.UserId, day.ToDateTime(TimeOnly.MinValue));
            await ExportCsvAsync(summary);
        }

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
                await _csv.WriteAsync(dialog.FileName, day.Entries);
        }

        public async Task ChangeIntervalAsync(int minutes)
        {
            Session.Settings.CheckInIntervalMinutes = minutes;
            await _log.SaveSettingsAsync(Session.Settings);
            // The engine reads the interval from the saved settings
            await Scheduler.IntervalChangedAsync();
        }

        public void SavePreferences() => _preferences.Save(Session.Preferences);

        private void Present(Screen screen, FrameworkElement view, bool activate = true)
        {
            _screen = screen;
            _host.Present(view, activate);
        }
    }
}
