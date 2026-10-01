using System;
using System.Collections.Generic;
using System.Text;

namespace TimePlanner.Widget.Models
{
    public sealed class ActivityNode(string label, params ActivityNode[] children)
    {
        public string Label { get; } = label;

        public List<ActivityNode> Children { get; } = [.. children];
    }
}
