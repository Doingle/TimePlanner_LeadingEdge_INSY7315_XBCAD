namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //audio transcription contract
    public interface ITranscriber
    {
        Task<string> TranscribeAsync(string wavPath, TranscriptionHints hints, CancellationToken ct);

        Task<string?> TestAsync(CancellationToken ct);
    }
}
//------------------------------EOF-----------------------------\\
