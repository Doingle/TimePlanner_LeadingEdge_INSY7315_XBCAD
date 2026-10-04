using System;
using System.Collections.Generic;
using System.Text;

namespace TimePlanner.Widget.Models
{
    public sealed record ActivityChoices(List<ActivityNode> Tree, List<IReadOnlyList<string>> Recent)
    {
        public static ActivityChoices Empty() => new([], []);
    }
}
