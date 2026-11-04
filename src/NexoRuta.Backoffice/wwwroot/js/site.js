// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

const initializeSingleSubmitForms = (root) => {
    root.querySelectorAll("form[data-single-submit]").forEach((form) => {
        form.addEventListener("submit", (event) => {
            if (form.dataset.submitted === "true") {
                event.preventDefault();
                return;
            }
            form.dataset.submitted = "true";
            form.querySelectorAll('button[type="submit"]').forEach((button) => { button.disabled = true; });
            const loadingButton = form.querySelector('button[type="submit"][data-loading-text]');
            if (loadingButton) {
                loadingButton.textContent = loadingButton.dataset.loadingText;
                form.setAttribute("aria-busy", "true");
            }
        });
    });
};
// Keep native disclosure children out of Tab boundaries; Bootstrap still owns the modal lifecycle.
const initializeOffcanvasKeyboard = (panel, initialFocus) => {
    if (panel.dataset.keyboardBoundary === "true") return;
    panel.dataset.keyboardBoundary = "true";
    panel.addEventListener("keydown", event => {
        if (event.key !== "Tab") return;
        const stops = [...panel.querySelectorAll('a[href], button, input:not([type=hidden]), textarea, select, summary, [tabindex="0"]')]
            .filter(el => el.tabIndex >= 0 && !el.matches(":disabled") && el.checkVisibility({ checkVisibilityCSS: true }));
        const first = stops[0], last = stops.at(-1);
        if (!first) { event.preventDefault(); panel.focus(); }
        else if (event.shiftKey && (document.activeElement === first || document.activeElement === initialFocus || document.activeElement === panel)) {
            event.preventDefault(); last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault(); first.focus();
        }
    });
};
initializeSingleSubmitForms(document);

document.querySelectorAll("form[data-preserve-scroll]").forEach((form) => {
    form.addEventListener("submit", () => sessionStorage.setItem("nr-shipments-scroll-y", String(window.scrollY)));
});

window.addEventListener("pageshow", () => {
    const scrollY = sessionStorage.getItem("nr-shipments-scroll-y");
    if (scrollY === null) return;
    sessionStorage.removeItem("nr-shipments-scroll-y");
    requestAnimationFrame(() => window.scrollTo(0, Number(scrollY)));
});

