//-----------------------------
//the exports page: the line under the picker and the button say what the download will be (a zip for everyone, one csv for a person),
//an old "nothing to export" message goes once another person is picked, and the button shows the file is being prepared.
//the form is a plain get form and downloads the same without this file. It lives in a file because the content security policy blocks inline scripts
(function () {
    var form = document.querySelector("[data-export-form]");
    if (!form)
        return;
    var select = form.querySelector("select[name='userId']");
    var button = form.querySelector("[data-export-button]");
    var name = form.querySelector("[data-export-name]");
    var notice = form.querySelector("[data-export-notice]");
    var busy = false;
    var timer = 0;

    function render() {
        var kind = select.value === "" ? "everyone" : "person";
        name.textContent = select.options[select.selectedIndex].text;
        form.querySelectorAll("[data-export-for]").forEach(function (part) {
            part.hidden = part.dataset.exportFor !== kind || (busy && button.contains(part));
        });
        button.querySelectorAll("[data-export-busy]").forEach(function (part) {
            part.hidden = !busy;
        });
        //the download arrow is an svg, which has no hidden property, so the attribute is set
        button.querySelector("[data-export-icon]").toggleAttribute("hidden", busy);
        if (busy)
            button.setAttribute("aria-disabled", "true");
        else
            button.removeAttribute("aria-disabled");
    }

    select.addEventListener("change", function () {
        if (notice)
            notice.hidden = true;
        render();
    });

    form.addEventListener("submit", function (event) {
        //one download at a time
        if (busy) {
            event.preventDefault();
            return;
        }
        busy = true;
        render();
        //a file download leaves the page where it is, so the button comes back once the browser has had time to start it
        clearTimeout(timer);
        timer = setTimeout(function () {
            busy = false;
            render();
        }, 4000);
    });

    //coming back to the page (or the browser restoring the picked person) starts from what is picked now
    window.addEventListener("pageshow", function () {
        busy = false;
        render();
    });
    render();
})();
