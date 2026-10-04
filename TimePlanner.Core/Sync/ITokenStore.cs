namespace TimePlanner.Core.Sync
{
    //-----------------------------
    //keeps the dashboard access token between runs
    public interface ITokenStore
    {
        Task<StoredToken?> LoadAsync();
        Task SaveAsync(StoredToken token);
        Task ClearAsync();
    }
}
//------------------------------EOF-----------------------------\\
