//-----------------------------
//the accessibility page: each change to the text size, bold or italic is applied to this page at once (the same attributes on <html> the layouts write
//from the cookie) and kept in a cookie for a year, so every other page is drawn the same way. Reset goes back to the defaults and forgets the choice.
//the cookie is read by TextPreferences on the server, which only accepts the values written here. It lives in a file because the content security policy blocks inline scripts
(function () {
    var sheet = document.querySelector("[data-text-preferences]");
    if (!sheet)
        return;
    var root = document.documentElement;
    var sizes = sheet.querySelectorAll("input[name='size']");
    var bold = document.getElementById("ax-bold");
    var italic = document.getElementById("ax-italic");
    var status = sheet.querySelector("[data-text-status]");
    var statusIcon = sheet.querySelector("[data-text-status-icon]");
    var names = { "default": "Default", large: "Large", larger: "Larger", largest: "Largest" };

    function current() {
        var size = "default";
        sizes.forEach(function (radio) {
            if (radio.checked)
                size = radio.value;
        });
        return { size: size, bold: bold.checked, italic: italic.checked };
    }

    function show(prefs) {
        sizes.forEach(function (radio) {
            radio.checked = radio.value === prefs.size;
        });
        bold.checked = prefs.bold;
        italic.checked = prefs.italic;
    }

    function apply(prefs) {
        root.setAttribute("data-text-size", prefs.size);
        root.setAttribute("data-bold", prefs.bold ? "on" : "off");
        root.setAttribute("data-italic", prefs.italic ? "on" : "off");
    }

    //"larger.bold.italic": the size, then the switches that are on. The defaults need no cookie at all
    function save(prefs) {
        var value = [prefs.size].concat(prefs.bold ? ["bold"] : [], prefs.italic ? ["italic"] : []).join(".");
        var secure = location.protocol === "https:" ? "; secure" : "";
        document.cookie = value === "default"
            ? "tp_text=; path=/; max-age=0; samesite=lax" + secure
            : "tp_text=" + value + "; path=/; max-age=31536000; samesite=lax" + secure;
    }

    //read out by screen readers, the box is a polite live region
    function announce(text) {
        status.textContent = text ? "Saved. " + text : "";
        statusIcon.toggleAttribute("hidden", !text);
    }

    function change(text) {
        var prefs = current();
        apply(prefs);
        save(prefs);
        announce(text);
    }

    sizes.forEach(function (radio) {
        radio.addEventListener("change", function () {
            change("Text size: " + names[radio.value] + ".");
        });
    });
    bold.addEventListener("change", function () {
        change("Bold text " + (bold.checked ? "on." : "off."));
    });
    italic.addEventListener("change", function () {
        change("Italic text " + (italic.checked ? "on." : "off."));
    });
    sheet.querySelector("[data-text-reset]").addEventListener("click", function () {
        show({ size: "default", bold: false, italic: false });
        change("Back to the default text.");
    });
})();
