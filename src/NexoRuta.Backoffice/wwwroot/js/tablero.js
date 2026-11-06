(() => {
    'use strict';
    const status = document.getElementById('tablero-estado');
    if (!status) return;
    const content = document.getElementById('tablero-contenido');
    const retry = document.getElementById('tablero-reintentar');
    const connection = new signalR.HubConnectionBuilder().withUrl('/hubs/tablero').withAutomaticReconnect([0, 2000, 5000, 10000]).build();
    let subscription, revision = -1;
    const short = id => id?.slice(-8) ?? 'Sin dato';
    const label = value => ({ EnTransito: 'En tránsito', EntregadoParcialmente: 'Entregado parcialmente', RetiroSolicitado: 'Retiro solicitado', RetiroAsignado: 'Retiro asignado', RetiroEnDeposito: 'Retiro en depósito', EntregadoComercio: 'Entregado al comercio', NoEntregado: 'No entregado', EnDeposito: 'En depósito', AsignadoRuta: 'Asignado a ruta', EntregaPendienteValidacion: 'Entrega pendiente de validación', EntregaSinValidacion: 'Entrega sin validación' }[value] ?? value ?? 'Sin dato');
    function row(values) {
        const tr = document.createElement('tr');
        for (const value of values) { const td = document.createElement('td'); td.textContent = value; tr.append(td); }
        return tr;
    }
    function render(snapshot) {
        if (snapshot.revision < revision) return;
        revision = snapshot.revision;
        const items = snapshot.entidades;
        const routes = items.filter(x => x.tipo === 'Ruta');
        const positions = items.filter(x => x.tipo === 'Posicion');
        const stops = items.filter(x => x.tipo === 'Parada');
        const shipments = items.filter(x => x.tipo === 'Envio');
        const parcels = items.filter(x => x.tipo === 'Bulto');
        const current = snapshot.estadoProyeccion === 'Actualizada';
        status.textContent = current ? 'Conectado · datos actualizados hasta la última verificación.' : (snapshot.estadoProyeccion === 'Atrasada' ? 'Conectado · datos atrasados. La información visible puede estar desactualizada.' : 'Conectado · actualización no disponible. Se conserva la última fotografía recibida.');
        document.getElementById('tablero-carga').hidden = true;
        document.getElementById('tablero-vacio').hidden = routes.length !== 0;
        content.setAttribute('aria-busy', 'false');
        const fleetRows = routes.map(route => {
            const position = positions.find(p => p.rutaId === route.entidadId);
            const sample = position && new Date(position.ocurridaEnUtc);
            const age = sample ? Math.max(0, Math.floor((Date.now() - sample.getTime()) / 1000)) : null;
            const ownStops = stops.filter(s => s.rutaId === route.entidadId);
            const pending = ownStops.filter(s => s.datos.activa && s.datos.resultadoVisita === 'Pendiente').length;
            return row([`${short(route.entidadId)} / ${route.datos.repartidorNombre ?? short(route.datos.repartidorAccesoId)} / ${route.datos.matricula ?? short(route.datos.vehiculoId)}`,
                position ? `${position.datos.latitud}, ${position.datos.longitud}` : 'Sin muestra',
                age === null ? 'Sin muestra' : `${age} s${age > 120 ? ' · muestra antigua' : ''}`,
                `${pending} pendientes · ${ownStops.filter(s => s.datos.resultadoVisita === 'Completada').length}/${ownStops.length} completadas`]);
        });
        document.getElementById('tablero-flota').replaceChildren(...fleetRows);
        const shipmentRows = shipments.map(shipment => {
            const ownParcels = parcels.filter(p => p.envioId === shipment.entidadId);

            const code = snapshot.riesgos?.find(r => r.envioId === shipment.entidadId)?.riesgo;
            const risk = ({ Finalizado: 'Finalizado', SinCompromiso: 'Sin compromiso disponible', ProyeccionAtrasada: 'No evaluable · datos atrasados', PlazoIncumplido: 'Plazo incumplido', SinEstimacion: 'Sin estimación vigente', EnRiesgo: 'En riesgo · llegada después del plazo', SinRiesgoDetectado: 'Sin riesgo detectado con la llegada planificada' })[code] ?? 'Sin evaluación disponible';
            const delivered = shipment.datos.esRetiro
                ? `${ownParcels.filter(p => p.datos.custodia === 'Comercio').length}/${ownParcels.length} al comercio`
                : `${ownParcels.filter(p => p.datos.resultado === 'Entregado').length}/${ownParcels.length} entregados`;
            return row([short(shipment.entidadId), shipment.datos.esRetiro ? 'Retiro' : 'Entrega', label(shipment.datos.estado), delivered, risk]);
        });
        document.getElementById('tablero-envios').replaceChildren(...shipmentRows);
    }
    function subscribe() {
        subscription?.dispose();
        subscription = connection.stream('Fotografias').subscribe({ next: render, complete: () => disconnected('El canal terminó.'), error: () => disconnected('No se puede leer la operación. Revisá la sesión y reconectá.') });
    }
    function disconnected(message) { status.textContent = `${message} Los datos visibles corresponden a la última fotografía recibida.`; content.setAttribute('aria-busy', 'false'); document.getElementById('tablero-carga').hidden = true; retry.hidden = false; }
    async function start() {
        const recoverFocus = document.activeElement === retry;
        retry.hidden = true; status.textContent = 'Conectando con la operación…';
        try { if (connection.state === signalR.HubConnectionState.Connected) await connection.stop(); await connection.start(); subscribe(); if (recoverFocus) document.getElementById('tablero-titulo').focus(); }
        catch { disconnected('No hay conexión con la operación.'); if (recoverFocus) retry.focus(); }
    }
    connection.onreconnecting(() => disconnected('Reconectando.'));
    connection.onreconnected(() => { retry.hidden = true; subscribe(); });
    connection.onclose(() => disconnected('Desconectado.'));
    retry.addEventListener('click', start);
    start();
})();
