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
    global.getComputedStyle = () => ({ getPropertyValue() { return '#005cc8'; } });
    let stylesheet;
    global.document = { body: {}, querySelector() { return null; }, createElement() { return { addEventListener(name, callback) { if (name === 'load') this.ready = callback; }, remove() {} }; }, head: { append(element) { stylesheet = element; queueMicrotask(() => element.ready()); } } };
    global.ResizeObserver = class { observe() {} disconnect() {} };
    global.MutationObserver = class { observe() {} disconnect() {} };
    let coverage, sourceCreated = 0;
    global.mapboxgl = {
        Map: class {
            on(name, callback) { handlers[name] = callback; }
            once() {} addControl() {} jumpTo() {} resize() {} fitBounds() {}
            remove() { removed = true; }
            isStyleLoaded() { return true; }
            getSource() { return sourceCreated ? { setData(value) { coverage = value; } } : null; }
            addSource(_, value) { sourceCreated++; coverage = value.data; }
            addLayer() {} getLayer() { return null; } moveLayer() {}
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
    assert.match(stylesheet.href, /mapbox-gl.css$/);
    const geometry = { type: 'Polygon', coordinates: [[[-57,-35],[-56,-35],[-56,-34],[-57,-35]]] };
    map.cobertura([{ nombre: 'Selected operator', geometria: geometry }]);
    assert.deepEqual(coverage.features[0].geometry, geometry);
    map.cobertura([]); assert.deepEqual(coverage.features, []); assert.equal(sourceCreated, 1);
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

test('preloaded SDK still waits for its stylesheet before constructing the map', async () => {
    const source = await fs.readFile(path.join(__dirname, '../src/NexoRuta.Commerce/wwwroot/js/destino-mapbox.js'), 'utf8');
    const module = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64') + '#css-ready');
    let loaded, maps = 0;
    global.document = { body: {}, querySelector() { return null; }, createElement() { return { addEventListener(event, callback) { if (event === 'load') loaded = callback; } }; }, head: { append() {} } };
    global.getComputedStyle = () => ({ getPropertyValue() { return '#005cc8'; } });
    global.ResizeObserver = global.MutationObserver = class { observe() {} disconnect() {} };
    global.mapboxgl = { Map: class { constructor() { maps++; } addControl() {} on() {} once() {} remove() {} }, NavigationControl: class {} };
    const pending = module.crear({ isConnected: true }, 'pk.synthetic', 'local-style', { invokeMethodAsync() { return Promise.resolve(); } }, true);
    await new Promise(r => setImmediate(r));
    assert.equal(maps, 0); assert.equal(typeof loaded, 'function');
    loaded(); const map = await pending; assert.equal(maps, 1); map.dispose();
});


test('coverage survives style readiness and reload, updates while GeoJSON is pending and frames real vertices without moving the pin', async () => {
    const text = await fs.readFile(path.join(__dirname, '../src/NexoRuta.Commerce/wwwroot/js/destino-mapbox.js'), 'utf8');
    const module = await import('data:text/javascript;base64,' + Buffer.from(text + '\n// coverage lifecycle').toString('base64'));
    const handlers = {}, sources = {}, layers = new Map(), notifications = []; let fitted, pin, styleLoaded = false, writes = 0, failLayers = false;
    global.document = { body: {}, querySelector() { return { sheet: {} }; } };
    global.getComputedStyle = () => ({ color: '#1d1d1f', getPropertyValue() { return '#005cc8'; } });
    global.ResizeObserver = global.MutationObserver = class { observe() {} disconnect() {} };
    global.mapboxgl = { NavigationControl: class {}, Marker: class {
        setLngLat(p) { pin = p; return this; } addTo() { return this; } on() {} setDraggable() {}
    }, Map: class {
        on(n, f) { handlers[n] = f; } once(n, f) { handlers[n] = f; } addControl() {} resize() {} jumpTo() {} remove() {}
        isStyleLoaded() { return styleLoaded; } // GeoJSON loading must not block source updates.
        getSource(id) { return sources[id]; } getLayer(id) { return layers.get(id); } moveLayer() {}
        addSource(id, s) { writes++; sources[id] = { data: s.data, setData(d) { writes++; this.data = d; } }; }
        addLayer(l) { if (failLayers) throw new Error('Controlled coverage renderer failure'); layers.set(l.id, l); }
        fitBounds(b) { fitted = b; } isSourceLoaded() { return true; }
    } };
    const map = await module.crear({ isConnected: true }, 'pk.synthetic', 'controlled', { invokeMethodAsync(...args) { notifications.push(args); return Promise.resolve(); } }, true);
    const geometry = { type: 'MultiPolygon', coordinates: [[[[-57,-35],[-55,-35],[-55,-33],[-57,-35]]]] };
    map.cobertura([{ nombre: 'Real zone', geometria: geometry }]);
    assert.equal(sources['destination-coverage'], undefined);
    assert.equal(typeof handlers['style.load'], 'function');
    handlers['style.load']();
    assert.deepEqual(sources['destination-coverage'].data.features[0].geometry, geometry);
    assert.deepEqual(fitted, [[-57,-35],[-55,-33]], 'choosing an operator must frame its real coverage before an address is selected');
    map.cobertura([]); assert.deepEqual(sources['destination-coverage'].data.features, []);
    map.cobertura([{ nombre: 'Real zone', geometria: geometry }]);
    delete sources['destination-coverage']; layers.clear(); handlers['style.load']();
    assert.equal(sources['destination-coverage'].data.features.length, 1);
    delete sources['destination-coverage']; layers.clear(); styleLoaded = true;
    handlers.styledata(); // Default differential setStyle does not emit style.load.
    assert.equal(sources['destination-coverage'].data.features.length, 1);
    const settledWrites = writes; handlers.styledata(); handlers.idle();
    assert.equal(writes, settledWrites, 'layer restoration must settle without a styledata render loop');
    assert.ok(layers.get('destination-coverage-line').paint['line-width'] >= 3);
    map.mostrar(-34, -56); map.verCobertura();
    assert.deepEqual(fitted, [[-57,-35],[-55,-33]]);
    assert.deepEqual(pin, [-56,-34]);
    delete sources['destination-coverage']; layers.clear(); failLayers = true;
    assert.doesNotThrow(() => handlers.styledata());
    assert.deepEqual(notifications.at(-1), ['EstadoCobertura', 'error']);
    const failedWrites = writes; handlers.idle(); handlers.styledata();
    assert.equal(writes, failedWrites, 'a failed layer must not trigger an idle retry loop');
    failLayers = false;
    map.cobertura([{ zonaId: 'zone', nombre: 'Real zone', geometria: geometry }]); handlers.idle();
    assert.deepEqual(notifications.at(-1), ['EstadoCobertura', 'listo']);
    map.dispose();
});
