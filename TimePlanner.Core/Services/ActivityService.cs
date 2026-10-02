using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Repositories.Interfaces;
using TimePlanner.Core.Services.Models;

namespace TimePlanner.Core.Services
{
    //-----------------------------
    //serves the activity tree and adds sub activities
    public class ActivityService
    {
        //root is 1
        public const int MaxDepth = 3;

        //sets longest activity name constraint
        public const int MaxNameLength = 60;

        private readonly ICategoryRepository _categories;
        private readonly ITimeEntryRepository _entries;

        public ActivityService(ICategoryRepository categories, ITimeEntryRepository entries)
        {
            _categories = categories;
            _entries = entries;
        }

        //-----------------------------
        //full tree in picker order
        public async Task<IReadOnlyList<ActivityTreeNode>> GetTreeAsync()
        {
            var all = await _categories.GetAllAsync();
            var roots = all
                .Where(c => c.ParentCategoryId == null && !c.IsArchived)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name);

            return roots.Select(r => BuildNode(r, all, Array.Empty<string>(), r)).ToList();
        }

        //-----------------------------
        //one node and its children with root colour and billing
        private static ActivityTreeNode BuildNode(Category category, List<Category> all, IReadOnlyList<string> parentPath, Category root)
        {
            var path = parentPath.Append(category.Name).ToList();
            var children = all
                .Where(c => c.ParentCategoryId == category.CategoryId && !c.IsArchived)
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => BuildNode(c, all, path, root))
                .ToList();

            return new ActivityTreeNode(category.CategoryId, category.Name, path, root.Colour, root.IsBillable, children);
        }

        //-----------------------------
        //names from the root down to the activity
        public async Task<IReadOnlyList<string>> GetPathAsync(int categoryId)
        {
            var all = await _categories.GetAllAsync();
            return BuildPath(categoryId, all);
        }

        //-----------------------------
        //every activity with its path and root details
        public async Task<IReadOnlyDictionary<int, ActivityInfo>> GetActivityLookupAsync()
        {
            var all = await _categories.GetAllAsync();

            return all.ToDictionary(
                c => c.CategoryId,
                c =>
                {
                    var root = FindRoot(c, all);
                    return new ActivityInfo(c.CategoryId, BuildPath(c.CategoryId, all), root.CategoryId, root.Name, root.Colour, root.IsBillable);
                });
        }

        //-----------------------------
        //walks up the parents to build a path
        private static IReadOnlyList<string> BuildPath(int categoryId, List<Category> all)
        {
            var byId = all.ToDictionary(c => c.CategoryId);
            var path = new List<string>();
            int? currentId = categoryId;

            //steps move up one level
            while (currentId != null && byId.TryGetValue(currentId.Value, out var current))
            {
                path.Insert(0, current.Name);
                currentId = current.ParentCategoryId;
            }

            return path;
        }

        //-----------------------------
        //top level activity above a node
        private static Category FindRoot(Category category, List<Category> all)
        {
            var current = category;

            //steps until no parent
            while (current.ParentCategoryId != null)
            {
                current = all.First(c => c.CategoryId == current.ParentCategoryId);
            }

            return current;
        }

        //-----------------------------
        //finds activity by path ignoring case
        public async Task<int?> FindByPathAsync(IReadOnlyList<string> path)
        {
            var all = await _categories.GetAllAsync();
            int? parentId = null;
            Category? match = null;

            //each name must be child of last match
            foreach (var name in path)
            {
                match = all.FirstOrDefault(c =>
                    c.ParentCategoryId == parentId &&
                    !c.IsArchived &&
                    string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

                //missing level gets no match
                if (match == null)
                {
                    return null;
                }

                parentId = match.CategoryId;
            }

            return match?.CategoryId;
        }

        //-----------------------------
        //adds sub activity or reuses same named one
        public async Task<int> AddActivityAsync(int parentCategoryId, string name)
        {
            var clean = name?.Trim() ?? string.Empty;

            //names follow the shared rules
            if (!NameRules.IsValidActivityName(clean))
            {
                throw new ArgumentException("Activity name must be 1 to 60 characters.", nameof(name));
            }

            var all = await _categories.GetAllAsync();
            var parent = all.FirstOrDefault(c => c.CategoryId == parentCategoryId)
                ?? throw new ArgumentException("Parent activity does not exist.", nameof(parentCategoryId));

            //tree stops at MaxDepth levels
            if (BuildPath(parent.CategoryId, all).Count >= MaxDepth)
            {
                throw new InvalidOperationException("Activities can only be nested three levels deep.");
            }

            var existing = all.FirstOrDefault(c =>
                c.ParentCategoryId == parent.CategoryId &&
                string.Equals(c.Name, clean, StringComparison.OrdinalIgnoreCase));

            //sibling with same name gets reused
            if (existing != null)
            {
                //a hidden activity comes back when typed again
                if (existing.IsArchived)
                {
                    existing.IsArchived = false;
                    await _categories.UpdateAsync(existing);
                }

                return existing.CategoryId;
            }

            var root = FindRoot(parent, all);
            var created = new Category
            {
                Name = clean,
                ParentCategoryId = parent.CategoryId,
                Colour = root.Colour,
                IsBillable = root.IsBillable
            };

            await _categories.AddAsync(created);
            return created.CategoryId;
        }

        //-----------------------------
        //most recently logged activities newest first
        public async Task<IReadOnlyList<RecentActivity>> GetRecentAsync(int userId, int count = 5)
        {
            var ids = await _entries.GetRecentCategoryIdsAsync(userId, count);
            var all = await _categories.GetAllAsync();
            var activeIds = all.Where(c => !c.IsArchived).Select(c => c.CategoryId).ToHashSet();
            return ids.Where(id => activeIds.Contains(id)).Select(id => new RecentActivity(id, BuildPath(id, all))).ToList();
        }

        //-----------------------------
        //finds a path or adds its missing lower levels
        public async Task<int> FindOrAddPathAsync(IReadOnlyList<string> path)
        {
            //a path needs at least its top level
            if (path.Count == 0)
            {
                throw new ArgumentException("Choose an activity.", nameof(path));
            }

            var id = await FindByPathAsync(new[] { path[0] })
                ?? throw new ArgumentException("Choose one of the main activities.", nameof(path));

            //each lower level is found or added
            foreach (var name in path.Skip(1))
            {
                id = await AddActivityAsync(id, name);
            }

            return id;
        }
    }
}
//------------------------------EOF-----------------------------\\
