// Shared progressive enhancements. Native navigation, forms and antiforgery remain intact.
(() => {
    document.addEventListener('click', event => {
        const skip = event.target.closest?.('a.nr-skip-link');
        if (skip) {
            const target = document.querySelector(skip.getAttribute('href'));
            if (target) { event.preventDefault(); event.stopImmediatePropagation(); target.scrollIntoView({ block: 'start' }); target.focus({ preventScroll: true }); }
        }
    }, true);
    const desktop = matchMedia('(min-width: 992px)');
    for (const toggle of document.querySelectorAll('[data-portal-nav-toggle]')) {
        const nav = document.getElementById(toggle.getAttribute('aria-controls'));
        if (!nav) continue;
        const close = restore => { nav.classList.remove('is-open'); toggle.setAttribute('aria-expanded', 'false'); if (restore) toggle.focus(); };
        toggle.addEventListener('click', () => { const open = !nav.classList.contains('is-open'); nav.classList.toggle('is-open', open); toggle.setAttribute('aria-expanded', String(open)); });
        nav.addEventListener('click', e => { if (e.target.closest('a')) close(false); });
        document.addEventListener('keydown', e => { if (e.key === 'Escape' && nav.classList.contains('is-open')) { e.preventDefault(); close(true); } });
        desktop.addEventListener('change', () => close(false));
    }
    desktop.addEventListener('change', () => {
        const toggle = document.querySelector('[data-bs-target="#backofficeNavigation"]');
        const nav = document.getElementById('backofficeNavigation');
        if (toggle && nav && desktop.matches) { window.bootstrap?.Collapse?.getInstance(nav)?.hide(); toggle.setAttribute('aria-expanded', 'false'); }
    });
    document.addEventListener('keydown', e => {
        const nav = document.getElementById('backofficeNavigation');
        const toggle = document.querySelector('[data-bs-target="#backofficeNavigation"]');
        if (e.key === 'Escape' && toggle?.getAttribute('aria-expanded') === 'true') {
            e.preventDefault();
            const close = () => window.bootstrap?.Collapse?.getOrCreateInstance(nav)?.hide();
            if (nav.classList.contains('collapsing')) nav.addEventListener('shown.bs.collapse', close, { once: true }); else close();
            toggle?.focus();
        }
    });
    const decorateTables = () => {
        for (const region of document.querySelectorAll('.table-responsive')) {
            if (!region.querySelector('table')) continue;
            region.setAttribute('role', 'region');
            if (!region.hasAttribute('aria-label') && !region.hasAttribute('aria-labelledby')) {
                const caption = region.querySelector('caption')?.textContent.trim();
                region.setAttribute('aria-label', caption || 'Tabla de ' + (document.querySelector('h1')?.textContent.trim() || 'resultados'));
            }
            region.tabIndex = region.scrollWidth > region.clientWidth ? 0 : -1;
        }
    };
    decorateTables();
    new MutationObserver(decorateTables).observe(document.body, { childList: true, subtree: true });
    addEventListener('resize', decorateTables);
    document.addEventListener('keydown', e => {
        const disclosure = e.target.closest?.('details.nr-disclosure[open]');
        if (e.key === 'Escape' && disclosure) { disclosure.open = false; disclosure.querySelector('summary')?.focus(); }
    });
    document.addEventListener('submit', e => {
        if (e.defaultPrevented || !e.target.matches('form[data-single-submit]')) return;
        e.target.setAttribute('aria-busy', 'true');
        const status = document.createElement('span'); status.className = 'visually-hidden'; status.role = 'status'; status.textContent = 'Procesando la solicitud…';
        e.target.append(status);
    });
})();
