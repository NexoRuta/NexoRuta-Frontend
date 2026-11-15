(() => {
    "use strict";
    const filters = ["Todas", "Disponible", "Ocupado", "Pendiente", "NoHabilitado"];
    const normalizedFilter = value => filters.find(x => x.toLowerCase() === String(value || "").toLowerCase()) || "Todas";
    const matchesVehicle = (name, status, search, filter, active = true) =>
        String(name).toLocaleLowerCase().includes(String(search || "").trim().toLocaleLowerCase()) &&
        (filter === "Todas" || filter === "NoHabilitado" ? filter === "Todas" || !active : filter === status);
    if (typeof module !== "undefined" && module.exports) module.exports = { matchesVehicle, normalizedFilter };
    if (typeof document === "undefined") return;
    const initialize = () => {
        const root = document.querySelector("[data-fleet-root]");
        if (!root) return;
        const form = root.querySelector("[data-fleet-filter]");
        const search = form?.querySelector("[name=buscarVehiculo]");
        const filter = form?.querySelector("[name=disponibilidad]");
        const cards = [...root.querySelectorAll("[data-fleet-card]")];
        const refresh = root.querySelector("[data-fleet-refresh]");
        const apply = (writeUrl = true) => {
            if (!search || !filter) return;
            let visible = 0;
            for (const card of cards) {
                card.hidden = !matchesVehicle(card.dataset.name, card.dataset.status, search.value, filter.value, card.dataset.active === "true");
                if (!card.hidden) visible++;
            }
            root.querySelector("[data-fleet-results]").textContent = `${visible} de ${cards.length} vehículos`;
            root.querySelector("[data-fleet-empty]").hidden = visible > 0;
            const url = new URL(location.href);
            url.searchParams.delete("handler");
            search.value ? url.searchParams.set("buscarVehiculo", search.value) : url.searchParams.delete("buscarVehiculo");
            filter.value !== "Todas" ? url.searchParams.set("disponibilidad", filter.value) : url.searchParams.delete("disponibilidad");
            if (writeUrl) history.replaceState(null, "", url);
            if (refresh) refresh.href = url.pathname + url.search;
        };
        form?.addEventListener("submit", event => { event.preventDefault(); apply(); });
        search?.addEventListener("input", () => apply());
        filter?.addEventListener("change", () => apply());
        window.addEventListener("popstate", () => {
            if (!search || !filter) return;
            const query = new URL(location.href).searchParams;
            search.value = query.get("buscarVehiculo") || "";
            filter.value = normalizedFilter(query.get("disponibilidad"));
            apply(false);
        });
        refresh?.addEventListener("click", () => {
            refresh.setAttribute("aria-busy", "true");
            refresh.textContent = "Actualizando…";
        });
        window.addEventListener("pageshow", () => {
            refresh?.removeAttribute("aria-busy");
            if (refresh) refresh.textContent = "Actualizar";
        });
        apply(false);
        const panel = document.getElementById("flota-operacion");
        if (!panel || !window.bootstrap?.Offcanvas) return;
        // The panel must not inherit the app shell's inert state.
        document.body.append(panel);
        const body = panel.querySelector("[data-fleet-panel-body]");
        const title = panel.querySelector("#fleet-panel-title");
        const shell = document.getElementById("backoffice-app-shell");
        let opener = null, actionOpener = null;
        const chooseAction = (id, trigger) => {
            const template = document.getElementById(id);
            const slot = body.querySelector("[data-fleet-action-slot]");
            if (!template || !slot) return;
            actionOpener = trigger;
            slot.replaceChildren(template.content.cloneNode(true));
            slot.querySelector('[name=BuscarVehiculo]').value = search?.value || "";
            slot.querySelector('[name=Disponibilidad]').value = filter?.value || "Todas";
            if (typeof initializeSingleSubmitForms === "function") initializeSingleSubmitForms(slot);
            slot.querySelector("textarea")?.focus();
        };
        const showVehicle = trigger => {
            const template = document.getElementById(trigger.dataset.fleetTemplate);
            if (!template) return false;
            opener = trigger;
            title.textContent = trigger.dataset.fleetTitle;
            body.replaceChildren(template.content.cloneNode(true));
            // Switching cards replaces a possibly focused action form. Keep focus
            // in the open panel immediately, including during reduced-motion reopen.
            if (panel.classList.contains("show") || panel.classList.contains("showing")) {
                panel.focus({ preventScroll: true });
                title.focus({ preventScroll: true });
            }
            if (trigger.dataset.fleetInitialAction) chooseAction(trigger.dataset.fleetInitialAction, body.querySelector(`[data-fleet-action-template="${trigger.dataset.fleetInitialAction}"]`) || title);
            return true;
        };
        root.addEventListener("click", event => {
            const trigger = event.target.closest("[data-fleet-template]");
            if (!trigger) return;
            event.preventDefault(); event.stopPropagation();
            if (panel.classList.contains("show") || panel.classList.contains("showing")) showVehicle(trigger);
            else window.bootstrap.Offcanvas.getOrCreateInstance(panel).show(trigger);
        });
        panel.addEventListener("show.bs.offcanvas", event => {
            if (event.relatedTarget?.dataset.fleetTemplate && !showVehicle(event.relatedTarget)) {
                event.preventDefault(); return;
            }
            if (shell) shell.inert = true;
        });
        panel.addEventListener("shown.bs.offcanvas", () => {
            // A rapidly reopened panel can paint before its heading inherits
            // visibility. Keep a focusable owner for Escape even in that frame.
            panel.focus({ preventScroll: true });
            (body.querySelector("textarea") || title).focus({ preventScroll: true });
            // A reduced-motion reopen can finish before descendants inherit visibility.
            // Restore focus on the next rendered frame, unless the panel closed meanwhile.
            requestAnimationFrame(() => {
                if (panel.classList.contains("show") && !panel.classList.contains("hiding"))
                    (body.querySelector("textarea") || title).focus({ preventScroll: true });
            });
        });
        panel.addEventListener("hidden.bs.offcanvas", () => {
            if (shell) shell.inert = false;
            if (opener?.isConnected) opener.focus({ preventScroll: true });
        });
        initializeOffcanvasKeyboard(panel, title);
        panel.addEventListener("click", async event => {
            const action = event.target.closest("[data-fleet-action-template]");
            if (action) { chooseAction(action.dataset.fleetActionTemplate, action); return; }
            if (event.target.closest("[data-fleet-cancel-action]")) {
                body.querySelector("[data-fleet-action-slot]")?.replaceChildren();
                actionOpener?.focus();
                return;
            }
            const copy = event.target.closest("[data-fleet-copy]");
            if (copy) {
                const status = body.querySelector("[data-fleet-copy-status]");
                try { await navigator.clipboard.writeText(copy.dataset.fleetCopy); status.textContent = "Identificador copiado."; }
                catch { status.textContent = "No se pudo copiar. Seleccioná el identificador para copiarlo manualmente."; }
            }
        });
        if (panel.dataset.fleetRetry === "true") {
            window.bootstrap.Offcanvas.getOrCreateInstance(panel).show();
        }
    };
    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", initialize, { once: true });
    else initialize();
})();
