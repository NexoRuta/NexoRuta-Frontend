// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

document.querySelectorAll("form[data-single-submit]").forEach((form) => {
    form.addEventListener("submit", (event) => {
        if (form.dataset.submitted === "true") {
            event.preventDefault();
            return;
        }
        form.dataset.submitted = "true";
        const submitter = event.submitter;
        form.querySelectorAll('button[type="submit"]').forEach((button) => { if (button !== submitter) button.disabled = true; });
    });
});
