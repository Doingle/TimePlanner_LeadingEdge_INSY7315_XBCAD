using TimePlanner.Core.Ai;
using Xunit;

namespace TimePlanner.Core.Tests.Ai
{
    //-----------------------------
    //unit tests for ai readiness decisions
    public class AiReadinessTests
    {
        //-----------------------------
        //voice model present marks voice ready
        [Fact]
        public void VoiceModelPresent_MarksVoiceReady()
        {
            var readiness = AiReadiness.Decide(true, false, OllamaState.Ready, false, CompilerKind.Ollama);

            Assert.True(readiness.VoiceReady);
            Assert.Null(readiness.VoiceProblem);
            Assert.True(readiness.DayReady);
            Assert.Null(readiness.DayProblem);
        }

        //-----------------------------
        //remote transcriber marks voice ready without model file
        [Fact]
        public void RemoteTranscriber_MarksVoiceReadyWithoutModel()
        {
            //dev feature not available to users
            var readiness = AiReadiness.Decide(false, true, OllamaState.Ready, false, CompilerKind.Ollama);

            Assert.True(readiness.VoiceReady);
            Assert.Null(readiness.VoiceProblem);
        }

        //-----------------------------
        //missing voice model reports problem
        [Fact]
        public void MissingVoiceModel_ReportsProblem()
        {
            var readiness = AiReadiness.Decide(false, false, OllamaState.Ready, false, CompilerKind.Ollama);

            Assert.False(readiness.VoiceReady);
            Assert.Equal("The voice model isn't downloaded.", readiness.VoiceProblem);
        }

        //-----------------------------
        //ollama ready marks day ready
        [Fact]
        public void OllamaReady_MarksDayReady()
        {
            var readiness = AiReadiness.Decide(true, false, OllamaState.Ready, false, CompilerKind.Ollama);

            Assert.True(readiness.DayReady);
            Assert.Null(readiness.DayProblem);
        }

        //-----------------------------
        //ollama model missing reports problem
        [Fact]
        public void OllamaModelMissing_ReportsProblem()
        {
            var readiness = AiReadiness.Decide(true, false, OllamaState.ModelMissing, false, CompilerKind.Ollama);

            Assert.False(readiness.DayReady);
            Assert.Equal("The day model isn't downloaded.", readiness.DayProblem);
        }

        //-----------------------------
        //ollama not running reports engine problem
        [Fact]
        public void OllamaNotRunning_ReportsEngineProblem()
        {
            var readiness = AiReadiness.Decide(true, false, OllamaState.NotRunning, false, CompilerKind.Ollama);

            Assert.False(readiness.DayReady);
            Assert.Equal("The AI engine isn't running.", readiness.DayProblem);
        }

        //-----------------------------
        //null ollama state reports engine problem
        [Fact]
        public void NullOllamaState_ReportsEngineProblem()
        {
            var readiness = AiReadiness.Decide(true, false, null, false, CompilerKind.Ollama);

            Assert.False(readiness.DayReady);
            Assert.Equal("The AI engine isn't running.", readiness.DayProblem);
        }

        //-----------------------------
        //dashboard available marks day ready
        [Fact]
        public void DashboardAvailable_MarksDayReady()
        {
            var readiness = AiReadiness.Decide(true, false, null, true, CompilerKind.Dashboard);

            Assert.True(readiness.DayReady);
            Assert.Null(readiness.DayProblem);
        }

        //-----------------------------
        //dashboard unavailable reports engine problem
        [Fact]
        public void DashboardUnavailable_ReportsEngineProblem()
        {
            var readiness = AiReadiness.Decide(true, false, null, false, CompilerKind.Dashboard);

            Assert.False(readiness.DayReady);
            Assert.Equal("The AI engine isn't running.", readiness.DayProblem);
        }

        //-----------------------------
        //openai compatible compiler marks day ready
        [Fact]
        public void OpenAiCompatible_MarksDayReady()
        {
            //dev feature not available to users
            var readiness = AiReadiness.Decide(true, false, null, false, CompilerKind.OpenAiCompatible);

            Assert.True(readiness.DayReady);
            Assert.Null(readiness.DayProblem);
        }
    }
}
//------------------------------EOF-----------------------------\\
