namespace TimePlanner.Core.Domain.Entities
{
    //-----------------------------
    //this class represents a kind of work a task is tagged with, shown as a coloured chip when logging an entry
    public class Category
    {
        public int CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;

        //hex colour in #RRGGBB form used for chips and the day breakdown
        public string Colour { get; set; } = "#71717A";

        //categories are listed in ascending SortOrder
        public int SortOrder { get; set; }

        //non billable categories such as Break are still logged but are left out of billable totals
        public bool IsBillable { get; set; } = true;
    }
}
//------------------------------EOF-----------------------------\\
