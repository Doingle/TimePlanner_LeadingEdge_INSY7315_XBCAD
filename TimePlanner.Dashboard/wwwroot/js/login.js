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
