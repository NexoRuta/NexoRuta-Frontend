export function cerrarMenus(root, except = null, restoreFocus = false) {
    root.querySelectorAll('details[data-shipment-menu][open]').forEach(menu => {
        if (menu === except) return;
        menu.open = false;
        if (restoreFocus) menu.querySelector('summary')?.focus();
    });
}

export function prepararActualizacion(form, page) {
    const button = form.querySelector('button[type="submit"]');
    if (!button) return;
    const label = button.textContent;
    page.addEventListener('pageshow', () => {
        delete form.dataset.submitted;
        form.removeAttribute('aria-busy');
        button.disabled = false;
        button.textContent = label;
    });
}

if (typeof document !== 'undefined') {
    document.querySelectorAll('[data-operator-refresh]').forEach(form => prepararActualizacion(form, window));
    document.addEventListener('click', event => {
        // The generated PDF anchor is not a user click outside the menu.
        if (event.target.closest?.('a[download][href^="blob:"]')) return;
        const menu = event.target.closest?.('details[data-shipment-menu]');
        cerrarMenus(document, menu);
        if (menu && event.target.closest?.('[data-copy-shipment-id],[data-bs-toggle="offcanvas"]')) {
            menu.open = false;
            if (!event.target.closest('[data-bs-toggle="offcanvas"]')) menu.querySelector('summary')?.focus();
        }
    });
    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape' || !document.querySelector('details[data-shipment-menu][open]')) return;
        event.preventDefault();
        cerrarMenus(document, null, true);
    });
}
