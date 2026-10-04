//-----------------------------
//shared behaviour for the pages in the signed in layout. It lives in a file because the content security policy blocks inline scripts
(function () {
    //inline styles are blocked too, so anything coloured by the data (a category dot, a timeline block, a bar) carries it as data-colour="#0E7490"
    document.querySelectorAll("[data-colour]").forEach(function (element) {
        element.style.backgroundColor = element.dataset.colour;
    });

    //and anything sized by the data (a bar, part of a split bar) carries its width as data-percent="55"
    document.querySelectorAll("[data-percent]").forEach(function (element) {
        var percent = parseFloat(element.dataset.percent);
        element.style.width = Math.min(100, Math.max(0, percent || 0)) + "%";
    });

    //the account menu in the top bar closes when the user clicks elsewhere or presses Escape
    var menu = document.querySelector(".app-user");
    if (!menu)
        return;

    document.addEventListener("click", function (e) {
        if (menu.open && !menu.contains(e.target))
            menu.open = false;
    });

    document.addEventListener("keydown", function (e) {
        if (e.key !== "Escape" || !menu.open)
            return;
        menu.open = false;
        menu.querySelector("summary").focus();
    });
})();
