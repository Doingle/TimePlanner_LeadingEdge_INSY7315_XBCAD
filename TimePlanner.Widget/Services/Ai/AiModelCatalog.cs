namespace TimePlanner.Widget.Services.Ai
{
    //-----------------------------
    //day model descriptor for catalog displays
    public sealed record DayModelOption(string Model, string Label, string SizeEstimate = "about 2 GB")
    {
        public string Id => Model;
    }

    //-----------------------------
    //voice model descriptor for catalog displays
    public sealed record VoiceModelOption(string File, string Label, string About)
    {
        public string SizeEstimate => About;
    }

    //-----------------------------
    //central catalog of supported day and voice models
    public static class AiModelCatalog
    {
        public static readonly IReadOnlyList<DayModelOption> DayModels =
        [
            new("qwen2.5:3b", "Recommended"),
            new("qwen2.5:1.5b", "Faster, less accurate")
        ];

        public static readonly IReadOnlyList<VoiceModelOption> VoiceModels =
        [
            new("ggml-base.en.bin", "Fast", "about 150 MB"),
            new("ggml-small.en-q5_1.bin", "Balanced (recommended)", "about 190 MB"),
            new("ggml-large-v3-turbo-q5_0.bin", "Most accurate", "about 570 MB")
        ];
    }
}
//------------------------------EOF-----------------------------\\
