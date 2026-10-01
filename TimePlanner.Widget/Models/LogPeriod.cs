using System;
using System.Collections.Generic;
using System.Text;

namespace TimePlanner.Widget.Models
{
    public sealed record LogPeriod(DateTime Start, DateTime End, TimeSpan Worked);
}
