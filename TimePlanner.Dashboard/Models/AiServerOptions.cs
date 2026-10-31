namespace TimePlanner.Dashboard.Models
{
    //-----------------------------
    //configuration for server side organisation ai
    public sealed class AiServerOptions
    {
        public string Url { get; set; } = "";
        public string Model { get; set; } = "";
        //set ai apikey in azure app settings or user secrets only
        public string ApiKey { get; set; } = "";

        //-----------------------------
        //checks whether all required server ai fields are configured
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Url) &&
            !string.IsNullOrWhiteSpace(Model) &&
            !string.IsNullOrWhiteSpace(ApiKey);
    }
}
//------------------------------EOF-----------------------------\\
