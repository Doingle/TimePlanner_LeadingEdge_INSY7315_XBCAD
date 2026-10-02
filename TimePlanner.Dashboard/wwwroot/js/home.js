//-----------------------------
//lays out the home page. The content security policy blocks inline styles, so the page writes times and percentages as data
//attributes and this file turns them into positions and widths. Colours (data-colour) are applied by app.js
(function () {
    //minutes since midnight for an "HH:mm" value, NaN for anything else
    function toMinutes(value) {
        var match = /^(\d{1,2}):(\d{2})$/.exec(value || "");
        return match ? Number(match[1]) * 60 + Number(match[2]) : NaN;
    }

    function pad(number) {
        return (number < 10 ? "0" : "") + number;
    }

    //bars are as wide as their share, data-percent="55"
    document.querySelectorAll(".home-bar__fill[data-percent]").forEach(function (fill) {
        var percent = parseFloat(fill.dataset.percent);
        fill.style.width = Math.min(100, Math.max(0, percent || 0)) + "%";
    });

    var track = document.querySelector(".home-track");
    if (!track)
        return;

    var dayStart = toMinutes(track.dataset.dayStart);
    var dayEnd = toMinutes(track.dataset.dayEnd);
    if (!(dayEnd > dayStart))
        return;

    //how far along the working day a time is, as a percentage of the track
    function position(minutes) {
        return Math.min(100, Math.max(0, (minutes - dayStart) / (dayEnd - dayStart) * 100));
    }

    //blocks span data-start to data-end, anything outside the working day is cut off at the edge
    track.querySelectorAll(".home-block").forEach(function (block) {
        var left = position(toMinutes(block.dataset.start));
        var right = position(toMinutes(block.dataset.end));
        if (isNaN(left) || isNaN(right) || right <= left) {
            block.hidden = true;
            return;
        }
        block.style.left = left + "%";
        block.style.width = right - left + "%";
    });

    //the now marker follows this device's clock and is hidden outside the working day
    var now = track.querySelector(".home-now");
    var label = now && now.querySelector(".home-now__label");
    if (!now || !label)
        return;

    function placeNow() {
        var date = new Date();
        var minutes = date.getHours() * 60 + date.getMinutes();
        now.hidden = minutes < dayStart || minutes > dayEnd;
        now.style.left = position(minutes) + "%";
        label.textContent = "Now " + pad(date.getHours()) + ":" + pad(date.getMinutes());
    }

    placeNow();
    setInterval(placeNow, 30000);
})();
