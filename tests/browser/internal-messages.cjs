const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path');
const {chromium}=require(process.env.NEXORUTA_PLAYWRIGHT_MODULE);
(async()=>{
 const base=process.env.INTERNAL_MESSAGES_URL,out=process.env.NEXORUTA_INTERNAL_MESSAGES_UI_DIR;
 assert.match(base,/^http:\/\/(127\.0\.0\.1|localhost):\d+\/$/);await fs.mkdir(out,{recursive:true});
 const browser=await chromium.launch({headless:true});const page=await browser.newPage({viewport:{width:1440,height:1000},reducedMotion:'reduce'});
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',async r=>{const u=new URL(r.request().url());if(u.origin!==new URL(base).origin)return r.abort();if(r.request().method()==='POST'&&u.pathname==='/mensajes-internos')await new Promise(resolve=>setTimeout(resolve,400));return r.continue();});
 try{
  await page.goto(base+'ingresar');await page.locator('#Email').fill('user@example.test');await page.locator('#Password').fill('correct-password');
  await Promise.all([page.waitForNavigation(),page.getByRole('button',{name:'Ingresar',exact:true}).click()]);
  await page.goto(base+'mensajes-internos');await page.getByRole('heading',{name:'Mensajes internos',exact:true}).waitFor();
  await page.screenshot({path:path.join(out,'desktop.png'),fullPage:true});
  await page.setViewportSize({width:320,height:900});
  const summary=page.locator('summary');await summary.focus();await page.keyboard.press('Enter');
  const reason=page.getByLabel('Motivo del reintento');await reason.waitFor();await reason.fill('Servicio recuperado en prueba sintética');
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth),true);
  await page.screenshot({path:path.join(out,'mobile.png'),fullPage:true});
  await reason.fill('');await page.locator('main form').evaluate(f=>f.noValidate=true);
  await Promise.all([page.waitForNavigation(),page.getByRole('button',{name:'Solicitar reintento',exact:true}).click()]);
  const invalid=page.getByLabel('Motivo del reintento (obligatorio)',{exact:true});assert.equal(await invalid.getAttribute('aria-invalid'),'true');
  await page.screenshot({path:path.join(out,'validation.png'),fullPage:true});
  await invalid.fill('Servicio recuperado en prueba sintética');
  await invalid.focus();await page.keyboard.press('Tab');assert.equal(await page.evaluate(()=>document.activeElement.tagName),'BUTTON');
  // Hold the browser's submit default once to inspect the actual single-submit rendering.
  await page.locator('main form').evaluate(f=>f.addEventListener('submit',e=>e.preventDefault(),{once:true}));
  await page.keyboard.press('Enter');
  await page.getByRole('button',{name:'Solicitando reintento…',exact:true}).waitFor();
  assert.equal(await page.locator('main form').getAttribute('aria-busy'),'true');
  await page.screenshot({path:path.join(out,'pending.png'),fullPage:true});
  await Promise.all([page.waitForNavigation(),page.locator('main form').evaluate(f=>f.submit())]);
  await page.getByRole('status').filter({hasText:'Reintento solicitado'}).waitFor();
  assert.match(await page.getByRole('main').innerText(),/No hay publicaciones fallidas/);
  await page.screenshot({path:path.join(out,'empty.png'),fullPage:true});
  assert.deepEqual(errors,[]);
 }catch(error){console.error(JSON.stringify({errors,forms:await page.locator('main form').evaluateAll(forms=>forms.map(f=>({busy:f.getAttribute('aria-busy'),submitted:f.dataset.submitted,buttons:[...f.querySelectorAll('button')].map(b=>({text:b.textContent,loading:b.dataset.loadingText,disabled:b.disabled}))}))),scripts:await page.locator('script[src]').evaluateAll(s=>s.map(x=>x.src))}));throw error;}finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
