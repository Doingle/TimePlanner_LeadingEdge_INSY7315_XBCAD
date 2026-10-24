using System.Text.Json;
using TimePlanner.Core.Ai;
using Xunit;

namespace TimePlanner.Core.Tests.Ai
{
    //-----------------------------
    //unit tests for prompt and schema formatting
    public class DayPromptBuilderTests
    {
        //-----------------------------
        //builds exact compact user prompt text
        [Fact]
        public void BuildUser_TwoWindows_MatchesExpectedFormat()
        {
            var day = new DateOnly(2026, 10, 22);
            var projects = new[]
            {
                new CatalogProject("P1", "Leading Edge (Internal)", "Internal"),
                new CatalogProject("P2", "Acme Foods", "Ordering App")
            };

            var activities = new[]
            {
                new CatalogActivity("A1", ["Coding"]),
                new CatalogActivity("A2", ["Coding", "Bug fixing"])
            };

            var windows = new[]
            {
                new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 8, 30, 0), new DateTime(2026, 10, 22, 10, 0, 0),
                    [new TimeRange(new DateTime(2026, 10, 22, 8, 30, 0), new DateTime(2026, 10, 22, 10, 0, 0))],
                    "started on acme ordering app"),
                new VoiceWindow("W2", Guid.NewGuid(), new DateTime(2026, 10, 22, 10, 0, 0), new DateTime(2026, 10, 22, 11, 30, 0),
                    [new TimeRange(new DateTime(2026, 10, 22, 10, 15, 0), new DateTime(2026, 10, 22, 11, 30, 0))],
                    "carried on with bug fixing")
            };

            var request = new DayBuildRequest(day, windows, projects, activities);
            var userText = DayPromptBuilder.Default.BuildUser(request);

            var expected = "DAY 2026-10-22\n" +
                           "PROJECTS\n" +
                           "P1 Leading Edge (Internal) › Internal\n" +
                           "P2 Acme Foods › Ordering App\n" +
                           "ACTIVITIES\n" +
                           "A1 Coding\n" +
                           "A2 Coding › Bug fixing\n" +
                           "WINDOWS\n" +
                           "W1 08:30-10:00 free 08:30-10:00\n" +
                           "W2 10:00-11:30 free 10:15-11:30\n" +
                           "NOTES\n" +
                           "W1: started on acme ordering app\n" +
                           "W2: carried on with bug fixing";

            Assert.Equal(expected, userText);
        }

        //-----------------------------
        //schema enum arrays contain fallback zero values and ids
        [Fact]
        public void BuildSchema_ContainsP0A0AndIds()
        {
            var day = new DateOnly(2026, 10, 22);
            var projects = new[] { new CatalogProject("P1", "Company", "Project") };
            var activities = new[] { new CatalogActivity("A1", ["Coding"]) };
            var windows = new[]
            {
                new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0), [], "note")
            };

            var request = new DayBuildRequest(day, windows, projects, activities);
            var schemaJson = DayPromptBuilder.Default.BuildSchema(request);

            using var doc = JsonDocument.Parse(schemaJson);
            var entryProps = doc.RootElement.GetProperty("properties").GetProperty("entries").GetProperty("items").GetProperty("properties");

            var pEnum = entryProps.GetProperty("p").GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToList();
            var aEnum = entryProps.GetProperty("a").GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToList();
            var wEnum = entryProps.GetProperty("w").GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToList();

            Assert.Contains("P0", pEnum);
            Assert.Contains("P1", pEnum);
            Assert.Contains("A0", aEnum);
            Assert.Contains("A1", aEnum);
            Assert.Contains("W1", wEnum);
        }

        //-----------------------------
        //collapses multiple whitespace characters in note transcript
        [Fact]
        public void BuildUser_CollapsesWhitespaceInNotes()
        {
            var day = new DateOnly(2026, 10, 22);
            var windows = new[]
            {
                new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 22, 9, 0, 0), new DateTime(2026, 10, 22, 10, 0, 0),
                    [], "   so   um \n\t started   working  ")
            };

            var request = new DayBuildRequest(day, windows, [], []);
            var userText = DayPromptBuilder.Default.BuildUser(request);

            Assert.Contains("W1: so um started working", userText);
        }
    }
}
//------------------------------EOF-----------------------------\\
