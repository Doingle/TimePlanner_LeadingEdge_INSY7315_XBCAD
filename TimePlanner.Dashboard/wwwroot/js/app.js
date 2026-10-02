//-----------------------------
//closes the account menu in the top bar when the user clicks elsewhere or presses Escape. It lives in a file because the content security policy blocks inline scripts
(function () {
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
