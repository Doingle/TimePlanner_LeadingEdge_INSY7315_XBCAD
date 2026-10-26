namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //storage contract for voice notes and ai markers
    public interface IVoiceNoteStore
    {
        string AudioFolder { get; }

        Task AddAsync(VoiceNote note);

        Task UpdateAsync(VoiceNote note);

        Task DeleteAsync(Guid id);

        Task<IReadOnlyList<VoiceNote>> GetDayAsync(DateOnly day);

        Task<IReadOnlyList<VoiceNote>> GetToTranscribeAsync();

        Task<DateTime?> GetLatestWindowEndAsync(DateOnly day);

        Task<IReadOnlyList<int>> GetAiEntryIdsAsync(DateOnly day);

        Task AddAiEntryIdsAsync(DateOnly day, IEnumerable<int> ids);

        Task RemoveAiEntryIdAsync(DateOnly day, int id);

        Task ClearAiEntryIdsAsync(DateOnly day);

        Task PurgeAsync(DateTime now);
    }
}
//------------------------------EOF-----------------------------\\
