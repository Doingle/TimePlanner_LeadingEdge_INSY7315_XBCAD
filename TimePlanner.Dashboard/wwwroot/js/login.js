//-----------------------------
//the eye button on the login page shows or hides the password. It lives in a file because the content security policy blocks inline scripts
(function () {
    var toggle = document.getElementById("togglePassword");
    var input = document.getElementById("Password");
    if (!toggle || !input)
        return;

    toggle.addEventListener("click", function () {
        var show = input.type === "password";
        input.type = show ? "text" : "password";
        toggle.setAttribute("aria-pressed", show);
        toggle.setAttribute("aria-label", show ? "Hide password" : "Show password");
    });
})();

//-----------------------------
//while the form is on its way the button reads "Logging in…", and a second press does not send it again
(function () {
    var form = document.querySelector(".login-form");
    var submit = form && form.querySelector(".login-submit");
    if (!submit)
        return;

    form.addEventListener("submit", function (e) {
        if (submit.getAttribute("aria-disabled") === "true") {
            e.preventDefault();
            return;
        }
        submit.setAttribute("aria-disabled", "true");
    });

    //coming back to the page with the back button shows it as it was left, so the button is reset
    window.addEventListener("pageshow", function (e) {
        if (e.persisted)
            submit.removeAttribute("aria-disabled");
    });
})();
