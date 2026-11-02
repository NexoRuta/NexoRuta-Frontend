// Run: node --test tests/rutas-planificacion.test.cjs
const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

test('planning summary converts grams/cm3, includes every row and reports independent excesses', () => {
    const element = (value = '') => ({ value, textContent: '', dataset: {}, listeners: {},
        addEventListener(name, fn) { this.listeners[name] = fn; }, classList: { toggle() {} } });
    const selectors = new Map();
    const get = selector => {
        if (!selectors.has(selector)) selectors.set(selector, element());
        return selectors.get(selector);
    };
    const rows = [
        { weight: '1000', volume: '1000000', packageCount: '1' },
        { weight: '2000', volume: '3000000', packageCount: '2' }
    ].map((data, i) => {
        const inputs = new Map(['[data-plan-check]', '[data-stop-order]', '[data-stop-time]'].map(x => [x, element()]));
        inputs.get('[data-plan-check]').checked = true;
        inputs.get('[data-stop-order]').value = String(i + 1);
        inputs.get('[data-stop-time]').value = '10:30';
        return { dataset: { ...data, destination: 'true', custody: 'true', eligible: 'true', search: 'package' },
            classList: { toggle() {} }, querySelector: s => inputs.get(s) };
    });
    for (const s of ['[data-summary-destination]', '[data-summary-custody]']) {
        const condition = get(s); const children = new Map();
        condition.querySelector = x => { if (!children.has(x)) children.set(x, element()); return children.get(x); };
    }
    const form = { querySelector: get, querySelectorAll: () => rows };
    get('#Fecha').value = '2026-10-20'; get('#eligibility-filter').value = 'all';
    const option = { textContent: 'ABC', dataset: { weightCapacity: '3000', volumeCapacity: '4000000' } };
    get('[data-vehicle-select]').selectedOptions = [option]; get('#RepartidorAccesoId').selectedOptions = [{ textContent: 'Courier' }];
    const document = { readyState: 'complete', querySelector: s => s === '#route-planning-form' ? form : get(s) };
    const source = fs.readFileSync(path.join(__dirname, '../src/NexoRuta.Backoffice/wwwroot/js/rutas-planificacion.js'), 'utf8');
    vm.runInNewContext(source, { document, Intl, Date });
    assert.equal(get('[data-summary-weight]').textContent, '3 kg');
    assert.equal(get('[data-summary-volume]').textContent, '4 m³');
    assert.equal(get('[data-summary-packages]').textContent, '3');
    assert.equal(get('[data-summary-volume-capacity]').textContent, '4 m³');
    assert.match(get('[data-summary-weight-excess]').textContent, /dentro/);
    assert.match(get('[data-summary-volume-excess]').textContent, /dentro/);
    option.dataset.weightCapacity = '2999.99'; get('[data-vehicle-select]').listeners.change();
    assert.match(get('[data-summary-weight-excess]').textContent, /excede/);
    assert.match(get('[data-summary-volume-excess]').textContent, /dentro/);
    option.dataset.weightCapacity = '3000'; option.dataset.volumeCapacity = '3999999.99';
    get('[data-vehicle-select]').listeners.change();
    assert.match(get('[data-summary-weight-excess]').textContent, /dentro/);
    assert.match(get('[data-summary-volume-excess]').textContent, /excede/);
    assert.match(get('[data-plan-summary]').textContent, /volumen excede/);
});
