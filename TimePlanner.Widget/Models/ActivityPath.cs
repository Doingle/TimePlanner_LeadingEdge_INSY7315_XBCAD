using System;
using System.Collections.Generic;
using System.Text;
using TimePlanner.Core.Domain.Entities;

namespace TimePlanner.Widget.Models
{
    public static class ActivityPath
    {
        public const string Separator = " › ";


        public static IReadOnlyList<string> Of(WorkTask task) =>
            [.. task.Category.Split('›', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), task.Name];


        public static string CategoryOf(IReadOnlyList<string> path) => string.Join(Separator, path.Take(path.Count - 1));


        public static bool Same(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
            a.Count == b.Count && a.Zip(b).All(pair => string.Equals(pair.First, pair.Second, StringComparison.OrdinalIgnoreCase));

        public static List<ActivityNode> BuildTree(IEnumerable<WorkTask> tasks)
        {
            var tree = new List<ActivityNode>();

            foreach (var task in tasks.Where(t => t.Name.Length > 0))
                Include(tree, Of(task));

            return tree;
        }


        public static void Include(List<ActivityNode> tree, IReadOnlyList<string> path)
        {
            var level = tree;
            foreach (var label in path)
            {
                var node = level.FirstOrDefault(n => string.Equals(n.Label, label, StringComparison.OrdinalIgnoreCase));
                if (node == null)
                {
                    node = new ActivityNode(label);
                    level.Add(node);
                }
                level = node.Children;
            }
        }
    }
}
