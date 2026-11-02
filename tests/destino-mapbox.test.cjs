const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');

test('destination Mapbox module only renders/selects points and never geocodes', async () => {
    const source = await fs.readFile(path.join(__dirname, '../src/NexoRuta.Commerce/wwwroot/js/destino-mapbox.js'), 'utf8');
    assert.doesNotMatch(source, /permanent|\/geocod(?:e|ing)|MapboxGeocoder|forwardGeocode|searchbox/i);
    const module = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
    assert.equal(module.buscarDirecciones, undefined);
    const handlers = {}, messages = [];
    let position, removed = false;
    global.fetch = () => { throw new Error('External network is forbidden'); };
    global.document = { body: {} };
    global.ResizeObserver = class { observe() {} disconnect() {} };
    global.MutationObserver = class { observe() {} disconnect() {} };
    global.mapboxgl = {
        Map: class {
            on(name, callback) { handlers[name] = callback; }
            once() {} addControl() {} jumpTo() {} resize() {}
            remove() { removed = true; }
        },
        Marker: class {
            setLngLat(value) { position = value; return this; }
            addTo() { return this; } on() {} remove() {} setDraggable() {}
        },
        NavigationControl: class {}
    };
    const map = await module.crear({ isConnected: true }, 'pk.synthetic', 'synthetic-style', {
        invokeMethodAsync(...args) { messages.push(args); return Promise.resolve(); }
    }, true);
    map.mostrar(-34.9, -56.2);
    assert.deepEqual(position, [-56.2, -34.9]);
    handlers.click({ lngLat: { lat: -34.91, lng: -56.21 } });
    assert.deepEqual(messages, [['Seleccionar', -34.91, -56.21, null]]);
    map.configurarEdicion(false);
    handlers.click({ lngLat: { lat: 0, lng: 0 } });
    assert.equal(messages.length, 1);
    map.dispose(); assert.equal(removed, true);
});


test('destination transport cannot enable permanent, batch or fallback geocoding', async () => {
    const source = await fs.readFile(path.join(__dirname, '../../backend/src/NexoRuta.Infrastructure/Envios/Geocodificacion/GeocodificadorMapboxTemporal.cs'), 'utf8');
    assert.match(source, /permanent=false/);
    assert.doesNotMatch(source, /permanent=true|places-permanent|geocode\/v6\/batch/);
    assert.match(source, /No result cache/);
});

test('keyboard commands keep arrow and enter order across asynchronous interop', async () => {
    const source = await fs.readFile(path.join(__dirname, '../src/NexoRuta.Commerce/wwwroot/js/destino-mapbox.js'), 'utf8');
    const module = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
    let handler; const calls = []; let release;
    const input = {addEventListener(_, h) {handler=h;},removeEventListener(){},getAttribute(){return 'true';}};
    module.conectarTeclado(input, {async invokeMethodAsync(name) {calls.push(name);if(name==='MoverSugerencia')await new Promise(r=>release=r);}});
    handler({key:'ArrowDown',preventDefault(){}}); handler({key:'Enter',preventDefault(){}});
    await new Promise(r=>setImmediate(r)); assert.deepEqual(calls,['MoverSugerencia']); release();
    await new Promise(r=>setImmediate(r)); assert.deepEqual(calls,['MoverSugerencia','ElegirSugerenciaActiva']);
});
