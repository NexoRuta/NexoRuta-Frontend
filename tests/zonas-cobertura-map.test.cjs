const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const geometry = { type: 'MultiPolygon', coordinates: [[[[-56, -35], [-55, -35], [-55, -34], [-56, -35]]]] };

async function render(mode, zones = []) {
    const handlers = {}, sources = {}, layers = [], fits = [], maxBounds = [];
    const root = { dataset: { mode, token: 'pk.synthetic', style: 'controlled', editable: 'false', geometry: JSON.stringify(geometry), zones: JSON.stringify(zones), territoryUrl: '/territory' }, style: {} };
    const elements = { 'coverage-map': root, 'map-status': {}, 'zone-map-data': { textContent: JSON.stringify(zones) } };
    const map = { on(name, callback) { handlers[name] = callback; }, addControl() {}, addSource(name, source) { sources[name] = source; }, addLayer(layer) { layers.push(layer); }, cameraForBounds() { return { zoom: 5 }; }, setMinZoom() {}, setMaxBounds(value) { maxBounds.push(value); }, fitBounds(bounds) { fits.push(bounds); }, getSource(name) { return sources[name]; }, remove() {} };
    const document = { getElementById(id) { return elements[id] ?? null; }, querySelectorAll() { return []; }, querySelector() { return null; }, createElement() { return { dataset: {}, append() {}, addEventListener() {} }; } };
    const window = { mapboxgl: { Map: function () { return map; }, NavigationControl: class {}, Marker: class { setLngLat() { return this; } addTo() { return this; } } } };
    const source = fs.readFileSync(path.join(__dirname, '../src/NexoRuta.Backoffice/wwwroot/js/zonas-cobertura.js'), 'utf8');
    vm.runInNewContext(source, { document, window, console, URL, getComputedStyle() { return { getPropertyValue(name) { return name.includes('inactive') ? '#53545b' : '#176b45'; } }; }, fetch: async () => ({ ok: true, json: async () => ({ limites: { oeste: -59, sur: -36, este: -53, norte: -30 }, geometria: geometry }) }) });
    await new Promise(resolve => setImmediate(resolve));
    assert.ok(handlers.load, 'map initialized');
    handlers.load();
    return JSON.parse(JSON.stringify({ sources, layers, fits, maxBounds }));
}

test('detail highlights and frames the stored geometry, not the whole country', async () => {
    const result = await render('detail');
    assert.deepEqual(result.sources['zone-coverage'].data.geometry, geometry);
    assert.deepEqual(result.maxBounds, [null]);
    assert.deepEqual(result.fits.at(-1), [[-56, -35], [-55, -34]]);
});

test('overview renders both states with names and detail identities and frames all zones', async () => {
    const zones = [{ id: 'a', nombre: 'Canelones', estado: 'Activa', geometria: geometry }, { id: 'b', nombre: 'San José', estado: 'Inactiva', geometria: geometry }];
    const result = await render('overview', zones);
    const features = result.sources['zone-overview'].data.features;
    assert.equal(features.length, 2);
    assert.deepEqual(features.map(f => f.properties.estado), ['Activa', 'Inactiva']);
    assert.deepEqual(features.map(f => f.properties.nombre), ['Canelones', 'San José']);
    assert.equal(result.layers.find(l => l.id === 'zone-overview-fill').paint['fill-color'][0], 'match');
    assert.deepEqual(result.fits.at(-1), [[-56, -35], [-55, -34]]);
});
