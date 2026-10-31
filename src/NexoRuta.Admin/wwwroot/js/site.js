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
        if (submitter instanceof HTMLButtonElement && submitter.dataset.loadingText) {
            submitter.disabled = true;
            submitter.setAttribute("aria-busy", "true");
            submitter.textContent = submitter.dataset.loadingText;
        }
    });
});

document.addEventListener("click", (event) => {
    const toggle = event.target.closest("[data-password-toggle]");
    if (!toggle) return;
    const input = document.getElementById(toggle.getAttribute("aria-controls"));
    if (!input) return;
    const visible = input.type === "password";
    input.type = visible ? "text" : "password";
    toggle.textContent = visible ? "Ocultar" : "Mostrar";
    toggle.setAttribute("aria-label", `${visible ? "Ocultar" : "Mostrar"} contraseña`);
    toggle.setAttribute("aria-pressed", String(visible));
});
