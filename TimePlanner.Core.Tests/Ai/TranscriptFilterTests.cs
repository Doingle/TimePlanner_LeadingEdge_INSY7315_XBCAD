using TimePlanner.Core.Ai;
using Xunit;

namespace TimePlanner.Core.Tests.Ai
{
    //-----------------------------
    //unit tests for transcript cleaning and prompt echo removal
    public class TranscriptFilterTests
    {
        //-----------------------------
        //prompt echo with connector word returns empty string
        [Fact]
        public void PromptEchoWithConnector_ReturnsEmptyString()
        {
            var segments = new List<(string Text, float? NoSpeech)>
            {
                ("and design", null)
            };

            var cleaned = TranscriptFilter.Clean(segments, ["Design"]);
            Assert.Equal("", cleaned);
        }

        //-----------------------------
        //real spoken sentence is kept intact
        [Fact]
        public void RealSpokenSentence_IsKept()
        {
            var segments = new List<(string Text, float? NoSpeech)>
            {
                ("I worked on the user interface today.", 0.05f)
            };

            var cleaned = TranscriptFilter.Clean(segments, ["Design"]);
            Assert.Equal("I worked on the user interface today.", cleaned);
        }

        //-----------------------------
        //segment with high no speech probability is dropped
        [Fact]
        public void HighNoSpeechProbability_IsDropped()
        {
            var segments = new List<(string Text, float? NoSpeech)>
            {
                ("hallucinated noise", 0.85f),
                ("real speech here", 0.15f)
            };

            var cleaned = TranscriptFilter.Clean(segments, ["Design"]);
            Assert.Equal("real speech here", cleaned);
        }

        //-----------------------------
        //all segments silent returns empty string
        [Fact]
        public void AllSilentSegments_ReturnsEmptyString()
        {
            var segments = new List<(string Text, float? NoSpeech)>
            {
                ("static noise", 0.95f)
            };

            var cleaned = TranscriptFilter.Clean(segments, []);
            Assert.Equal("", cleaned);
        }
    }
}
//------------------------------EOF-----------------------------\\