document.addEventListener("click", async (event) => {
    const button = event.target.closest?.("[data-copy-shipment-id]");
    if (!button) return;
    const status = document.getElementById(button.dataset.copyStatus);
    try {
        await navigator.clipboard.writeText(button.dataset.copyShipmentId);
        if (status) status.textContent = "Identificador copiado";
    } catch {
        if (status) status.textContent = button.closest("#shipment-detail-dialog")
            ? "No se pudo copiar. Consultá y seleccioná el identificador en Información técnica."
            : "No se pudo copiar. Abrí el detalle para seleccionar el identificador completo.";
    }
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

const shipmentDetailDialog = document.getElementById("shipment-detail-dialog");
if (shipmentDetailDialog && window.bootstrap?.Offcanvas) {
    const detailBody = document.getElementById("shipment-detail-body");
    const detailTitle = document.getElementById("shipment-detail-title");
    initializeOffcanvasKeyboard(shipmentDetailDialog, detailTitle);
    const detailShortId = document.getElementById("shipment-detail-short-id");
    const detailStatus = document.getElementById("shipment-detail-status");
    const copyButton = document.getElementById("shipment-detail-copy");
    const copyStatus = document.getElementById("shipment-detail-copy-status");
    const appShell = document.getElementById("backoffice-app-shell");
    let detailOpener = null;
    const hasOperatorList = !!document.querySelector(".nr-operator-shipments");
    let historyNavigation = false;
    let openingInitialSelection = false;
    const updateSelection = (id) => {
        if (!hasOperatorList || historyNavigation) return;
        const url = new URL(window.location.href);
        if (id) url.searchParams.set("Seleccion", id);
        else url.searchParams.delete("Seleccion");
        url.searchParams.delete("handler");
        document.querySelectorAll('[data-operator-refresh] [name="Seleccion"]').forEach(input => { input.value = id || ""; });
        if (url.href === window.location.href) return;
        window.history[openingInitialSelection ? "replaceState" : "pushState"]({}, "", url);
    };
    if (hasOperatorList) window.addEventListener("popstate", () => {
        // Native SSR keeps selection and filters authoritative; no parallel detail requests can race.
        historyNavigation = true;
        window.location.reload();
    });

    shipmentDetailDialog.addEventListener("show.bs.offcanvas", (event) => {
        const trigger = event.relatedTarget;
        const template = trigger?.dataset.shipmentTemplate
            ? document.getElementById(trigger.dataset.shipmentTemplate)
            : null;
        if (!trigger || !template || !detailBody || !detailShortId || !detailStatus || !copyButton) {
            event.preventDefault();
            return;
        }

        detailOpener = trigger;
        updateSelection(trigger.dataset.shipmentId);
        detailBody.replaceChildren(template.content.cloneNode(true));
        initializeSingleSubmitForms(detailBody);
        detailShortId.textContent = trigger.dataset.shipmentShortId || "";
        copyButton.dataset.copyShipmentId = trigger.dataset.shipmentId || "";
        copyButton.setAttribute("aria-label", `Copiar identificador completo del envío ${detailShortId.textContent}`);
        if (copyStatus) copyStatus.textContent = "";

        const stateClass = ["success", "warning", "danger", "info"].includes(trigger.dataset.shipmentStatusClass)
            ? trigger.dataset.shipmentStatusClass
            : "info";
        const icon = document.createElement("span");
        icon.setAttribute("aria-hidden", "true");
        icon.textContent = ({ success: "✓", warning: "!", danger: "×", info: "•" })[stateClass];
        const stateText = document.createElement("span");
        stateText.textContent = trigger.dataset.shipmentStatusLabel || "Estado no informado";
        detailStatus.className = `nr-shipment-status nr-shipment-status--${stateClass}`;
        detailStatus.replaceChildren(icon, stateText);

        if (appShell) appShell.inert = true;
    });

    shipmentDetailDialog.addEventListener("shown.bs.offcanvas", () => {
        const cancellation = detailOpener?.dataset.focusCancel === "true" ? detailBody.querySelector(".nr-cancel-shipment") : null;
        if (cancellation) {
            cancellation.open = true;
            cancellation.querySelector("textarea")?.focus({ preventScroll: true });
        } else detailTitle?.focus({ preventScroll: true });
    });

    shipmentDetailDialog.addEventListener("hidden.bs.offcanvas", () => {
        if (appShell) appShell.inert = false;
        updateSelection(null);
        if (detailOpener?.isConnected && !detailOpener.hidden) detailOpener.focus({ preventScroll: true });
        else document.getElementById("shipment-search")?.focus({ preventScroll: true });
        detailOpener = null;
    });

    const failedCancellationTrigger = document.querySelector('[data-open-on-load="true"]');
    if (failedCancellationTrigger) {
        openingInitialSelection = true;
        window.bootstrap.Offcanvas.getOrCreateInstance(shipmentDetailDialog).show(failedCancellationTrigger);
        openingInitialSelection = false;
    }
}

const backofficeNavigationToggle = document.querySelector(".nr-menu-toggle");
const backofficeNavigation = backofficeNavigationToggle
    ? document.getElementById(backofficeNavigationToggle.getAttribute("aria-controls"))
    : null;

if (backofficeNavigationToggle && backofficeNavigation && window.bootstrap?.Collapse) {
    const collapse = window.bootstrap.Collapse.getOrCreateInstance(backofficeNavigation, { toggle: false });

    document.addEventListener("keydown", (event) => {
        if (document.getElementById("shipment-detail-dialog")?.classList.contains("show")) return;
        if (event.key !== "Escape" || backofficeNavigationToggle.getAttribute("aria-expanded") !== "true") return;
        collapse.hide();
        backofficeNavigationToggle.focus();
    });
}
