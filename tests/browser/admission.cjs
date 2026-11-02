const assert=require('node:assert/strict');
const fs=require('node:fs/promises');const path=require('node:path');
const {chromium}=require(process.env.NEXORUTA_PLAYWRIGHT_MODULE);
(async()=>{
 const base=process.env.DESTINATION_BASE_URL;const out=process.env.NEXORUTA_DESTINATION_BROWSER_DIR;
 assert.match(base,/^http:\/\/(localhost|127\.0\.0\.1):\d+\/$/);
 const browser=await chromium.launch({headless:true});const page=await browser.newPage({viewport:{width:1440,height:1000}});
 const external=[],errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',route=>{if(new URL(route.request().url()).origin!==new URL(base).origin){external.push(route.request().url());return route.abort();}return route.continue();});
    await page.addInitScript(() => {
        // No real map, tiles, SDK, telemetry or geocoding requests in this test.
        class Map {
            constructor(options) {
                this.handlers = {};
                const button = document.createElement('button');
                button.type = 'button'; button.textContent = 'Mapa simulado: seleccionar punto';
                button.onclick = () => this.handlers.click?.({ lngLat: { lat: -34.91, lng: -56.21 } });
                options.container.append(button);
            }
            on(name, callback) { this.handlers[name] = callback; }
            once(name, callback) { if (name === 'load') setTimeout(callback, 0); }
            addControl() {} resize() {} remove() {} jumpTo() {}
        }
        class Marker {
            setLngLat(point) { this.point = point; return this; }
            addTo() { return this; } on() {} setDraggable() {} remove() {}
        }
        globalThis.mapboxgl = { Map, Marker, NavigationControl: class {} };
    });
 try{
  await page.goto(base+'ingresar');await page.locator('#Email').fill(process.env.DESTINATION_EMAIL);await page.locator('#Password').fill(process.env.DESTINATION_PASSWORD);
  await Promise.all([page.waitForNavigation(),page.getByRole('button',{name:'Ingresar',exact:true}).click()]);
  await page.goto(base+'envios/crear');
  await page.getByRole('button',{name:'Mapa simulado: seleccionar punto'}).waitFor();
  await page.getByRole('combobox',{name:'Operador',exact:true}).selectOption(process.env.ADMISSION_OPERATOR);
  await page.getByLabel('Nombre',{exact:true}).fill('Destinatario CU09 navegador');await page.getByLabel('Correo del destinatario',{exact:true}).fill('cu09-browser@example.test');
  const search=page.getByRole('combobox',{name:'Buscar dirección'});await search.fill('Primera');await page.getByRole('option',{name:'Primera 123',exact:true}).waitFor();
  await search.press('ArrowDown');await search.press('Enter');
  await page.waitForFunction(()=>document.querySelector('#alta-servicio')?.value.includes('|Estandar'));
  await page.getByRole('button',{name:'Mapa simulado: seleccionar punto'}).click();
  await page.waitForFunction(()=>document.querySelector('#alta-servicio')?.value.includes('|Estandar'));
  const one=page.getByRole('group',{name:'Bulto 1',exact:true});
  for(const [label,value] of [['Código','CU09-UI-UNO'],['Peso (g)','500'],['Largo (cm)','20'],['Ancho (cm)','10'],['Alto (cm)','5']])await one.getByLabel(label,{exact:true}).fill(value);
  await page.getByRole('button',{name:'Agregar bulto',exact:true}).click();const two=page.getByRole('group',{name:'Bulto 2',exact:true});
  for(const [label,value] of [['Código','CU09-UI-DOS'],['Peso (g)','1500'],['Largo (cm)','30'],['Ancho (cm)','20'],['Alto (cm)','10']])await two.getByLabel(label,{exact:true}).fill(value);
  // Validation keeps the destination and both parcels available for correction.
  await page.getByLabel('Nombre',{exact:true}).fill('');
  await page.getByRole('button',{name:'Calcular tarifa',exact:true}).click();
  await page.locator('.validation-errors').waitFor();
  assert.equal(await two.getByLabel('Código',{exact:true}).inputValue(),'CU09-UI-DOS');
  assert.equal(await page.getByLabel('Dirección',{exact:true}).inputValue(),'Primera 123');
  await page.getByLabel('Nombre',{exact:true}).fill('Destinatario CU09 navegador');
  await page.getByRole('button',{name:'Calcular tarifa',exact:true}).click();await page.getByRole('button',{name:'Confirmar envío',exact:true}).waitFor();
  assert.match(await page.locator('.nr-price-review').innerText(),/121[,.]40/);
  await page.screenshot({path:path.join(out,'alta-cotizada-1440.png'),fullPage:true});await page.setViewportSize({width:320,height:1000});
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
  await page.screenshot({path:path.join(out,'alta-cotizada-320.png'),fullPage:true});
  await page.getByRole('button',{name:'Confirmar envío',exact:true}).click();await page.getByRole('status').filter({hasText:'Tarifa aplicada:'}).waitFor();
  await page.getByRole('link',{name:'Continuar: revisar y confirmar ubicación',exact:true}).click();await page.getByRole('heading',{name:'Primera 123',exact:true}).waitFor();
  await page.reload();await page.getByRole('button',{name:'Mapa simulado: seleccionar punto'}).waitFor();
  await page.getByRole('button',{name:'Mapa simulado: seleccionar punto'}).click();await page.locator('#destino-revisado').check();await page.getByRole('button',{name:'Confirmar nueva ubicación',exact:true}).click();
  await page.getByText('Ubicación confirmada. El punto quedó listo para su planificación.',{exact:true}).waitFor();
  await page.reload();await page.getByRole('heading',{name:'Primera 123',exact:true}).waitFor();await page.screenshot({path:path.join(out,'alta-reabierta-320.png'),fullPage:true});
  assert.deepEqual(external,[]);assert.deepEqual(errors,[]);console.log('CU09 Chromium: búsqueda previa, selección, ajuste, cotización, dos bultos, guardado y reapertura; sin consumo externo.');
 }catch(error){await page.screenshot({path:path.join(out,"alta-fallo.png"),fullPage:true});console.error(await page.locator(".validation-errors,.alert-danger,.validation-message").allTextContents());console.error(await page.locator(".nr-creation-form").evaluate(f=>[...f.querySelectorAll("input,select")].map(i=>({name:i.getAttribute("id"),value:i.value,invalid:i.validationMessage}))));throw error;}finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
