namespace TimePlanner.Core.Domain.Entities
{
    //-----------------------------
    //this class represents one node of the activity tree where top level categories carry the colour and billable flag for everything below them
    public class Category
    {
        public int CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;

        //the parent activity which is null for top level categories such as Coding
        public int? ParentCategoryId { get; set; }
        public Category? Parent { get; set; }

        public string Colour { get; set; } = "#71717A";

        //categories are listed in ascending SortOrder
        public int SortOrder { get; set; }

        public bool IsBillable { get; set; } = true;

        //hidden from pickers but kept because time was logged to it
        public bool IsArchived { get; set; }
    }
}
//------------------------------EOF-----------------------------\\
