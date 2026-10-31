namespace TimePlanner.Widget.Services
{
    //-----------------------------
    //manages developer mode activation
    public sealed class DevMode
    {
        public static DevMode Current { get; } = new();

        public static bool Enabled
        {
            get => Current.IsEnabled;
            set => Current.IsEnabled = value;
        }

        public bool IsEnabled { get; set; }

        //-----------------------------
        //initializes dev mode state
        public DevMode(bool enabled = false)
        {
            IsEnabled = enabled;
        }
    }
}
//------------------------------EOF-----------------------------\\
