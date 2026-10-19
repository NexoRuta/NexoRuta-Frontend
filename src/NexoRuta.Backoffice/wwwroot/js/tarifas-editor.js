document.querySelectorAll("form[data-unsaved-form]").forEach((form) => {
    let dirty = form.dataset.unsavedInitial === "true";
    const notice = document.querySelector("[data-unsaved-message]");

    const showUnsavedNotice = () => {
        if (notice) notice.hidden = !dirty;
    };

    form.addEventListener("input", () => {
        dirty = true;
        showUnsavedNotice();
    });
    form.addEventListener("change", () => {
        dirty = true;
        showUnsavedNotice();
    });
    form.addEventListener("submit", (event) => {
        const button = event.submitter;
        if (button?.dataset.confirmRemove === "true" &&
            !window.confirm(button.dataset.confirmMessage || "¿Quitar este elemento de la edición local?")) {
            event.preventDefault();
            return;
        }
        form.dataset.submitting = "true";
    });

    document.querySelectorAll("a[data-requires-clean]").forEach((link) => {
        link.addEventListener("click", (event) => {
            if (!dirty) return;
            event.preventDefault();
            if (notice) {
                notice.hidden = false;
                notice.focus();
            }
        });
    });

    window.addEventListener("beforeunload", (event) => {
        if (!dirty || form.dataset.submitting === "true") return;
        event.preventDefault();
        event.returnValue = "";
    });

    showUnsavedNotice();
});

document.querySelectorAll("[data-adjustment]").forEach((adjustment) => {
    const type = adjustment.querySelector("[data-adjustment-type]");
    const label = adjustment.querySelector("[data-adjustment-label]");
    const help = adjustment.querySelector("[data-adjustment-help]");
    const precision = adjustment.querySelector("[data-adjustment-precision]");
    if (!type || !label || !help || !precision) return;

    const update = () => {
        const option = type.selectedOptions[0];
        if (!option) return;
        label.textContent = option.dataset.adjustmentValueLabel || "Valor";
        help.textContent = option.dataset.adjustmentHelpText || "Configurá el valor del ajuste.";
        precision.textContent = option.dataset.adjustmentPrecisionHint || "";
    };
    type.addEventListener("change", update);
    update();
});
