namespace TimePlanner.Dashboard.Models
{
    //-----------------------------
    //how the person wants text to look (Settings > Accessibility): a size and whether it is bold or italic. It is kept in this browser only,
    //in a cookie that accessibility.js writes, and every layout reads it so the choice applies to each page as it is drawn, with no flash of the default.
    //the cookie holds the size and the switches that are on, joined by dots, for example "larger.bold". Anything not on the list is ignored,
    //so a made up cookie can never put its own text into the page
    public record TextPreferences(string Size, bool Bold, bool Italic)
    {
        public const string CookieName = "tp_text";

        //the sizes in the order the page offers them, with the share of the normal size each one is
        public static readonly IReadOnlyList<(string Value, string Name, string Percent)> Sizes = new[]
        {
            ("default", "Default", "100%"),
            ("large", "Large", "112%"),
            ("larger", "Larger", "125%"),
            ("largest", "Largest", "150%")
        };

        public static readonly TextPreferences Default = new("default", false, false);

        public static TextPreferences From(HttpRequest request)
        {
            if (!request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrEmpty(value) || value.Length > 40)
                return Default;

            var parts = value.Split('.');
            var size = Sizes.Any(s => s.Value == parts[0]) ? parts[0] : Default.Size;
            return new TextPreferences(size, parts.Contains("bold"), parts.Contains("italic"));
        }

        //what goes on <html>, always written out in full: Razor renders a data- attribute even when it is null or false, so there is no leaving one off
        public string BoldValue => Bold ? "on" : "off";
        public string ItalicValue => Italic ? "on" : "off";
    }
}
//------------------------------EOF-----------------------------\\
