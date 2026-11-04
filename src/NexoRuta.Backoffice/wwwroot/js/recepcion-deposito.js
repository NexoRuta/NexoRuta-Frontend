(() => {
    'use strict';
    const start = document.getElementById('activar-camara'), stopButton = document.getElementById('detener-camara');
    const video = document.getElementById('video-recepcion'), status = document.getElementById('estado-camara');
    const input = document.getElementById('codigo-bulto'), form = document.getElementById('buscar-bulto');
    if (!start || !video || !input || !form) return;
    input.addEventListener('focus', () => input.select());
    if (document.activeElement === input) input.select();
    let generation = 0, stream = null, controls = null;
    function stop(focus = false) {
        generation++;
        controls?.stop(); controls = null;
        stream?.getTracks().forEach(track => track.stop()); stream = null;
        video.srcObject = null; video.hidden = true;
        stopButton.hidden = true; start.disabled = false;
        start.removeAttribute('aria-busy');
        if (focus) input.focus();
    }
    start.addEventListener('click', async () => {
        stop(); const attempt = generation;
        if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia || !window.ZXingBrowser) {
            status.textContent = 'La cámara no está disponible en este navegador o conexión. Usá el lector o ingresá el código.';
            input.focus(); return;
        }
        start.disabled = true; start.setAttribute('aria-busy', 'true'); stopButton.hidden = false;
        status.textContent = 'Esperando permiso de cámara. También podés cancelar y usar el código.';
        try {
            const obtained = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' } }, audio: false });
            if (attempt !== generation) { obtained.getTracks().forEach(track => track.stop()); return; }
            stream = obtained; video.hidden = false;
            status.textContent = 'Apuntá al QR de un bulto. La lectura solo consulta sus datos.';
            const reader = new ZXingBrowser.BrowserQRCodeReader();
            const resultControls = await reader.decodeFromStream(stream, video, (result, error, callbackControls) => {
                if (!result || attempt !== generation) return;
                const code = result.getText().trim();
                if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(code)) {
                    status.textContent = 'Ese QR no contiene un UUID de bulto NexoRuta. Probá otra etiqueta o ingresá el código.';
                    return;
                }
                callbackControls.stop(); stop(); input.value = code; input.focus();
                status.textContent = 'QR leído. Consultando el bulto…'; form.requestSubmit();
            });
            if (attempt !== generation) resultControls.stop(); else controls = resultControls;
        } catch (error) {
            if (attempt !== generation) return;
            stop();
            status.textContent = error.name === 'NotAllowedError' || error.name === 'SecurityError'
                ? 'Permiso de cámara denegado. Podés habilitarlo en el navegador o ingresar el código.'
                : error.name === 'NotFoundError' ? 'No encontramos una cámara. Usá un lector o ingresá el código.'
                : 'No pudimos abrir la cámara. Cerrá otras aplicaciones e intentá de nuevo, o ingresá el código.';
            input.focus();
        }
    });
    stopButton.addEventListener('click', () => { stop(true); status.textContent = 'Cámara detenida. Podés usar el código o volver a abrirla.'; });
    form.addEventListener('submit', () => stop());
    document.addEventListener('visibilitychange', () => { if (document.hidden) { stop(); status.textContent = 'Cámara detenida al salir de la página.'; } });
    window.addEventListener('pagehide', () => stop());
    const receipt = document.getElementById('cotejar-bulto');
    if (receipt) {
        const refreshRequired = () => {
            const conforming = receipt.querySelector('#cotejo-conforme').checked;
            receipt.querySelector('textarea').required = !conforming;
            receipt.querySelectorAll('input[type=number]').forEach(field => { field.required = conforming; });
        };
        receipt.querySelectorAll('input[type=radio]').forEach(radio => radio.addEventListener('change', refreshRequired));
        refreshRequired();
    }
    for (const current of [form, receipt].filter(Boolean)) {
        let submitting = false;
        current.addEventListener('submit', event => {
            if (submitting) { event.preventDefault(); return; }
            submitting = true; current.setAttribute('aria-busy', 'true');
            current.querySelectorAll('button[type=submit]').forEach(button => { button.disabled = true; button.dataset.previousText = button.textContent; button.textContent = current === form ? 'Buscando…' : 'Registrando…'; });
        });
        window.addEventListener('pageshow', () => {
            submitting = false; current.removeAttribute('aria-busy');
            current.querySelectorAll('button[type=submit]').forEach(button => { button.disabled = false; if (button.dataset.previousText) button.textContent = button.dataset.previousText; });
        });
    }
    document.querySelector('[data-receipt-error]')?.focus();
})();
