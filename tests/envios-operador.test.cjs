const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const vm = require('node:vm');
const directory = path.join(__dirname, '../src/NexoRuta.Backoffice/wwwroot/js');
let sequence = 0;

async function setup() {
    const events = {}, menus = [];
    const document = {
        querySelectorAll(selector) { return selector.includes('data-shipment-menu') ? menus.filter(x => x.open) : []; },
        querySelector(selector) { return selector.includes('data-shipment-menu') ? menus.find(x => x.open) : null; },
        addEventListener(name, handler) { events[name] = handler; }
    };
    global.document = document; global.window = { addEventListener() {} };
    const source = await fs.readFile(path.join(directory,'envios-operador.js'),'utf8');
    const fresh = await import('data:text/javascript;base64,'+Buffer.from(source+'\n// case '+ ++sequence).toString('base64'));
    return { module:fresh, events, menus, document };
}

test('native action disclosure closes with Escape and restores its summary focus', async () => {
    const s=await setup();let focus=0,prevented=0;
    const menu={open:true,querySelector:()=>({focus(){focus++;}})};s.menus.push(menu);
    s.events.keydown({key:'Escape',preventDefault(){prevented++;}});
    assert.equal(menu.open,false);assert.equal(focus,1);assert.equal(prevented,1);
    s.events.keydown({key:'Escape',preventDefault(){prevented++;}});assert.equal(prevented,1);
});

test('copy and download remain independent actions instead of opening shipment detail', async () => {
    const s=await setup();let focus=0;
    const menu={open:true,querySelector:()=>({focus(){focus++;}})};s.menus.push(menu);
    for (const action of ['copy','download']) {
        menu.open=true;
        const target={closest(selector){
            if(selector==='details[data-shipment-menu]')return menu;
            if(selector==='[data-bs-toggle="offcanvas"]')return null;
            return selector.includes('data-copy-shipment-id')&&action==='copy'||selector.includes('data-label-link')&&action==='download'?{action}:null;
        }};
        s.events.click({target});assert.equal(menu.open,action==='download');
    }
    assert.equal(focus,1);
    // No row click or navigation handler is installed by the action-menu module.
    assert.deepEqual(Object.keys(s.events).sort(),['click','keydown']);
});

test('outside click closes other menus without stealing the target focus', async () => {
    const s=await setup();let focus=0;
    const menu={open:true,querySelector:()=>({focus(){focus++;}})};s.menus.push(menu);
    s.events.click({target:{closest(){return null;}}});assert.equal(menu.open,false);assert.equal(focus,0);
});

test('real refresh submit has local progress and back navigation restores only that control', async () => {
    const s=await setup(),formEvents={},pageEvents={};
    const button={disabled:false,textContent:'Actualizar',dataset:{loadingText:'Actualizando…'}};
    const form={dataset:{},attributes:{},addEventListener(k,v){formEvents[k]=v;},setAttribute(k,v){this.attributes[k]=v;},removeAttribute(k){delete this.attributes[k];},
        querySelectorAll(){return[button];},querySelector(){return button;}};
    const source=await fs.readFile(path.join(directory,'site.js'),'utf8');
    const doc={querySelectorAll(selector){return selector==='form[data-single-submit]'?[form]:[];},querySelector(){return null;},getElementById(){return null;},addEventListener(){}};
    vm.runInNewContext(source,{document:doc,window:{addEventListener(){}},sessionStorage:{getItem(){return null;}},navigator:{}});
    s.module.prepararActualizacion(form,{addEventListener(k,v){pageEvents[k]=v;}});
    let prevented=0;formEvents.submit({preventDefault(){prevented++;}});
    assert.equal(button.disabled,true);assert.equal(button.textContent,'Actualizando…');assert.equal(form.attributes['aria-busy'],'true');
    formEvents.submit({preventDefault(){prevented++;}});assert.equal(prevented,1);
    pageEvents.pageshow();assert.equal(button.disabled,false);assert.equal(button.textContent,'Actualizar');assert.equal(form.dataset.submitted,undefined);
});


test('generated PDF download keeps its menu result and keyboard context visible', async () => {
    const s=await setup();const menu={open:true,querySelector:()=>({focus(){}})};s.menus.push(menu);
    s.events.click({target:{closest(selector){return selector.startsWith('a[download]')?{}:null;}}});
    assert.equal(menu.open,true);
});
