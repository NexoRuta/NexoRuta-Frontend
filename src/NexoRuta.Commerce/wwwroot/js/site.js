document.querySelectorAll("form[data-single-submit]").forEach((form) => {
    form.addEventListener("submit", (event) => {
        if (form.dataset.submitted === "true") {
            event.preventDefault();
            return;
        }
        form.dataset.submitted = "true";
        form.querySelectorAll('button[type="submit"]').forEach((button) => { button.disabled = true; });
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

const commerceNavigationToggle = document.querySelector("[data-commerce-nav-toggle]");
const commerceNavigation = commerceNavigationToggle
    ? document.getElementById(commerceNavigationToggle.getAttribute("aria-controls"))
    : null;

if (commerceNavigationToggle && commerceNavigation) {
    const closeCommerceNavigation = (restoreFocus = false) => {
        commerceNavigationToggle.setAttribute("aria-expanded", "false");
        commerceNavigation.classList.remove("is-open");
        if (restoreFocus) commerceNavigationToggle.focus();
    };

    commerceNavigationToggle.addEventListener("click", () => {
        const open = commerceNavigationToggle.getAttribute("aria-expanded") !== "true";
        commerceNavigationToggle.setAttribute("aria-expanded", String(open));
        commerceNavigation.classList.toggle("is-open", open);
    });

    commerceNavigation.addEventListener("click", (event) => {
        if (event.target.closest("a")) closeCommerceNavigation();
    });

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape" && commerceNavigationToggle.getAttribute("aria-expanded") === "true") {
            closeCommerceNavigation(true);
        }
    });

    window.matchMedia("(min-width: 992px)").addEventListener("change", (event) => {
        if (event.matches) closeCommerceNavigation();
    });
}
