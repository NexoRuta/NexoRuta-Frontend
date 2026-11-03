const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path');
const {chromium}=require(process.env.NEXORUTA_PLAYWRIGHT_MODULE);
(async()=>{
 const base=process.env.RECEIPT_E2E_URL,out=process.env.NEXORUTA_RECEIPT_E2E_DIR,parcels=JSON.parse(process.env.RECEIPT_E2E_PARCELS);
 assert.match(base,/^http:\/\/127\.0\.0\.1:\d+\/$/);await fs.mkdir(out,{recursive:true});
 const browser=await chromium.launch({headless:true});const context=await browser.newContext({viewport:{width:1440,height:1000}});const page=await context.newPage();
 const AxeBuilder=require(path.join(path.dirname(process.env.NEXORUTA_PLAYWRIGHT_MODULE),'@axe-core/playwright')).default;
 const report={browser:browser.version(),checks:[],errors:[],posts:0,axe:[]};page.on('pageerror',e=>report.errors.push(e.message));
 await page.route('**/*',async route=>{
  const request=route.request(),u=new URL(request.url());if(u.origin!==new URL(base).origin)return route.abort();
  if(u.pathname==='/cu10-etiqueta.png')return route.fulfill({path:path.join(out,'etiqueta.png'),contentType:'image/png'});
  if(request.method()==='POST'&&u.pathname==='/deposito/recepcion'){report.posts++;}
  return route.continue();
 });
 await page.addInitScript(()=>{
  async function frame(){const img=new Image();img.src='/cu10-etiqueta.png';await img.decode();const canvas=document.createElement('canvas');canvas.width=img.width;canvas.height=img.height;canvas.getContext('2d').drawImage(img,0,0);window.__receiptCanvas=canvas;const stream=canvas.captureStream(10);for(const track of stream.getTracks()){const stop=track.stop.bind(track);track.stop=()=>{localStorage.setItem('receiptTrackStops',String(Number(localStorage.getItem('receiptTrackStops')||0)+1));stop();};}return stream;}
  Object.defineProperty(navigator.mediaDevices,'getUserMedia',{configurable:true,value:async()=>{const mode=localStorage.getItem('receiptCameraMode')||'denied';if(mode==='denied')throw new DOMException('controlled permission refusal','NotAllowedError');if(mode==='missing')throw new DOMException('controlled missing device','NotFoundError');if(mode==='pending')return new Promise(resolve=>{window.__resolveReceiptCamera=async()=>resolve(await frame());});return frame();}});
 });
 const submit=async button=>Promise.all([page.waitForNavigation(),button.click()]);
 const screenshot=async name=>page.getByRole('main').screenshot({path:path.join(out,name+'.png')});
 try{
  await page.goto(base+'ingresar');await page.locator('#Email').fill(process.env.RECEIPT_E2E_EMAIL);await page.locator('#Password').fill(process.env.RECEIPT_E2E_PASSWORD);await submit(page.getByRole('button',{name:'Ingresar',exact:true}));
  await page.goto(base+'deposito');await submit(page.getByRole('link',{name:'Escanear y cotejar bultos',exact:true}));
  const input=page.locator('#codigo-bulto');await input.focus();await input.fill('UNKNOWN-SYNTHETIC');await Promise.all([page.waitForNavigation(),page.keyboard.press('Enter')]);await page.getByRole('alert').filter({hasText:'No encontramos ese bulto'}).waitFor();assert.equal(await input.inputValue(),'UNKNOWN-SYNTHETIC');assert.ok(await page.locator('[data-receipt-error]').evaluate(e=>document.activeElement===e));
  await input.focus();await page.keyboard.type('BROWSER-RECEIPT-1');await Promise.all([page.waitForNavigation(),input.press('Enter')]);await page.getByRole('heading',{name:'BROWSER-RECEIPT-1',exact:true}).waitFor();assert.equal(report.posts,0);
  const decoded=await page.evaluate(async()=>String((await new ZXingBrowser.BrowserQRCodeReader().decodeFromImageUrl('/cu10-etiqueta.png')).getText()));assert.equal(decoded,parcels[0]);report.checks.push('Real QR decoding of PDF label rendered by pdftoppm; UUID equals persisted parcel');
  await page.locator('#activar-camara').click();await page.getByRole('status').filter({hasText:'Permiso de cámara denegado'}).waitFor();assert.ok(await input.evaluate(e=>document.activeElement===e));
  await page.evaluate(()=>localStorage.setItem('receiptCameraMode','missing'));await page.locator('#activar-camara').click();await page.getByRole('status').filter({hasText:'No encontramos una cámara'}).waitFor();
  await page.evaluate(()=>localStorage.setItem('receiptCameraMode','pending'));await page.locator('#activar-camara').click();await page.getByRole('status').filter({hasText:'Esperando permiso'}).waitFor();await page.locator('#detener-camara').click();await page.evaluate(()=>window.__resolveReceiptCamera());await page.waitForFunction(()=>Number(localStorage.getItem('receiptTrackStops'))>0);assert.ok(await page.locator('#video-recepcion').isHidden());
  await page.evaluate(()=>localStorage.setItem('receiptCameraMode','allowed'));await Promise.all([page.waitForNavigation(),page.locator('#activar-camara').click()]);assert.match(page.url(),new RegExp(parcels[0]));assert.equal(report.posts,0);assert.ok(Number(await page.evaluate(()=>localStorage.getItem('receiptTrackStops')))>=2);report.checks.push('Controlled permission/missing-device/pending-cancel errors; real decoder consumes label frames via canvas MediaStream; tracks stopped; scan performs GET only');
  const formAxe=await new AxeBuilder({page}).include('.nr-reception').analyze();assert.deepEqual(formAxe.violations.map(v=>v.id),[]);
  for(const width of [1440,375,320]){await page.setViewportSize({width,height:1000});assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));await screenshot('cotejo-'+width);}
  const receipt=()=>page.locator('#cotejar-bulto');await receipt().locator('[name="Cotejo.PesoObservado"]').fill('700');await page.locator('#cotejo-discrepancia').check();await receipt().locator('textarea').fill('Peso observado distinto; embalaje dañado.');
  await receipt().evaluate(f=>f.addEventListener('submit',e=>e.preventDefault(),{once:true}));
  await receipt().getByRole('button',{name:'Confirmar recepción y cotejo',exact:true}).click();
  assert.equal(await receipt().getAttribute('aria-busy'),'true');assert.ok(await receipt().getByRole('button',{name:'Registrando…'}).isDisabled());
  await receipt().evaluate(f=>f.requestSubmit());assert.equal(report.posts,0);await screenshot('guardando-320');
  await Promise.all([page.waitForNavigation(),receipt().evaluate(f=>HTMLFormElement.prototype.submit.call(f))]);assert.equal(report.posts,1);
  await page.getByRole('heading',{name:'Discrepancia registrada',exact:true}).waitFor();assert.match(await page.getByRole('main').innerText(),/Discrepancia pendiente/);assert.match(await page.getByRole('main').innerText(),/Admitido/);await screenshot('discrepancia-320');
  await submit(page.getByRole('link',{name:'BROWSER-RECEIPT-2',exact:true}));
  const lostCommand=await receipt().locator('[name="Cotejo.ComandoId"]').inputValue();const lostWeight=await receipt().locator('[name="Cotejo.PesoObservado"]').inputValue();
  const apiBase=process.env.RECEIPT_E2E_API;assert.match(apiBase,/^http:\/\/(127\.0\.0\.1|localhost):\d+\/$/);
  assert.equal((await fetch(apiBase+'__cu10_test/fail-receipt-response',{method:'POST'})).status,204);
  await submit(receipt().getByRole('button',{name:'Confirmar recepción y cotejo',exact:true}));
  await page.getByRole('heading',{name:'Reintentar cotejo',exact:true}).waitFor();assert.equal(await receipt().locator('[name="Cotejo.ComandoId"]').inputValue(),lostCommand);assert.equal(await receipt().locator('[name="Cotejo.PesoObservado"]').inputValue(),lostWeight);await screenshot('reintento-320');
  await submit(receipt().getByRole('button',{name:'Confirmar recepción y cotejo',exact:true}));await page.getByRole('status').filter({hasText:'Este cotejo ya estaba registrado'}).waitFor();assert.match(await page.getByRole('main').innerText(),/Admitido/);
  report.checks.push('Real API write committed, response replaced by controlled 503 and next query failed; Backoffice retained payload/command and retry recovered one persisted fact');
  await submit(page.getByRole('link',{name:'BROWSER-RECEIPT-1',exact:true}));const command=await receipt().locator('[name="Cotejo.ComandoId"]').inputValue();await receipt().locator('[name="Cotejo.PesoObservado"]').fill('600');await submit(receipt().getByRole('button',{name:'Confirmar recepción y cotejo',exact:true}));await page.getByRole('alert').filter({hasText:'medidas observadas difieren'}).waitFor();assert.equal(await receipt().locator('[name="Cotejo.PesoObservado"]').inputValue(),'600');assert.equal(await receipt().locator('[name="Cotejo.ComandoId"]').inputValue(),command);assert.ok(await page.locator('[data-receipt-error]').evaluate(e=>document.activeElement===e));
  await receipt().locator('[name="Cotejo.PesoObservado"]').fill('500');await receipt().locator('textarea').fill('Revisado después de corregir el embalaje.');await submit(receipt().getByRole('button',{name:'Confirmar recepción y cotejo',exact:true}));await page.getByRole('status').filter({hasText:'Recepción completa'}).waitFor();assert.match(await page.getByRole('main').innerText(),/2 de 2 con cotejo conforme/);assert.equal(await receipt().count(),0);assert.equal(report.posts,5);
  const axe=await new AxeBuilder({page}).include('.nr-reception').analyze();report.axe=axe.violations.map(v=>({id:v.id,impact:v.impact,nodes:v.nodes.length}));assert.deepEqual(report.axe,[]);
  await screenshot('completa-320');await page.evaluate(()=>document.documentElement.style.fontSize='200%');assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));await screenshot('texto-200-320');
  await input.focus();await page.keyboard.type(parcels[1]);await Promise.all([page.waitForNavigation(),page.keyboard.press('Enter')]);await page.getByRole('heading',{name:'BROWSER-RECEIPT-2',exact:true}).waitFor();assert.equal(report.posts,5);report.checks.push('Keyboard scanner text plus Enter replaces previous code on focus; repeated scan does not POST a receipt');
  report.checks.push('Desktop 1440 and mobile 375/320, keyboard Enter/focus, observed measures/server validation preserved, busy/double submit, discrepancy/incomplete/recheck/complete persisted, axe scoped scan and 200% text reflow');assert.deepEqual(report.errors,[]);
 }finally{await fs.writeFile(path.join(out,'browser-report.json'),JSON.stringify(report,null,2));await browser.close();}
})().catch(e=>{console.error(e.stack||e.message);process.exitCode=1;});
