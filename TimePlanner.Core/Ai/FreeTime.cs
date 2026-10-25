namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //free time inside a window
    public static class FreeTime
    {
        //-----------------------------
        //cuts busy spans out of a window
        public static IReadOnlyList<TimeRange> Within(TimeRange window, IEnumerable<TimeRange> busy)
        {
            //an empty window has no free time
            if (window.End <= window.Start)
            {
                return [];
            }

            var free = new List<TimeRange> { window };

            //subtract each busy slice from all current free slices
            foreach (var b in busy.OrderBy(b => b.Start))
            {
                var next = new List<TimeRange>();

                //cut the busy block out of each slice
                foreach (var f in free)
                {
                    //no overlap keeps the free span
                    if (b.End <= f.Start || b.Start >= f.End)
                    {
                        next.Add(f);
                        continue;
                    }

                    //keep part before the busy block
                    if (b.Start > f.Start)
                    {
                        next.Add(new TimeRange(f.Start, b.Start));
                    }

                    //keep part after the busy block
                    if (b.End < f.End)
                    {
                        next.Add(new TimeRange(b.End, f.End));
                    }
                }

                free = next;
            }

            //drop spans under one minute
            return free.Where(f => f.End - f.Start >= TimeSpan.FromMinutes(1)).ToList();
        }

        //-----------------------------
        //clips a piece to the unlogged ranges
        public static IReadOnlyList<TimeRange> Intersect(TimeRange piece, IReadOnlyList<TimeRange> free) =>
            free.Select(f => new TimeRange(f.Start > piece.Start ? f.Start : piece.Start, f.End < piece.End ? f.End : piece.End))
                .Where(r => r.End - r.Start >= TimeSpan.FromMinutes(1)).ToList();
    }
}
//------------------------------EOF-----------------------------\\
