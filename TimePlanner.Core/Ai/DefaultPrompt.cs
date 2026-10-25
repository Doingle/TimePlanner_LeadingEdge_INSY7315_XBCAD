namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //fallback prompt text embedded in the assembly
    public static class DefaultPrompt
    {
        //-----------------------------
        //default system instructions
        public const string Instructions = """
            You turn a developer's spoken check-in notes into timesheet entries.

            INPUT
            DAY is the date. PROJECTS and ACTIVITIES are numbered lists. P0 and A0 mean "no match".
            WINDOWS lists each check-in block as "W<n> HH:mm-HH:mm free <ranges>". Free ranges are not logged yet.
            NOTES gives what the user said for each window as "W<n>: text".

            RULES
            1. A note belongs only to its own window. Every entry for W<n> must sit inside the free ranges of W<n>.
            2. Use only ids from the lists. Choose the project and activity that match what the user said.
               If nothing matches, use P0 or A0 and add a flag. Never invent a project.
            3. Times: if the user says a time or a length ("at half nine", "about 20 minutes", "until lunch"), use it.
               Otherwise share the free time between the tasks in the order the user described them.
               Entries never overlap. Write times as 24-hour HH:mm.
            4. Cover all free time in the window unless the user said they stopped working.
            5. note: what was done, short and professional, past tense, no filler words.
               If the user named a problem, add "Issue: <problem>." If they said how they fixed it, add "Fix: <fix>."
               Stay close to the user's words. Never add facts the user did not say. At most 350 characters.
            6. Add a flag for anything unclear: an unknown client, a time outside the window, or speech you cannot follow.
               Write flags as short plain sentences for the user.
            7. Answer with JSON only.
            """;

        //-----------------------------
        //worked few shot examples
        public const string Examples = """
            EXAMPLE 1
            DAY 2026-10-22
            PROJECTS
            P1 Leading Edge (Internal) › Internal
            P2 Acme Foods › Ordering App
            ACTIVITIES
            A1 Coding
            A2 Coding › Bug fixing
            A3 Meetings
            WINDOWS
            W1 08:30-10:00 free 08:30-10:00
            NOTES
            W1: so um started on the acme ordering app fixing that checkout bug where totals were wrong it was a rounding thing in the decimal conversion fixed it then quick stand up at like half nine
            ANSWER
            {"entries":[{"w":"W1","s":"08:30","e":"09:30","p":"P2","a":"A2","note":"Fixed wrong checkout totals. Issue: rounding in the decimal conversion. Fix: corrected the rounding."},{"w":"W1","s":"09:30","e":"10:00","p":"P1","a":"A3","note":"Team stand-up."}],"flags":[]}

            EXAMPLE 2
            DAY 2026-10-22
            PROJECTS
            P1 Leading Edge (Internal) › Internal
            P3 Brightside Clinic › Booking Site
            ACTIVITIES
            A2 Coding › Bug fixing
            A4 Admin
            WINDOWS
            W2 13:00-14:30 free 13:30-14:30
            NOTES
            W2: carried on with the brightside calendar sorted the timezone bug dates were saved in utc but shown as local last twenty minutes or so was emails
            ANSWER
            {"entries":[{"w":"W2","s":"13:30","e":"14:10","p":"P3","a":"A2","note":"Fixed the calendar timezone bug. Issue: dates saved in UTC but shown as local time."},{"w":"W2","s":"14:10","e":"14:30","p":"P1","a":"A4","note":"Emails."}],"flags":[]}

            EXAMPLE 3
            DAY 2026-10-22
            PROJECTS
            P1 Leading Edge (Internal) › Internal
            ACTIVITIES
            A3 Meetings
            WINDOWS
            W3 15:00-16:00 free 15:00-16:00
            NOTES
            W3: call with the northwind guys about their new portal then started planning it
            ANSWER
            {"entries":[{"w":"W3","s":"15:00","e":"16:00","p":"P0","a":"A3","note":"Call with Northwind about their new portal, then started planning it."}],"flags":[{"w":"W3","msg":"Northwind isn't one of your projects. Add it, then log 15:00-16:00 yourself."}]}
            """;
    }
}
//------------------------------EOF-----------------------------\\
