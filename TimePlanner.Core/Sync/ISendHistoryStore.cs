namespace TimePlanner.Core.Sync
{
    //-----------------------------
    //local record of days already sent
    public interface ISendHistoryStore
    {
        //newest first
        Task<IReadOnlyList<SentDay>> GetAsync();

        //adds a day or replaces the same day
        Task AddAsync(SentDay sent);
    }
}
//------------------------------EOF-----------------------------\\
