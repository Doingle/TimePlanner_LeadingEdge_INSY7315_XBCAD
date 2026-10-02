using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;

namespace TimePlanner.Dashboard.Services.Reports
{
    //-----------------------------
    //answers questions about the activity tree from one read of the categories: the full path of an activity, its top level category and that category's colour.
    //the top level category is what screens call the "work type" or "category", sub activities belong to it and share its colour
    public class ActivityLookup
    {
        private readonly Dictionary<int, Category> _byId;

        public ActivityLookup(List<Category> all) => _byId = all.ToDictionary(c => c.CategoryId);

        public static async Task<ActivityLookup> LoadAsync(AppDbContext db) => new(await db.Categories.AsNoTracking().ToListAsync());

        public string Path(int id)
        {
            var names = new List<string>();
            for (var c = Find(id); c != null; c = c.ParentCategoryId == null ? null : Find(c.ParentCategoryId.Value))
                names.Insert(0, c.Name);
            return string.Join(" > ", names);
        }

        public string RootName(int id) => Root(id)?.Name ?? "Unknown";

        public string? RootColour(int id) => Root(id)?.Colour;

        public bool RootIsBillable(int id) => Root(id)?.IsBillable ?? true;

        private Category? Find(int id) => _byId.GetValueOrDefault(id);

        private Category? Root(int id)
        {
            var c = Find(id);
            while (c?.ParentCategoryId != null)
                c = Find(c.ParentCategoryId.Value);
            return c;
        }
    }
}
//------------------------------EOF-----------------------------\\
