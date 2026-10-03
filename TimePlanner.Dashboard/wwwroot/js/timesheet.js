//-----------------------------
//My Timesheet's month calendar. A month shows one day's entries at a time: picking a day on the calendar, or a missed day named in the
//summary, shows that day below and takes the person there. The arrow keys move through the month like a calendar: left and right one day,
//up and down one week. Without this file every day stays listed. It lives in a file because the content security policy blocks inline scripts
(function () {
    var calendar = document.querySelector(".ts-cal--month");
    var entries = document.querySelector(".ts-entries");
    if (!calendar || !entries)
        return;

    var tiles = Array.prototype.slice.call(calendar.querySelectorAll("button.ts-tile[data-date]"));
    var sections = Array.prototype.slice.call(entries.querySelectorAll(".ts-day[data-date]"));
    if (tiles.length === 0)
        return;

    //the tile for each day of the month, by its day number
    var byDay = {};
    tiles.forEach(function (tile) {
        byDay[Number(tile.dataset.date.slice(8))] = tile;
    });

    function show(date, focus) {
        sections.forEach(function (section) {
            section.hidden = section.dataset.date !== date;
        });
        tiles.forEach(function (tile) {
            var chosen = tile.dataset.date === date;
            tile.setAttribute("aria-pressed", chosen ? "true" : "false");
            tile.tabIndex = chosen ? 0 : -1;
        });
        if (!focus)
            return;

        //the day's entries are below the calendar, so the person is taken there
        var heading = document.getElementById("day-" + date + "-h");
        if (!heading)
            return;
        heading.tabIndex = -1;
        heading.focus({ preventScroll: true });
        var still = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
        heading.scrollIntoView({ block: "start", behavior: still ? "auto" : "smooth" });
    }

    show(entries.dataset.selected || tiles[tiles.length - 1].dataset.date, false);

    calendar.addEventListener("click", function (e) {
        var tile = e.target.closest("button.ts-tile[data-date]");
        if (tile)
            show(tile.dataset.date, true);
    });

    document.querySelectorAll("[data-select]").forEach(function (link) {
        link.addEventListener("click", function (e) {
            var date = link.dataset.select;
            if (!sections.some(function (section) { return section.dataset.date === date; }))
                return;
            e.preventDefault();
            show(date, true);
        });
    });

    calendar.addEventListener("keydown", function (e) {
        var tile = e.target.closest("button.ts-tile[data-date]");
        if (!tile)
            return;

        var target = null;
        if (e.key === "Home")
            target = tiles[0];
        else if (e.key === "End")
            target = tiles[tiles.length - 1];
        else {
            var step = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 }[e.key];
            if (!step)
                return;
            //walk in that direction until a day that can be opened, or off the edge of the month
            for (var day = Number(tile.dataset.date.slice(8)) + step; day >= 1 && day <= 31 && !target; day += step)
                target = byDay[day] || null;
        }

        e.preventDefault();
        if (!target)
            return;
        tiles.forEach(function (other) {
            other.tabIndex = -1;
        });
        target.tabIndex = 0;
        target.focus();
    });
})();
