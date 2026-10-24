using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using TimePlanner.Widget.Models;
using TimePlanner.Widget.Services;
using Xunit;

namespace TimePlanner.Widget.Tests.Services
{
    //-----------------------------
    //tests for the "Start with Windows" preference: on by default, saved across restarts and kept on updates.
    //Uses a temporary widget.json, never the real one in %LOCALAPPDATA%\TimePlanner
    public class StartWithWindowsPreferenceTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"widget-{Guid.NewGuid():N}.json");

        //-----------------------------
        //deletes the temporary preferences file
        public void Dispose()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }

        private static StartupService CreateStartup(IStartupRegistration registration, string programPath) =>
            new(registration, programPath, NullLogger<StartupService>.Instance);

        //-----------------------------
        //a first run has no preferences file, so the setting is on
        [Fact]
        public void Load_FirstRun_StartWithWindowsIsOn()
        {
            var preferences = new PreferencesStore(_path).Load();

            Assert.True(preferences.StartWithWindows);
        }

        //-----------------------------
        //switching it off survives a restart
        [Fact]
        public void Save_Off_IsStillOffAfterRestart()
        {
            var store = new PreferencesStore(_path);
            var preferences = store.Load();
            preferences.StartWithWindows = false;
            store.Save(preferences);

            var reloaded = new PreferencesStore(_path).Load();

            Assert.False(reloaded.StartWithWindows);
        }

        //-----------------------------
        //a widget.json saved before the setting existed keeps its other choices and gets the setting on
        [Fact]
        public void Load_FileFromBeforeTheSetting_KeepsChoicesAndIsOn()
        {
            File.WriteAllText(_path, """{ "IdleVisibility": "Faded", "IdleShape": "Ring", "SoundOnCheckIn": false }""");

            var preferences = new PreferencesStore(_path).Load();

            Assert.True(preferences.StartWithWindows);
            Assert.Equal(IdleVisibility.Faded, preferences.IdleVisibility);
            Assert.False(preferences.SoundOnCheckIn);
        }

        //-----------------------------
        //first run: the default preference registers the widget, like an installer would on a first install
        [Fact]
        public void FirstRun_RegistersWidget()
        {
            var registration = new InMemoryStartupRegistration();
            var preferences = new PreferencesStore(_path).Load();

            CreateStartup(registration, @"C:\TimePlanner\TimePlanner.Widget.exe").Apply(preferences.StartWithWindows);

            Assert.NotNull(registration.Command);
        }

        //-----------------------------
        //an update (new version, new folder) never turns a switched-off setting back on, and an old entry is removed
        [Fact]
        public void Update_WithSettingOff_KeepsItOffAndUnregistered()
        {
            var registration = new InMemoryStartupRegistration();
            var store = new PreferencesStore(_path);

            // version 1.0.0: first run registers, then the user switches it off
            var oldStartup = CreateStartup(registration, @"C:\TimePlanner-1.0.0\TimePlanner.Widget.exe");
            var preferences = store.Load();
            oldStartup.Apply(preferences.StartWithWindows);
            preferences.StartWithWindows = false;
            store.Save(preferences);
            // an entry left over from the old version, for example one put back outside the widget
            registration.Register(oldStartup.Command!);

            // version 1.1.0 starts with the same preferences file
            var reloaded = new PreferencesStore(_path).Load();
            CreateStartup(registration, @"C:\TimePlanner-1.1.0\TimePlanner.Widget.exe").Apply(reloaded.StartWithWindows);

            Assert.False(reloaded.StartWithWindows);
            Assert.Null(registration.Command);
            Assert.False(new PreferencesStore(_path).Load().StartWithWindows);
        }

        //-----------------------------
        //an update with the setting on keeps it on and registers the new version
        [Fact]
        public void Update_WithSettingOn_StaysOnAtNewPath()
        {
            var registration = new InMemoryStartupRegistration();
            var store = new PreferencesStore(_path);
            store.Save(new WidgetPreferences { StartWithWindows = true });
            CreateStartup(registration, @"C:\TimePlanner-1.0.0\TimePlanner.Widget.exe").Apply(enabled: true);

            var updated = CreateStartup(registration, @"C:\TimePlanner-1.1.0\TimePlanner.Widget.exe");
            updated.Apply(new PreferencesStore(_path).Load().StartWithWindows);

            Assert.Equal(updated.Command, registration.Command);
        }
    }
}
