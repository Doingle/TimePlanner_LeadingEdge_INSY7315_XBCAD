namespace TimePlanner.Core.Sync
{
    //-----------------------------
    //token kept in memory for tests and non windows hosts
    public sealed class InMemoryTokenStore : ITokenStore
    {
        private StoredToken? _token;

        //-----------------------------
        //returns the token saved in memory
        public Task<StoredToken?> LoadAsync() => Task.FromResult(_token);

        //-----------------------------
        //stores the token in memory
        public Task SaveAsync(StoredToken token)
        {
            _token = token;
            return Task.CompletedTask;
        }

        //-----------------------------
        //clears the token in memory
        public Task ClearAsync()
        {
            _token = null;
            return Task.CompletedTask;
        }
    }
}
//------------------------------EOF-----------------------------\\
