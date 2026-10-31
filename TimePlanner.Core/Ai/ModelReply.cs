using System.Text.Json.Serialization;

namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //raw response payload from the model
    public sealed class ModelReply
    {
        private List<ModelEntryReply>? _entries;
        private List<ModelFlagReply>? _flags;

        [JsonPropertyName("entries")]
        public List<ModelEntryReply> Entries
        {
            get => _entries ??= [];
            set => _entries = value ?? [];
        }

        [JsonPropertyName("flags")]
        public List<ModelFlagReply> Flags
        {
            get => _flags ??= [];
            set => _flags = value ?? [];
        }
    }

    //-----------------------------
    //single entry proposed by the model
    public sealed class ModelEntryReply
    {
        [JsonPropertyName("w")]
        public string? W { get; set; }

        [JsonPropertyName("s")]
        public string? S { get; set; }

        [JsonPropertyName("e")]
        public string? E { get; set; }

        [JsonPropertyName("p")]
        public string? P { get; set; }

        [JsonPropertyName("a")]
        public string? A { get; set; }

        [JsonPropertyName("note")]
        public string? Note { get; set; }
    }

    //-----------------------------
    //flag or issue reported by the model
    public sealed class ModelFlagReply
    {
        [JsonPropertyName("w")]
        public string? W { get; set; }

        [JsonPropertyName("msg")]
        public string? Msg { get; set; }
    }
}
//------------------------------EOF-----------------------------\\
