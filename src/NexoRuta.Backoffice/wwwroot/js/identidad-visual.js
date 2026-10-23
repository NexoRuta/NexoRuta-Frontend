(() => {
    const form = document.querySelector("[data-identity-editor]");
    if (!form) return;

    const name = form.querySelector("[data-preview-name-input]");
    const fallbackName = form.dataset.fallbackName || "Nombre del operador";
    const previewName = document.querySelector("[data-preview-name]");
    const previewEmail = document.querySelector("[data-preview-email]");
    const previewPhone = document.querySelector("[data-preview-phone]");
    const logoInput = form.querySelector("[data-logo-input]");
    const logoMessage = form.querySelector("[data-logo-message]");
    const previewLogo = document.querySelector("[data-preview-logo]");
    let selectedLogoUrl = null;

    const rgb = (hex) => {
        if (!/^#[\da-f]{6}$/i.test(hex || "")) return null;
        return [1, 3, 5].map(index => parseInt(hex.slice(index, index + 2), 16) / 255);
    };
    const luminance = (hex) => {
        const components = rgb(hex);
        if (!components) return null;
        const [r, g, b] = components.map(value => value <= .04045 ? value / 12.92 : ((value + .055) / 1.055) ** 2.4);
        return .2126 * r + .7152 * g + .0722 * b;
    };
    const contrastWithWhite = (hex) => {
        const value = luminance(hex);
        return value === null ? 0 : 1.05 / (value + .05);
    };

    if (name) {
        name.addEventListener("input", () => {
            const value = name.value.trim() || fallbackName;
            if (previewName) previewName.textContent = value;
            if (previewLogo) previewLogo.alt = `Logo de ${value}`;
        });
    }

    const bindContact = (id, preview) => {
        const field = form.querySelector(`#${id}`);
        if (!field || !preview) return;
        field.addEventListener("input", () => {
            preview.textContent = field.value.trim();
            preview.hidden = field.value.trim().length === 0;
        });
    };
    bindContact("CorreoPublico", previewEmail);
    bindContact("TelefonoPublico", previewPhone);

    const bindColor = (enabledSelector, colorSelector, warningSelector, chipSelector, accentToken, borderSelector) => {
        const enabled = form.querySelector(enabledSelector);
        const color = form.querySelector(colorSelector);
        const warning = document.querySelector(warningSelector);
        const chip = document.querySelector(chipSelector);
        const border = document.querySelector(borderSelector);
        if (!enabled || !color) return;
        const refresh = () => {
            color.disabled = !enabled.checked;
            const value = color.value.toUpperCase();
            const usable = enabled.checked && contrastWithWhite(value) >= 3;
            const root = document.querySelector(".brand-preview-card");
            if (root) {
                if (usable) root.style.setProperty(accentToken, value);
                else root.style.removeProperty(accentToken);
            }
            if (warning) warning.hidden = !enabled.checked || usable;
            if (chip) {
                chip.hidden = !enabled.checked;
                const swatch = chip.querySelector(".operator-brand-swatch");
                if (swatch) swatch.style.backgroundColor = enabled.checked ? value : "transparent";
            }
            if (border) {
                border.hidden = !enabled.checked;
                if (usable) border.style.backgroundColor = value;
                else border.style.removeProperty("background-color");
            }
        };
        enabled.addEventListener("change", refresh);
        color.addEventListener("input", refresh);
        refresh();
    };
    bindColor("[data-primary-enabled]", "[data-primary-color]", "[data-primary-warning]",
        "[data-preview-primary-chip]", "--nr-brand-primary", ".brand-preview-primary");
    bindColor("[data-secondary-enabled]", "[data-secondary-color]", "[data-secondary-warning]",
        "[data-preview-secondary-chip]", "--nr-brand-secondary", "[data-preview-secondary]");

    const removeLogo = form.querySelector("#QuitarLogo");
    if (removeLogo && previewLogo) {
        removeLogo.addEventListener("change", () => {
            previewLogo.hidden = removeLogo.checked || !previewLogo.getAttribute("src") || previewLogo.getAttribute("src") === "data:,";
        });
    }

    if (logoInput && logoMessage && previewLogo) {
        logoInput.addEventListener("change", async () => {
            const file = logoInput.files?.[0];
            logoMessage.textContent = "";
            if (!file) return;
            if (removeLogo) removeLogo.checked = false;
            if (file.size > 1_048_576 || !["image/png", "image/jpeg"].includes(file.type)) {
                logoMessage.textContent = "La vista previa admite PNG o JPEG de hasta 1 MiB. El servidor valida el contenido al guardar.";
                return;
            }
            try {
                if (typeof createImageBitmap === "function") {
                    const bitmap = await createImageBitmap(file);
                    const valid = bitmap.width <= 2048 && bitmap.height <= 2048 && bitmap.width * bitmap.height <= 4_194_304;
                    bitmap.close();
                    if (!valid) {
                        logoMessage.textContent = "El logo no puede superar 2048 × 2048 píxeles. El servidor vuelve a validar las dimensiones.";
                        return;
                    }
                }
                if (selectedLogoUrl) URL.revokeObjectURL(selectedLogoUrl);
                selectedLogoUrl = URL.createObjectURL(file);
                previewLogo.src = selectedLogoUrl;
                previewLogo.hidden = false;
            } catch {
                logoMessage.textContent = "No se pudo previsualizar el archivo. El servidor validará el contenido al guardar.";
            }
        });
    }
    window.addEventListener("pagehide", () => {
        if (selectedLogoUrl) URL.revokeObjectURL(selectedLogoUrl);
    }, { once: true });
})();
