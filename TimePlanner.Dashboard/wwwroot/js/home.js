//-----------------------------
//lays out the day ribbon in the home page's hero. The content security policy blocks inline styles, so the page writes times as data attributes
//and this file turns them into positions. They are set as custom properties (--left, --width, --at) so a phone's list layout can ignore them.
//Bar widths (data-percent) and colours (data-colour) are applied by app.js
(function () {
    //minutes since midnight for an "HH:mm" value, NaN for anything else
    function toMinutes(value) {
        var match = /^(\d{1,2}):(\d{2})$/.exec(value || "");
        return match ? Number(match[1]) * 60 + Number(match[2]) : NaN;
    }

    var ribbon = document.querySelector(".home-ribbon");
    if (!ribbon)
        return;

    var dayStart = toMinutes(ribbon.dataset.dayStart);
    var dayEnd = toMinutes(ribbon.dataset.dayEnd);
    if (!(dayEnd > dayStart))
        return;

    //how far along the working day a time is, as a percentage of the ribbon
    function position(minutes) {
        return Math.min(100, Math.max(0, (minutes - dayStart) / (dayEnd - dayStart) * 100));
    }

    //blocks span data-start to data-end, anything outside the working day is cut off at the edge
    ribbon.querySelectorAll(".home-block").forEach(function (block, index) {
        var left = position(toMinutes(block.dataset.start));
        var right = position(toMinutes(block.dataset.end));
        if (isNaN(left) || isNaN(right) || right <= left) {
            block.hidden = true;
            return;
        }
        block.style.setProperty("--left", left + "%");
        block.style.setProperty("--width", right - left + "%");
        //a block in the second half of the day opens its tooltip to the left, so it stays on screen
        block.classList.toggle("home-block--late", left > 50);

        //the day fills in from left to right, one block after the other
        var bar = block.querySelector(".home-block__bar");
        if (bar)
            bar.style.animationDelay = index * 70 + "ms";
    });

    ribbon.querySelectorAll(".home-axis__tick").forEach(function (tick) {
        tick.style.setProperty("--at", position(toMinutes(tick.dataset.at)) + "%");
    });
})();
