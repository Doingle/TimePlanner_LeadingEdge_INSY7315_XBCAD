namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //factory methods for ai components
    public static class AiProviders
    {
        //-----------------------------
        //instantiates configured day compiler
        public static IDayCompiler CreateCompiler(AiSettings s, HttpClient http, string? apiKey, DayPromptBuilder prompt)
        {
            //use ollama backend for local compilation
            if (s.Compiler == CompilerKind.Ollama)
            {
                return new OllamaDayCompiler(http, s, prompt);
            }

            return new OpenAiCompatibleDayCompiler(http, s, apiKey, prompt);
        }

        //-----------------------------
        //instantiates remote transcriber or null for local whisper
        public static ITranscriber? CreateRemoteTranscriber(AiSettings s, HttpClient http, string? apiKey)
        {
            //local whisper handled by widget assembly
            if (s.Transcriber == TranscriberKind.LocalWhisper)
            {
                return null;
            }

            return new OpenAiCompatibleTranscriber(http, s, apiKey);
        }
    }
}
//------------------------------EOF-----------------------------\\
