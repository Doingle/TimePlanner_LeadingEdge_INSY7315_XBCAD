//-----------------------------
//the settings page: the eye buttons show or hide a password, the name's character count shows near the limit, and a form's old message
//goes once the person starts changing that form again. It lives in a file because the content security policy blocks inline scripts
(function () {
    document.querySelectorAll("[data-reveal]").forEach(function (button) {
        var input = document.getElementById(button.dataset.reveal);
        if (!input)
            return;
        button.addEventListener("click", function () {
            var show = input.type === "password";
            input.type = show ? "text" : "password";
            button.setAttribute("aria-pressed", show);
            button.setAttribute("aria-label", show ? "Hide password" : "Show password");
        });
    });

    var name = document.getElementById("name");
    var count = document.getElementById("st-name-count");
    if (name && count) {
        var update = function () {
            count.hidden = name.value.length < 80;
            count.textContent = name.value.length + " / 100";
        };
        name.addEventListener("input", update);
        update();
    }

    document.querySelectorAll(".st-form").forEach(function (form) {
        form.addEventListener("input", function () {
            var status = form.querySelector(".st-status");
            if (status)
                status.hidden = true;
        });
    });
})();
