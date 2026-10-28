using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Media;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using TimePlanner.Core.Ai;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Services;
using TimePlanner.Core.Services.Models;
using TimePlanner.Core.Sync;
using TimePlanner.Widget.Controls;
using TimePlanner.Widget.Models;
using TimePlanner.Widget.Services;
using TimePlanner.Widget.Services.Ai;
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
            VoiceNote,
            DayBuild,
            VoiceNoteDetail,
            Exit,
            Help,
            ManageLists,
            WorkingDays,
        }

        private readonly IWidgetHost _host;
        private readonly TimeLogService _log;
        private readonly CsvExportService _csv;
        private readonly PreferencesStore _preferences;
        private readonly AiService _aiService;
        private readonly WhisperModelManager _modelManager;
        private readonly VoiceTranscriptionQueue _voiceQueue;
        private readonly IVoiceNoteStore _voiceStore;
        private readonly StartupService _startup;
        private readonly AiSetupService _aiSetup;
        private Screen _screen;
        private FrameworkElement? _view;
        private DaySummary? _endedDay;
        private bool _loaded;

        public WidgetFlow(IWidgetHost host, WidgetSession session, CheckInScheduler scheduler, SnoozeManager snooze,
            TimeLogService log, CsvExportService csv, PreferencesStore preferences, UnsentDayReminder reminder,
            StartupService startup, AiService aiService, WhisperModelManager modelManager,
            VoiceTranscriptionQueue voiceQueue, IVoiceNoteStore voiceStore, AiSetupService aiSetup)
        {
            _host = host;
            Session = session;
            Scheduler = scheduler;
            Snooze = snooze;
            _log = log;
            _csv = csv;
            _preferences = preferences;
            _aiService = aiService;
            _modelManager = modelManager;
            _voiceQueue = voiceQueue;
            _voiceStore = voiceStore;
            _aiSetup = aiSetup;
            Reminder = reminder;
            _startup = startup;

            scheduler.Ticked += (_, _) =>
            {
                if (_loaded)
                {
                    _ = Reminder.RefreshAsync(Scheduler.EndedToday);
                    _ = RetryPendingSendsAsync();
                }
                // Keeps the status line's date and tracking dot current on the screens that show it, and on the wide timesheet
                if (_view != null && ShowsStatus(_screen))
                    WidgetCard.SetStatus(_view, Status);
                _wide?.ShowStatus(Status);
                if (_screen is Screen.Idle or Screen.Timer && Scheduler.IsDue)
                    CheckIn(announce: true);
            };

            // Tracking ran past midnight: the day closed at its last activity, and Day ended says so
            scheduler.DayClosedOvernight += async (_, _) =>
            {
                if (Scheduler.ClosedOvernight is not { } closed)
                    return;
                _endedDay = await _log.GetDayAsync(Session.User.UserId, closed.StartedAt);

                // Into a day off, the widget goes quietly to the tray; Day ended waits there for the next time it is opened
                if (!IsWorkingDay)
                {
                    _screen = Screen.None;
                    _host.HideWidget();
                    return;
                }
                ShowDayEnded(_endedDay, activate: false);
            };

            // Today isn't sent yet only on working days, which the user chooses
            Reminder.IsWorkingDay = day => Session.Preferences.WorkingDays.Contains(day);
        }

        /// <summary>Today is one of the working days in the preferences.</summary>
        public bool IsWorkingDay => Session.Preferences.IsWorkingDay(Scheduler.Now);

        /// <summary>The status line on the time-tracking cards: today's date, with a dot that is green only while tracking.</summary>
        public CardStatus Status
        {
            get
            {
                var state = Scheduler.State switch
                {
                    DayState.Tracking => "Tracking",
                    DayState.Paused => "Paused",
                    DayState.Ended => "Day ended",
                    _ => "Not started",
                };
                return new CardStatus(Format.Date(DateOnly.FromDateTime(Scheduler.Now)), state, Scheduler.State == DayState.Tracking);
            }
        }

        // Only the screens about tracking time carry the status line, and the Timesheet; Settings, Help and the rest don't
        private static bool ShowsStatus(Screen screen) =>
            screen is Screen.Setup or Screen.Timer or Screen.CheckIn or Screen.LogEntry or Screen.EntrySaved
                or Screen.EndDay or Screen.DayEnded or Screen.Timesheet;

        // The ended day shows first: one ended today, or one the widget closed because it was left open overnight
        private bool ShowsEndedDay => _endedDay != null
            && (_endedDay.Day.Date == Scheduler.Now.Date || Scheduler.ClosedOvernight != null);

        public WidgetSession Session { get; }

        public CheckInScheduler Scheduler { get; }

        public SnoozeManager Snooze { get; }

        public UnsentDayReminder Reminder { get; }

        /// <summary>Why Windows startup doesn't match the setting, or null when it does. Settings shows it.</summary>
        public string? StartupProblem { get; private set; }

        public async Task LoadAsync()
        {
            _loaded = false;
            Session.User = await _log.SignInAsync();
            Session.Preferences = _preferences.Load();
            _host.ConfigurePositions(Session.Preferences, SavePreferences);
            // The first run registers the widget (the setting starts on); later runs follow the saved choice
            StartupProblem = _startup.Apply(Session.Preferences.StartWithWindows);
            (Session.ProjectId, Session.Activity) = await _log.GetLastChoiceAsync(Session.User.UserId);
            await Scheduler.InitialiseAsync();
            _endedDay = Scheduler.ClosedOvernight is { } closed ? await _log.GetDayAsync(Session.User.UserId, closed.StartedAt)
                : Scheduler.EndedToday ? await _log.GetDayAsync(Session.User.UserId, Scheduler.Now)
                : null;
            _loaded = true;
            _ = Reminder.RefreshAsync(Scheduler.EndedToday);
            _ = RetryPendingSendsAsync(force: true);
            await _voiceStore.PurgeAsync(Scheduler.Now);
            _voiceQueue.Start();
            _ = Task.Run(async () =>
            {
                try
                {
                    await _aiSetup.GetReadinessAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceWarning($"Background readiness check failed: {ex.Message}");
                }
            });
        }

        public void StartVoiceNote()
        {
            var windowStart = Scheduler.PeriodStart;
            var windowEnd = Scheduler.Now;
            var view = new VoiceNoteView(this, windowStart, windowEnd, _modelManager, _aiService);
            Present(Screen.VoiceNote, view);
        }

        public AiService Ai => _aiService;
        public OllamaHost? AiHost => _aiService.OllamaHost;
        public AiSetupService AiSetup => _aiSetup;
        public TimeLogService Logs => _log;
        public PreferencesStore Preferences => _preferences;
        public WhisperModelManager ModelManager => _modelManager;

        public async Task SaveVoiceNoteAsync(string wavPath, DateTime windowStart, DateTime windowEnd, double seconds)
        {
            await _aiService.SaveRecordingAsync(wavPath, windowStart, windowEnd, seconds);
            await Scheduler.VoiceAnsweredAsync(windowEnd);
            ShowIdle();
        }

        //-----------------------------
        //shows day compilation screen
        public void ShowDayBuild(DateOnly day, Action back)
        {
            var view = new DayBuildView(this, day, back);
            Present(Screen.DayBuild, view);
        }

        //-----------------------------
        //shows day compilation screen asynchronously
        public Task ShowDayBuildAsync(DateOnly day, Action back)
        {
            ShowDayBuild(day, back);
            return Task.CompletedTask;
        }

        //-----------------------------
        //shows detail screen for single voice note
        public async Task ShowVoiceNoteAsync(Guid id, Action back)
        {
            var note = await _aiService.GetNoteAsync(id);

            //return if note not found
            if (note == null)
            {
                back();
                return;
            }

            var view = new VoiceNoteDetailView(this, note, back);
            Present(Screen.VoiceNoteDetail, view);
        }

        //-----------------------------
        //sends again the days the user already sent that did not get through. Started without waiting, a day that cannot be sent stays waiting for the next try
        private async Task RetryPendingSendsAsync(bool force = false)
        {
            foreach (var day in await _log.RetryPendingSendsAsync(Session.User.UserId, force))
                Reminder.DaySent(day);
        }

        /// <summary>
        /// The first screen: the idle widget when today is still being tracked (the widget was restarted), Day ended
        /// for a day ended today or closed overnight, or Setup.
        /// </summary>
        public void ShowStart()
        {
            if (Scheduler.IsRunning)
                ShowIdle();
            else if (ShowsEndedDay)
                ShowDayEnded(_endedDay!);
            else
                ShowSetup();
        }

        /// <summary>
        /// Started by Windows at sign-in: on a day off nothing pops up, the widget just waits in the tray, where a day
        /// can still be started by hand. A day still being tracked (started by hand earlier) shows as usual.
        /// </summary>
        public void ShowStartAtSignIn()
        {
            if (!Scheduler.IsRunning && !IsWorkingDay)
            {
                _screen = Screen.None;
                _host.HideWidget();
                return;
            }
            ShowStart();
        }

        /// <summary>The latest earlier day with time that was never sent, for the "isn't sent yet" notice.</summary>
        public Task<UnsentDay?> GetUnsentEarlierDayAsync() => _log.GetUnsentEarlierDayAsync(Session.User.UserId);

        /// <summary>Today and the days before it, for the timesheet's day list.</summary>
        public Task<IReadOnlyList<DayChoice>> GetRecentDaysAsync() => _log.GetRecentDaysAsync(Session.User.UserId);

        /// <summary>Send now: opens that day's timesheet and starts sending it, so the entries are in view while it asks.</summary>
        public Task SendNowAsync(DateOnly day, Action back) => ShowTimesheetAsync(day, back, sendNow: true);

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
            else if (ShowsEndedDay)
                ShowDayEnded(_endedDay!);
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
                CheckIn(activate: activate);
            else
                Present(Screen.Timer, new TimerView(this), activate);
        }

        public void ShowSettings() => Present(Screen.Settings, new SettingsView(this));

        /// <summary>More options, back on the Settings screen it came from, or on Setup.</summary>
        public void ShowMoreOptions(bool fromSettings)
        {
            var view = fromSettings ? new SettingsView(this) : new SetupView(this, isSettings: false);
            Present(fromSettings ? Screen.Settings : Screen.Setup, view);
            view.ShowAdvanced(moveFocus: true);
        }

        /// <summary>The working days list, from More options.</summary>
        public void ShowWorkingDays(Action back) => Present(Screen.WorkingDays, new WorkingDaysView(this, back));

        //-----------------------------
        //shows voice and ai settings screen
        public void ShowAiSettings(Action back, bool openAdvanced = false)
        {
            var view = new AiSettingsView(this, back, openAdvanced);
            Present(Screen.Settings, view);
        }

        //opens settings → manage lists, back returns to settings
        public async Task<ManageListsView> ShowManageListsAsync()
        {
            var view = new ManageListsView(this, await _log.GetManagedListsAsync());
            Present(Screen.ManageLists, view);
            return view;
        }

        public Task<ManagedLists> GetManagedListsAsync() => _log.GetManagedListsAsync();

        public Task<(ListKind Kind, int Id)?> FindListItemAsync(bool activity, IReadOnlyList<string> path) => _log.FindListItemAsync(activity, path);

        public Task<IReadOnlyList<DateOnly>> GetSentDaysUsingAsync(ListKind kind, int id) => _log.GetSentDaysUsingAsync(Session.User.UserId, kind, id);

        public Task<ListChangeOutcome> RenameListItemAsync(ListKind kind, int id, string name) => _log.RenameAsync(Session.User.UserId, kind, id, name);

        public Task<ListChangeOutcome> SetListItemHiddenAsync(ListKind kind, int id, bool hidden) => _log.SetHiddenAsync(kind, id, hidden);

        public Task<ListChangeOutcome> AddProjectAsync(string client, string project) => _log.AddProjectAsync(client, project);

        public Task<ListChangeOutcome> AddActivityAsync(int parentId, string name) => _log.AddActivityAsync(parentId, name);

        public void ShowHelp()
        {
            if (_screen == Screen.Help)
                return;
            var previousScreen = _screen;
            var previousView = _view;
            Present(Screen.Help, new HelpView(() =>
            {
                // A check-in may have become due while reading. Other screens retain their edits.
                if (previousScreen is Screen.Idle or Screen.Timer or Screen.None || previousView == null)
                    ShowFromTray();
                else
                    Present(previousScreen, previousView);
            }));
        }

        public Task LogFromTrayAsync()
        {
            if (Scheduler.IsRunning)
                return LogTimeAsync(LogEntryMode.LogNow);
            ShowFromTray();
            return Task.CompletedTask;
        }

        public void AskExit(Action? exit = null)
        {
            if (_screen == Screen.Exit)
                return;
            var previousScreen = _screen;
            var previousView = _view;
            Present(Screen.Exit, new ExitView(this, exit ?? (() => Application.Current.Shutdown()), () =>
            {
                if (previousScreen == Screen.Idle || previousView == null)
                    ShowFromTray();
                else
                    Present(previousScreen, previousView);
            }));
        }

        public void CheckIn(bool announce = false, bool activate = false)
        {
            Present(Screen.CheckIn, new CheckInAlertView(this), activate);
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

        /// <summary>
        /// The project and activity of the last entry, for "Still on the same task" at a check-in. Null when there
        /// is none, or when either has been hidden or removed since, so the button never brings one back.
        /// </summary>
        public async Task<SameTask?> GetSameTaskAsync()
        {
            var (projectId, activity) = await _log.GetLastChoiceAsync(Session.User.UserId);
            if (projectId is not { } id || activity.Count == 0)
                return null;

            // Only active projects come back, so a hidden one is not offered
            var project = (await _log.GetProjectsAsync()).FirstOrDefault(p => p.ProjectID == id);
            if (project?.Company is not { } company)
                return null;

            // A hidden activity is not found by its path
            if (await _log.FindListItemAsync(activity: true, activity) == null)
                return null;

            return new SameTask([company.Name, project.Name], activity);
        }

        /// <summary>
        /// "Still on the same task": logs the period since the last check-in to the last entry's project and
        /// activity, the same way Log time would, then says what it logged. If they are no longer offered,
        /// the Log time form opens instead.
        /// </summary>
        public async Task LogSameTaskAsync()
        {
            if (await GetSameTaskAsync() is not { } same)
            {
                await LogTimeAsync(LogEntryMode.CheckIn);
                return;
            }

            // Non-breaking spaces keep "Project · Activity" together if the line wraps
            await SaveEntryAsync(LogEntryMode.CheckIn, GetLogPeriod(), same.ProjectPath, same.Activity, note: null,
                saved: $"Logged to {same.Short.Replace(" · ", " · ")}.");
        }

        public async Task SaveEntryAsync(LogEntryMode mode, LogPeriod period, IReadOnlyList<string> projectPath, IReadOnlyList<string> activity, string? note,
            string? saved = null)
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
                Present(Screen.EntrySaved, new EntrySavedView(this, saved));
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

        public void ShowDayEnded(DaySummary day, bool activate = true)
        {
            _endedDay = day;
            Present(Screen.DayEnded, new DayEndedView(this, day), activate);
        }

        /// <summary>Resume day carries on a day from today; once the date has changed, the scheduler starts today instead.</summary>
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
        //loads and shows one day of the timesheet, the way it was last left: the page in the widget, or the wide window
        public async Task ShowTimesheetAsync(DateOnly day, Action back, bool sendNow = false)
        {
            if (Session.Preferences.TimesheetWide)
            {
                await ShowWideTimesheetAsync(day, back, sendNow);
                return;
            }

            var (slots, preview, sent, pending) = await LoadTimesheetAsync(day);
            Present(Screen.Timesheet, new TimesheetReviewView(this, day, slots, preview, sent, back, pending, sendNow));
        }

        //-----------------------------
        //one day of the timesheet: its slots, and whether it went to the dashboard
        public async Task<(IReadOnlyList<TimesheetSlot> Slots, DayPreview Preview, SentDay? Sent, PendingDay? Pending)> LoadTimesheetAsync(DateOnly day)
        {
            var slots = await _log.GetTimesheetAsync(Session.User.UserId, day);
            var (preview, sent, pending) = await _log.GetSendStateAsync(Session.User.UserId, day);
            return (slots, preview, sent, pending);
        }

        //-----------------------------
        //the wide timesheet window, and where the widget goes back to when it is restored to the small page
        private TimesheetWindow? _wide;
        private Action _wideBack = () => { };

        //how a window is put on screen; the tests swap it so none shows
        public Action<Window> ShowWindow { get; set; } = window =>
        {
            window.Show();
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            window.Activate();
        };

        //-----------------------------
        //opens the timesheet in its wide window, which is how it opens from now on. The widget leaves the timesheet
        //(and a sign-in a send from it led to) for where the timesheet was opened from
        public async Task ShowWideTimesheetAsync(DateOnly day, Action back, bool sendNow = false)
        {
            if (!Session.Preferences.TimesheetWide)
            {
                Session.Preferences.TimesheetWide = true;
                SavePreferences();
            }
            _wideBack = back;

            var (slots, preview, sent, pending) = await LoadTimesheetAsync(day);
            if (_wide == null)
            {
                _wide = new TimesheetWindow(this);
                _wide.Closed += (_, _) => _wide = null;
            }
            _wide.ApplySize(Session.Preferences.LargerText);
            _wide.ShowStatus(Status);
            _wide.Load(day, slots, preview, sent, pending, sendNow);
            ShowWindow(_wide);

            if (_screen is Screen.Timesheet or Screen.SignIn)
                back();
        }

        //-----------------------------
        //shows another day, or the same one again after a change, in the wide window
        public Task ReopenWideTimesheetAsync(DateOnly day) => ShowWideTimesheetAsync(day, _wideBack);

        //-----------------------------
        //Restore: the wide window closes and the widget shows the same day on the small page, which is how it opens from now on
        public Task RestoreTimesheetAsync(DateOnly day)
        {
            Session.Preferences.TimesheetWide = false;
            SavePreferences();
            var wide = _wide;
            _wide = null;
            wide?.Close();
            return ShowTimesheetAsync(day, _wideBack);
        }

        //-----------------------------
        //the projects to log against, for the wide timesheet's rows
        public Task<List<Core.Domain.Entities.Project>> GetProjectsAsync() => _log.GetProjectsAsync();

        //-----------------------------
        //puts back the entries an edit changed, for the wide timesheet's undo
        public Task<EditResult> RestoreEntriesAsync(EditResult saved) => _log.RestoreEntriesAsync(Session.User.UserId, saved);

        //-----------------------------
        //gets timesheet slots for a user and day
        public Task<IReadOnlyList<TimesheetSlot>> GetTimesheetAsync(int userId, DateOnly day) => _log.GetTimesheetAsync(userId, day);

        //-----------------------------
        //adds a manual timesheet entry
        public Task<EditResult> AddEntryAsync(EntryEdit edit) => _log.AddEntryAsync(Session.User.UserId, edit);

        //-----------------------------
        //what saving an edit would do to the entries around it, without saving anything
        public Task<EditPreview> PreviewUpdateEntryAsync(int entryId, EntryEdit edit) => _log.PreviewUpdateEntryAsync(Session.User.UserId, entryId, edit);

        //-----------------------------
        //updates an existing timesheet entry. Other entries it would change are only changed once the user has agreed (confirmed)
        public Task<EditResult> UpdateEntryAsync(int entryId, EntryEdit edit, bool confirmed = false) => _log.UpdateEntryAsync(Session.User.UserId, entryId, edit, confirmed);

        //-----------------------------
        //the editor closes as soon as an edit is saved, so an undo for an edit that changed other entries waits here until the timesheet opens and shows it
        private UndoOffer? _undoOffer;

        public void OfferUndo(EditResult saved) =>
            _undoOffer = new UndoOffer("Entry updated · other entries changed", () => _log.RestoreEntriesAsync(Session.User.UserId, saved));

        //takes the waiting undo, so it is only ever shown once
        public UndoOffer? TakeUndo()
        {
            var offer = _undoOffer;
            _undoOffer = null;
            return offer;
        }

        //-----------------------------
        //deletes a timesheet entry
        public Task<EditResult> DeleteEntryAsync(int entryId) => _log.DeleteEntryAsync(Session.User.UserId, entryId);

        //-----------------------------
        //sends a day to the dashboard
        public async Task<SendOutcome> SendDayAsync(DateOnly day)
        {
            var outcome = await _log.SendDayAsync(Session.User.UserId, day);
            if (outcome.Status == SendStatus.Sent)
                Reminder.DaySent(day);
            return outcome;
        }

        public Task OpenReminderTimesheetAsync() => ShowTimesheetAsync(Reminder.Today, ShowFromTray);

        //-----------------------------
        //the host name of the dashboard client
        public string DashboardHost => _log.DashboardHost;

        //-----------------------------
        //signs in to dashboard
        public event EventHandler? DashboardSignedIn;

        public async Task<SignInOutcome> SignInToDashboardAsync(string email, string password, string? otp = null)
        {
            var outcome = await _log.SignInToDashboardAsync(email, password, otp);
            if (outcome.Status == SignInStatus.SignedIn)
            {
                DashboardSignedIn?.Invoke(this, EventArgs.Empty);
                _ = RetryPendingSendsAsync(force: true);
            }
            return outcome;
        }

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

        /// <summary>Every interval from the last check-in, or on the hour and half hour. Saved in the preferences file.</summary>
        public async Task ChangeTimingAsync(CheckInTiming timing)
        {
            Session.Preferences.CheckInTiming = timing;
            SavePreferences();
            await Scheduler.TimingChangedAsync();
        }

        /// <summary>The days the widget opens at sign-in. Saved in the preferences file, Monday first.</summary>
        public Task ChangeWorkingDaysAsync(IEnumerable<DayOfWeek> days)
        {
            Session.Preferences.WorkingDays = days.OrderBy(day => ((int)day + 6) % 7).ToList();
            SavePreferences();
            // Today may have just become a day off, or a working day again
            return Scheduler.RefreshAsync();
        }

        /// <summary>The "Larger text and buttons" switch: large view, every screen 1.5 times the size, straight away. Saved in the preferences file.</summary>
        public void ChangeLargerText(bool on)
        {
            Session.Preferences.LargerText = on;
            SavePreferences();
            _host.ApplySize();
            _wide?.ApplySize(on);
        }

        /// <summary>
        /// Changes Windows startup straight away. The choice is saved only when Windows accepted it, so the
        /// setting never claims something that isn't so; returns false when it was not changed.
        /// </summary>
        public bool ChangeStartWithWindows(bool enabled)
        {
            StartupProblem = _startup.Apply(enabled);
            if (StartupProblem != null)
                return false;

            Session.Preferences.StartWithWindows = enabled;
            SavePreferences();
            return true;
        }

        private void Present(Screen screen, FrameworkElement view, bool activate = true)
        {
            _screen = screen;
            _view = view;
            WidgetCard.SetStatus(view, ShowsStatus(screen) ? Status : null);
            _host.Present(view, activate);
        }
    }

    //-----------------------------
    //an undo the timesheet shows once, with the words for its bar
    public sealed record UndoOffer(string Message, Func<Task<EditResult>> Undo);
}
