namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //readiness status for voice and day compilation features
    public sealed record AiReadiness(bool VoiceReady, bool DayReady, string? VoiceProblem, string? DayProblem)
    {
        //-----------------------------
        //decides readiness and problem descriptions from components
        public static AiReadiness Decide(bool voiceModelPresent, bool remoteTranscriber, OllamaState? ollama, bool dashboardAvailable, CompilerKind compiler)
        {
            var voiceReady = voiceModelPresent;

            //dev feature not available to users
            //remote transcriber counts as voice ready
            if (remoteTranscriber)
            {
                voiceReady = true;
            }

            var voiceProblem = voiceReady ? null : "The voice model isn't downloaded.";

            var dayReady = false;
            string? dayProblem = null;

            //evaluate day compiler readiness
            if (compiler == CompilerKind.Ollama)
            {
                //check ollama engine and model state
                if (ollama == OllamaState.Ready)
                {
                    dayReady = true;
                }
                else if (ollama == OllamaState.ModelMissing)
                {
                    dayProblem = "The day model isn't downloaded.";
                }
                else
                {
                    dayProblem = "The AI engine isn't running.";
                }
            }
            else if (compiler == CompilerKind.Dashboard)
            {
                //check dashboard availability
                if (dashboardAvailable)
                {
                    dayReady = true;
                }
                else
                {
                    dayProblem = "The AI engine isn't running.";
                }
            }
            else if (compiler == CompilerKind.OpenAiCompatible)
            {
                //dev feature not available to users
                //custom openai compatible endpoint is assumed ready
                dayReady = true;
            }

            return new AiReadiness(voiceReady, dayReady, voiceProblem, dayProblem);
        }
    }
}
//------------------------------EOF-----------------------------\\
