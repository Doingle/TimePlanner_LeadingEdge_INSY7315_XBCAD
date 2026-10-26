using System.IO;
using TimePlanner.Core.Services.Models;
using TimePlanner.Widget.Models;
using TimePlanner.Widget.Services;
using Xunit;

namespace TimePlanner.Widget.Tests.Services
{
    //-----------------------------
    //tests for the working days and check-in timing preferences, kept in widget.json with no database change.
    //Uses a temporary widget.json, never the real one in %LOCALAPPDATA%\TimePlanner
    public class WorkingDaysPreferenceTests : IDisposable
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

        //-----------------------------
        //a first run, or a file from before the settings, works Monday to Friday from the last check-in
        [Fact]
        public void Load_Defaults_MondayToFridayFromLastCheckIn()
        {
            File.WriteAllText(_path, """{ "IdleShape": "Ring" }""");

            var preferences = new PreferencesStore(_path).Load();

            Assert.Equal([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday], preferences.WorkingDays);
            Assert.Equal(CheckInTiming.FromLastCheckIn, preferences.CheckInTiming);
            Assert.Equal(IdleShape.Ring, preferences.IdleShape);
            Assert.False(preferences.IsWorkingDay(new DateTime(2026, 10, 24)));
            Assert.True(preferences.IsWorkingDay(new DateTime(2026, 10, 26)));
        }

        //-----------------------------
        //both choices survive a restart, saved by name
        [Fact]
        public void SaveAndLoad_KeepsTheChoices()
        {
            var store = new PreferencesStore(_path);
            store.Save(new WidgetPreferences
            {
                WorkingDays = [DayOfWeek.Sunday, DayOfWeek.Monday],
                CheckInTiming = CheckInTiming.OnTheHalfHour,
                LargerText = true,
            });

            var reloaded = new PreferencesStore(_path).Load();

            Assert.Equal([DayOfWeek.Sunday, DayOfWeek.Monday], reloaded.WorkingDays);
            Assert.Equal(CheckInTiming.OnTheHalfHour, reloaded.CheckInTiming);
            Assert.True(reloaded.LargerText);
            Assert.Contains("\"OnTheHalfHour\"", File.ReadAllText(_path));
        }

        //-----------------------------
        //a value the widget doesn't offer falls back to the defaults rather than stopping the widget
        [Theory]
        [InlineData("""{ "CheckInTiming": "Sometimes" }""")]
        [InlineData("""{ "WorkingDays": null }""")]
        [InlineData("""{ "WorkingDays": [ "Funday" ] }""")]
        public void Load_UnknownValues_FallBackToDefaults(string json)
        {
            File.WriteAllText(_path, json);

            var preferences = new PreferencesStore(_path).Load();

            Assert.Equal(5, preferences.WorkingDays.Count);
            Assert.Equal(CheckInTiming.FromLastCheckIn, preferences.CheckInTiming);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("[\"Monday\", \"Funday\"]")]
        [InlineData("[1]")]
        [InlineData("false")]
        public void InvalidWorkingDays_KeepOtherValidSettings(string days)
        {
            File.WriteAllText(_path, "{\"WorkingDays\":" + days +
                ",\"CheckInTiming\":\"OnTheHalfHour\",\"LargerText\":true,\"StartWithWindows\":false,\"Ai\":{\"VoiceEnabled\":true}}");
            var loaded = new PreferencesStore(_path).Load();
            Assert.Equal(5, loaded.WorkingDays.Count);
            Assert.Equal(CheckInTiming.OnTheHalfHour, loaded.CheckInTiming);
            Assert.True(loaded.LargerText);
            Assert.False(loaded.StartWithWindows);
            Assert.True(loaded.Ai.VoiceEnabled);
        }

        [Fact]
        public void EmptyWorkingDaysAndInvalidTiming_KeepIndependentChoices()
        {
            File.WriteAllText(_path, """{"WorkingDays":[],"CheckInTiming":"Unknown","LargerText":true,"IdleShape":"Ring"}""");
            var loaded = new PreferencesStore(_path).Load();
            Assert.Empty(loaded.WorkingDays);
            Assert.Equal(CheckInTiming.FromLastCheckIn, loaded.CheckInTiming);
            Assert.True(loaded.LargerText);
            Assert.Equal(IdleShape.Ring, loaded.IdleShape);
        }
    }
}
