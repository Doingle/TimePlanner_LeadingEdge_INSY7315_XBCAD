//-----------------------------
//uploading a timesheet. In the dialog on My Reports: open it from the Upload CSV link, show the chosen file, explain a file that will not be accepted
//before it is sent, send the form to the /CsvUpload handler with fetch and show the result block from its answer. On the /CsvUpload page itself
//only the file checks run and the form posts as usual. Without this file the link opens that page, so nothing depends on it.
//it lives in a file because the content security policy blocks inline scripts
(function () {
    //the importer's own limit (TimesheetImportService.MaxFileBytes)
    var maxBytes = 1000000;

    document.querySelectorAll("[data-upload-form]").forEach(function (form) {
        var dialog = form.closest("dialog");
        var input = form.querySelector("[data-upload-file]");
        var drop = form.querySelector("[data-upload-drop]");
        var state;

        function find(selector) {
            return form.querySelector(selector);
        }

        function all(selector, each) {
            form.querySelectorAll(selector).forEach(each);
        }

        function fresh() {
            state = { step: "pick", file: null, error: "", net: "", kind: "", imported: false };
        }

        function size(bytes) {
            return bytes < 1000000 ? Math.max(1, Math.round(bytes / 1000)) + " KB" : (bytes / 1000000).toFixed(1) + " MB";
        }

        function check(file) {
            if (!/\.csv$/i.test(file.name))
                return "“" + file.name + "” isn’t a CSV file. Choose the .csv file the widget exported.";
            if (file.size > maxBytes)
                return "“" + file.name + "” is " + size(file.size) + ", and the limit is 1 MB. Split it into smaller files and upload them one at a time.";
            return "";
        }

        //the picker's face: empty, a good file, or a file that will not be accepted
        function renderPick() {
            var chosen = !!state.file;
            drop.classList.toggle("up-drop--chosen", chosen && !state.error);
            drop.classList.toggle("up-drop--error", chosen && !!state.error);
            drop.classList.toggle("up-drop--busy", state.step === "busy");
            all("[data-upload-icon]", function (icon) {
                var want = !chosen ? "pick" : state.error ? "bad" : "ok";
                icon.toggleAttribute("hidden", icon.dataset.uploadIcon !== want);
            });
            find("[data-upload-empty]").hidden = chosen;
            find("[data-upload-chosen]").hidden = !chosen;
            find("[data-upload-another]").hidden = !chosen || state.step === "busy";
            if (chosen) {
                find("[data-upload-name]").textContent = state.file.name;
                find("[data-upload-size]").textContent = size(state.file.size);
            }
            input.setAttribute("aria-invalid", state.error ? "true" : "false");
            find("[data-upload-error]").hidden = !state.error;
            find("[data-upload-error-text]").textContent = state.error;
            find("[data-upload-net]").hidden = !state.net;
            find("[data-upload-net-text]").textContent = state.net;
        }

        //the dialog's step and the buttons that go with it
        function render() {
            renderPick();
            if (!dialog)
                return;
            var foot = find("[data-upload-foot]");
            var buttons = state.step === "result" ? (state.kind === "fail" ? "fail" : "done") : state.step;
            foot.dataset.step = state.step;
            all("[data-upload-for]", function (button) {
                button.hidden = button.dataset.uploadFor.split(" ").indexOf(buttons) < 0;
            });
            var submit = find("[data-upload-submit]");
            var busy = state.step === "busy";
            if (busy)
                submit.setAttribute("aria-disabled", "true");
            else
                submit.removeAttribute("aria-disabled");
            all("[data-upload-busy]", function (part) { part.toggleAttribute("hidden", !busy); });
            all("[data-upload-idle]", function (part) { part.toggleAttribute("hidden", busy); });
            input.tabIndex = busy ? -1 : 0;
            find("[data-upload-step='pick']").hidden = state.step === "result";
            find("[data-upload-step='result']").hidden = state.step !== "result";
            find("[data-upload-live]").textContent = busy ? "Uploading " + state.file.name : "";
        }

        input.addEventListener("change", function () {
            var file = input.files[0];
            state.net = "";
            state.file = file ? { name: file.name, size: file.size } : null;
            state.error = file ? check(file) : "";
            render();
        });
        ["dragenter", "dragover"].forEach(function (type) {
            drop.addEventListener(type, function () { drop.classList.add("up-drop--over"); });
        });
        ["dragleave", "drop"].forEach(function (type) {
            drop.addEventListener(type, function () { drop.classList.remove("up-drop--over"); });
        });

        form.addEventListener("submit", function (event) {
            if (state.step === "busy") {
                event.preventDefault();
                return;
            }
            if (!state.file || state.error) {
                event.preventDefault();
                if (!state.file)
                    state.error = "Choose a CSV file to upload.";
                render();
                input.focus();
                return;
            }
            //the page posts as usual, only the dialog stays where it is
            if (!dialog)
                return;
            event.preventDefault();
            state.step = "busy";
            state.net = "";
            render();
            find("[data-upload-submit]").focus();

            fetch(form.action, { method: "POST", body: new FormData(form), credentials: "same-origin" })
                .then(function (response) {
                    if (response.status === 429)
                        throw "Too many uploads in a short time, so nothing was imported. Wait a minute, then select Upload again.";
                    if (response.redirected && response.url.indexOf("/Account/Login") >= 0)
                        throw "Your sign in has ended, so nothing was imported. Sign in again, then upload the file.";
                    if (!response.ok)
                        throw "";
                    return response.text();
                })
                .then(function (html) {
                    //the handler answers with the /CsvUpload page, its result block is the same one the page shows
                    var result = new DOMParser().parseFromString(html, "text/html").querySelector("[data-upload-result]");
                    if (!result)
                        throw "";
                    showResult(document.importNode(result, true));
                })
                .catch(function (message) {
                    state.step = "pick";
                    state.net = typeof message === "string" && message
                        ? message
                        : "The upload didn’t go through, so nothing was imported. Check your connection, then select Upload again.";
                    render();
                    find("[data-upload-submit]").focus();
                });
        });

        function showResult(result) {
            state.step = "result";
            state.kind = result.dataset.kind;
            state.imported = state.imported || result.dataset.imported === "true";
            find("[data-upload-step='result']").replaceChildren(result);
            find("[data-upload-again='pick']").textContent = result.dataset.again || "Choose another file";
            render();
            find("[data-upload-focus]").focus();
        }

        fresh();
        render();
        if (!dialog)
            return;

        var opener = null;
        var finished = true;

        //after the dialog closes: the page is drawn again if entries were added, otherwise focus goes back to the link.
        //the close buttons call this straight away, and so does the close event (Esc). Whichever comes first wins, and a late close event
        //from an earlier opening, arriving while the dialog is open again, is ignored
        function finish() {
            if (finished || dialog.open)
                return;
            finished = true;
            if (state.imported)
                location.reload();
            else if (opener)
                opener.focus();
        }

        function reset() {
            var imported = state && state.imported;
            fresh();
            state.imported = imported;
            form.reset();
            find("[data-upload-step='result']").replaceChildren();
            render();
        }

        document.querySelectorAll("[data-upload-open]").forEach(function (link) {
            link.addEventListener("click", function (event) {
                if (typeof dialog.showModal !== "function")
                    return;
                event.preventDefault();
                opener = link;
                reset();
                state.imported = false;
                finished = false;
                dialog.showModal();
                input.focus();
            });
        });

        dialog.addEventListener("click", function (event) {
            if (event.target.closest("[data-upload-close]")) {
                dialog.close();
                finish();
                return;
            }
            var again = event.target.closest("[data-upload-again]");
            if (again) {
                reset();
                input.focus();
                //straight to the file picker after a file with problems
                if (again.dataset.uploadAgain === "pick")
                    input.click();
            }
        });

        //Esc does nothing while the file is on its way, the answer would be lost
        dialog.addEventListener("cancel", function (event) {
            if (state.step === "busy")
                event.preventDefault();
        });

        //closed by Esc (the buttons have already finished)
        dialog.addEventListener("close", finish);
    });
})();
