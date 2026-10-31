namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //a continuous range of time
    public sealed record TimeRange(DateTime Start, DateTime End);

    //-----------------------------
    //the lifecycle of a voice note
    public enum VoiceNoteStatus
    {
        Recorded,
        Transcribed,
        Failed,
        Built
    }

    //-----------------------------
    //supported day compilation backends
    public enum CompilerKind
    {
        Ollama,
        OpenAiCompatible,
        Dashboard
    }

    //-----------------------------
    //supported hardware choices for day model
    public enum HardwareChoice
    {
        Automatic,
        Graphics,
        Processor
    }

    //-----------------------------
    //supported hardware choices for voice recognition
    public enum VoiceHardwareChoice
    {
        Automatic,
        Processor
    }

    //-----------------------------
    //speed benchmark outcome
    public sealed record SpeedResult(int? GraphicsTokensPerSec, int? ProcessorTokensPerSec,
        double? VoiceSecondsGraphics, double? VoiceSecondsProcessor, DateTime? CheckedAt)
    {
        public int? GpuLayers => (GraphicsTokensPerSec.HasValue && ProcessorTokensPerSec.HasValue && GraphicsTokensPerSec.Value >= ProcessorTokensPerSec.Value * 1.15)
            ? null
            : (GraphicsTokensPerSec.HasValue && !ProcessorTokensPerSec.HasValue ? null : 0);
    }

    //-----------------------------
    //supported transcription backends
    public enum TranscriberKind
    {
        LocalWhisper,
        OpenAiCompatible
    }

    //-----------------------------
    //a single recorded voice update
    public sealed class VoiceNote
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateOnly Day { get; set; }
        public DateTime WindowStart { get; set; }
        public DateTime WindowEnd { get; set; }
        public DateTime RecordedAt { get; set; }
        public double Seconds { get; set; }
        public string? AudioFile { get; set; }
        public string? Transcript { get; set; }
        public VoiceNoteStatus Status { get; set; } = VoiceNoteStatus.Recorded;
        public int Attempts { get; set; }
        public string? Error { get; set; }
        public DateTime? BuiltAt { get; set; }
    }

    //-----------------------------
    //hints to guide speech recognition
    public sealed record TranscriptionHints(IReadOnlyList<string> Vocabulary, string Language = "en");

    //-----------------------------
    //numbered project for prompt compactness
    public sealed record CatalogProject(string Id, string Company, string Project);

    //-----------------------------
    //numbered activity path for prompt compactness
    public sealed record CatalogActivity(string Id, IReadOnlyList<string> Path);

    //-----------------------------
    //check in window with unlogged time
    public sealed record VoiceWindow(string Id, Guid NoteId, DateTime Start, DateTime End, IReadOnlyList<TimeRange> Free, string Transcript);

    //-----------------------------
    //payload to build timesheet entries
    public sealed record DayBuildRequest(DateOnly Day, IReadOnlyList<VoiceWindow> Windows,
        IReadOnlyList<CatalogProject> Projects, IReadOnlyList<CatalogActivity> Activities);

    //-----------------------------
    //proposed entry from model output
    public sealed record SuggestedEntry(string WindowId, DateTime Start, DateTime End,
        IReadOnlyList<string> ProjectPath, IReadOnlyList<string> ActivityPath, string Note);

    //-----------------------------
    //problem flagged during compilation
    public sealed record BuildFlag(string? WindowId, string Message);

    //-----------------------------
    //outcome of a day build call
    public sealed record DayBuildResult(IReadOnlyList<SuggestedEntry> Entries, IReadOnlyList<BuildFlag> Flags,
        TimeSpan Took, string Model, int? OutputTokens);

    //-----------------------------
    //configuration for ai features
    public sealed class AiSettings
    {
        public bool VoiceEnabled { get; set; }
        public bool SetupCompleted { get; set; }
        public CompilerKind Compiler { get; set; } = CompilerKind.Ollama;
        public string CompilerUrl { get; set; } = "http://localhost:11434";
        public string CompilerModel { get; set; } = "qwen2.5:3b";
        public int ContextTokens { get; set; } = 8192;
        public int TimeoutSeconds { get; set; } = 300;
        //null lets ollama decide and 0 means cpu only
        public int? GpuLayers { get; set; }
        public HardwareChoice DayHardware { get; set; } = HardwareChoice.Automatic;
        public VoiceHardwareChoice VoiceHardware { get; set; } = VoiceHardwareChoice.Automatic;
        public SpeedResult? LastSpeedCheck { get; set; }
        public TranscriberKind Transcriber { get; set; } = TranscriberKind.LocalWhisper;
        public string WhisperModel { get; set; } = "ggml-small.en-q5_1.bin";
        public string? WhisperModelPath { get; set; }
        public string TranscriberUrl { get; set; } = "";
        public string TranscriberModel { get; set; } = "whisper-large-v3-turbo";
        public string? MicrophoneDeviceId { get; set; }
        public bool OllamaManagedByWidget { get; set; }
        public bool SlowPcHintDismissed { get; set; }
    }

    //-----------------------------
    //fixed limits for ai operations
    public static class AiLimits
    {
        public const int Note = 500;
        public const int NoteTarget = 350;
        public const int RecordingSeconds = 180;
        public const int RecordingWarnSeconds = 150;
        public const int KeepDays = 30;
        public const int MaxTranscribeAttempts = 3;
    }
}
//------------------------------EOF-----------------------------\\
