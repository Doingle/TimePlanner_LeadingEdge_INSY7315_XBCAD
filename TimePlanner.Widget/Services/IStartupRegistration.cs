namespace TimePlanner.Widget.Services
{
    /// <summary>
    /// The entry that makes Windows start the widget when this user signs in. Behind an interface so the
    /// tests and the previews never touch the real machine's startup entries.
    /// </summary>
    public interface IStartupRegistration
    {
        /// <summary>The command Windows runs at sign-in, or null when the widget is not registered.</summary>
        string? GetCommand();

        /// <summary>Adds the entry, or points an existing one at this command.</summary>
        void Register(string command);

        /// <summary>Removes the entry. Nothing happens when there is none.</summary>
        void Unregister();
    }
}
