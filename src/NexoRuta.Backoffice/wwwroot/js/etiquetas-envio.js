const pendientes = new Set();

export async function descargarEtiquetas(widget) {
    const link = widget.querySelector('[data-label-link]');
    const status = widget.querySelector('[data-label-status]');
    if (!link || !status || pendientes.has(link.href)) return;
    const destino = link.href;
    const label = link.textContent;
    let failure = 'No pudimos descargar las etiquetas. Reintentá desde este botón.';
    pendientes.add(destino);
    widget.setAttribute('aria-busy', 'true');
    link.setAttribute('aria-disabled', 'true');
    link.textContent = 'Descargando…';
    status.textContent = 'Descargando etiquetas…';
    status.classList.toggle('text-danger', false);
    status.classList.toggle('text-body-secondary', true);
    try {
        const response = await fetch(destino, { credentials: 'same-origin', headers: { Accept: 'application/pdf' }, signal: AbortSignal.timeout(30000) });
        if (response.redirected && /\/ingresar(?:[?#]|$)/.test(response.url)) {
            failure = 'La sesión venció. Recargá la página para ingresar y reintentá.';
            throw new Error();
        }
        if (!response.ok) {
            const messages = {
                401: 'La sesión venció. Volvé a ingresar y reintentá.',
                403: 'No tenés permiso para descargar estas etiquetas. Pedí acceso a quien administra el operador.',
                404: 'El envío ya no está disponible. Actualizá el listado.'
            };
            failure = messages[response.status] ?? failure;
            throw new Error();
        }
        if (response.headers.get('content-type')?.split(';')[0] !== 'application/pdf') throw new Error();
        const bytes = await response.arrayBuffer();
        if (new TextDecoder().decode(bytes.slice(0, 5)) !== '%PDF-') throw new Error();
        const url = URL.createObjectURL(new Blob([bytes], { type: 'application/pdf' }));
        const download = document.createElement('a');
        download.href = url;
        download.download = `etiquetas-${destino.split('/').at(-2)}.pdf`;
        document.body.append(download);
        try { download.click(); }
        finally {
            download.remove();
            // Allow the browser to acquire the blob before releasing its object URL.
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        }
        status.textContent = 'Descarga iniciada. Podés volver a descargar para reimprimir.';
    } catch {
        status.textContent = failure;
        status.classList.toggle('text-body-secondary', false);
        status.classList.toggle('text-danger', true);
    } finally {
        pendientes.delete(destino);
        widget.removeAttribute('aria-busy');
        link.removeAttribute('aria-disabled');
        link.textContent = label;
    }
}

if (typeof document !== 'undefined') document.addEventListener('click', event => {
    const link = event.target.closest('[data-label-link]');
    if (!link || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    const widget = link.closest('[data-label-download]');
    if (!widget) return;
    event.preventDefault();
    void descargarEtiquetas(widget);
});
