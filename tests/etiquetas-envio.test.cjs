const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');

async function setup() {
    const source = await fs.readFile(path.join(__dirname, '../src/NexoRuta.Backoffice/wwwroot/js/etiquetas-envio.js'), 'utf8');
    const actions = [], timers = [];
    global.document = { addEventListener() {}, body: { append() {} }, createElement() { return { click() { actions.push('download'); }, remove() {} }; } };
    global.URL = { createObjectURL() { return 'blob:controlled'; }, revokeObjectURL(url) { actions.push(url); } };
    global.setTimeout = callback => timers.push(callback);
    const module = await import('data:text/javascript;base64,' + Buffer.from(source + '\n// ' + Math.random()).toString('base64'));
    const widget = () => {
        const link = { href: 'http://localhost/envios/00000000-0000-0000-0000-000000000001/etiquetas', textContent: 'Descargar etiquetas', attributes: {}, setAttribute(k,v) { this.attributes[k]=v; }, removeAttribute(k) { delete this.attributes[k]; } };
        const status = { textContent: '', classList: { toggle() {} } };
        return { link, status, attributes: {}, setAttribute(k,v) {this.attributes[k]=v;}, removeAttribute(k) {delete this.attributes[k];}, querySelector(selector) { return selector.includes('data-label-link') ? link : status; } };
    };
    return { module, actions, timers, widget };
}
const pdf = () => ({ ok: true, status: 200, headers: { get: () => 'application/pdf' }, arrayBuffer: async () => new TextEncoder().encode('%PDF-controlled').buffer });

test('download exposes progress, prevents duplicate requests across widgets and releases object URL', async () => {
    const s=await setup(); const widget=s.widget(); let finish, requests=0;
    global.fetch=async (_,options) => {requests++;assert.equal(options.credentials,'same-origin');return new Promise(resolve=>finish=resolve);};
    const pending=s.module.descargarEtiquetas(widget);
    assert.equal(widget.attributes['aria-busy'],'true');
    assert.equal(widget.link.attributes['aria-disabled'],'true');
    assert.match(widget.status.textContent,/Descargando/);
    await s.module.descargarEtiquetas(widget); await s.module.descargarEtiquetas(s.widget());
    assert.equal(requests,1); finish(pdf()); await pending;
    assert.deepEqual(s.actions,['download']); assert.match(widget.status.textContent,/iniciada/);
    assert.equal(widget.link.attributes['aria-disabled'],undefined);
    assert.equal(widget.attributes['aria-busy'],undefined);
    s.timers.forEach(callback=>callback()); assert.deepEqual(s.actions,['download','blob:controlled']);
});

test('failed download retains an actionable retry and retry can succeed', async () => {
    const s=await setup(); const widget=s.widget(); let attempts=0;
    global.fetch=async ()=> { if(++attempts===1) throw new Error('controlled failure');return pdf(); };
    await s.module.descargarEtiquetas(widget);
    assert.match(widget.status.textContent,/Reintent/);assert.equal(widget.link.textContent,'Descargar etiquetas');
    assert.deepEqual(s.actions,[]);
    await s.module.descargarEtiquetas(widget);assert.equal(attempts,2);assert.deepEqual(s.actions,['download']);
});

test('expired authorization or invalid content never becomes a PDF download', async () => {
    const s=await setup(); const widget=s.widget();
    global.fetch=async ()=>({ok:false,status:403});await s.module.descargarEtiquetas(widget);assert.match(widget.status.textContent,/permiso/);
    global.fetch=async ()=>({...pdf(),redirected:true,url:'http://localhost/ingresar?ReturnUrl=x'});
    await s.module.descargarEtiquetas(widget);assert.match(widget.status.textContent,/sesión venció/);
    global.fetch=async ()=>({...pdf(),arrayBuffer:async()=>new TextEncoder().encode('<html>').buffer});
    await s.module.descargarEtiquetas(widget);assert.match(widget.status.textContent,/Reintent/);assert.deepEqual(s.actions,[]);
});

test('timeout while consuming the PDF body clears progress and allows retry', async () => {
    const s=await setup(); const widget=s.widget(); const controller=new AbortController();
    const timeout=AbortSignal.timeout;
    AbortSignal.timeout=milliseconds=>{assert.equal(milliseconds,30000);return controller.signal;};
    try {
        global.fetch=async (_,options)=>({...pdf(),arrayBuffer:()=>new Promise((_,reject)=>options.signal.addEventListener('abort',()=>reject(new Error('controlled body timeout')),{once:true}))});
        const pending=s.module.descargarEtiquetas(widget);
        await new Promise(setImmediate);controller.abort();await pending;
        assert.match(widget.status.textContent,/Reintent/);
        assert.equal(widget.attributes['aria-busy'],undefined);
        assert.equal(widget.link.attributes['aria-disabled'],undefined);
        global.fetch=async()=>pdf();await s.module.descargarEtiquetas(widget);
        assert.deepEqual(s.actions,['download']);
    } finally {AbortSignal.timeout=timeout;}
});
