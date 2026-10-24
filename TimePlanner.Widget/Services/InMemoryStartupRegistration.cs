namespace TimePlanner.Widget.Services
{
    /// <summary>A startup entry kept in memory, for the previews and the tests.</summary>
    public sealed class InMemoryStartupRegistration : IStartupRegistration
    {
        public string? Command { get; private set; }

        public string? GetCommand() => Command;

        public void Register(string command) => Command = command;

        public void Unregister() => Command = null;
    }
}
