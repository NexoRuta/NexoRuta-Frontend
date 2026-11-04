const { test } = require('node:test');
const assert = require('node:assert/strict');
const { matchesVehicle, normalizedFilter } = require('../src/NexoRuta.Backoffice/wwwroot/js/flota.js');
test('fleet filters combine vehicle text and explicit availability without treating enabled as available', () => {
    assert.equal(matchesVehicle('CU06-LOCAL-V', 'Ocupado', 'cu06', 'Todas'), true);
    assert.equal(matchesVehicle('CU06-LOCAL-V', 'Ocupado', 'cu06', 'Disponible'), false);
    assert.equal(matchesVehicle('DEMO-A-001', 'Disponible', ' demo ', 'Disponible'), true);
    assert.equal(matchesVehicle('ABCD', 'Pendiente', '', 'Pendiente'), true);
    assert.equal(matchesVehicle('ABCD', 'Disponible', '', 'NoHabilitado', false), true);
});
test('unknown status query normalizes to the explicit all-vehicles scope', () => {
    assert.equal(normalizedFilter('ocupado'), 'Ocupado');
    assert.equal(normalizedFilter('not-a-state'), 'Todas');
    assert.equal(normalizedFilter(null), 'Todas');
});
