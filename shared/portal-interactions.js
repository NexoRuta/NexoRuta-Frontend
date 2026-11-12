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

// Native selectors with explicit, bounded search and paging. Keep the chosen value on refresh/error.
for (const group of document.querySelectorAll('[data-nr-selector]')) {
    const select = group.querySelector('select'), search = group.querySelector('input[type=search]');
    const status = group.querySelector('[data-options-status]'), more = group.querySelector('[data-more]');
    let page = 1, query = '', busy = false;
    async function load(append) {
        if (busy) return;
        busy = true;
        group.setAttribute('aria-busy', 'true');
        group.querySelectorAll('button').forEach(b => b.disabled = true);
        status.textContent = 'Buscando opciones…';
        const next = append ? page + 1 : 1;
        const term = append ? query : search.value.trim();
        try {
            const url = new URL(location.href);
            url.search = new URLSearchParams({handler:'Opciones',tipo:group.dataset.tipo,buscar:term,pagina:next});
            const response = await fetch(url, {headers:{Accept:'application/json'}});
            if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw Error();
            const result = await response.json(), selected = select.selectedOptions[0]?.cloneNode(true);
            if (!append) {
                select.replaceChildren(select.options[0].cloneNode(true));
                if (selected?.value) select.append(selected);
            }
            for (const option of result.opciones) {
                if (![...select.options].some(o => o.value === option.id)) select.add(new Option(option.nombre, option.id));
            }
            page = next; query = term; more.hidden = !result.hayMas;
            status.textContent = result.opciones.length ? (result.hayMas ? 'Hay más opciones. Afiná la búsqueda o cargá la siguiente página.' : 'Opciones actualizadas.') : 'Sin coincidencias. Probá otro nombre.';
        } catch { status.textContent = 'No pudimos cargar opciones. Reintentá la búsqueda; conservamos tu selección.'; }
        finally { busy = false; group.setAttribute('aria-busy', 'false'); group.querySelectorAll('button').forEach(b => b.disabled = false); }
    }
    group.querySelector('[data-search]').addEventListener('click', () => load(false));
    more.addEventListener('click', () => load(true));
    search.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); load(false); } });
}
