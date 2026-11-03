const assert=require('node:assert/strict'),fs=require('node:fs/promises'),http=require('node:http'),path=require('node:path'),crypto=require('node:crypto');
const {chromium}=require(process.env.NEXORUTA_PLAYWRIGHT_MODULE);
(async()=>{
 const out=process.env.NEXORUTA_SESSION_E2E_DIR,ports=JSON.parse(process.env.SESSION_PORTALS),emails=JSON.parse(process.env.SESSION_EMAILS);
 const report={checks:[],timeline:[],errors:[]};const event=(name,data={})=>report.timeline.push({atUtc:new Date().toISOString(),name,...data});
 async function control(action,portal){const id=crypto.randomUUID();const file=path.join(out,'control.json');await fs.writeFile(file+'.tmp',JSON.stringify({id,action,portal}));await fs.rename(file+'.tmp',file);for(let n=0;n<400;n++){try{if(JSON.parse(await fs.readFile(path.join(out,'reply.json'),'utf8')).id===id){event(action,{portal});return;}}catch{}await new Promise(r=>setTimeout(r,50));}throw Error('Local controller timeout: '+action);}
 const proxies=[];async function proxy(port){
  const state={gate:null};const server=http.createServer((req,res)=>{const upstream=http.request({hostname:'127.0.0.1',port,path:req.url,method:req.method,headers:req.headers},response=>{
   const location=response.headers.location||'';
   if(state.gate&&req.method==='GET'&&location.includes('/ingresar?expirada=true')){const gate=state.gate;state.gate=null;const chunks=[];response.on('data',c=>chunks.push(c));response.on('end',()=>{event('late_401_redirect_buffered',{deletesSessionCookie:(response.headers['set-cookie']||[]).some(v=>/NexoRuta\.(Backoffice|Commerce)=;/.test(v))});gate.captured();gate.release=()=>{res.writeHead(response.statusCode,response.headers);res.end(Buffer.concat(chunks));};});}
   else{res.writeHead(response.statusCode,response.headers);response.pipe(res);}
  });upstream.on('error',()=>{res.writeHead(503);res.end('Test proxy upstream unavailable');});req.pipe(upstream);});await new Promise(r=>server.listen(0,'127.0.0.1',r));state.base='http://127.0.0.1:'+server.address().port+'/';state.close=()=>new Promise(r=>server.close(r));proxies.push(state);return state;
 }
 const browser=await chromium.launch({headless:true});
 try{
  for(const portal of (process.env.NEXORUTA_SESSION_ONLY?[process.env.NEXORUTA_SESSION_ONLY]:['backoffice','commerce'])){
   await control('restart_api',portal);const p=await proxy(ports[portal]),context=await browser.newContext();const a=await context.newPage(),b=await context.newPage();for(const page of[a,b])page.on('pageerror',e=>report.errors.push(e.message));
   const target=portal==='backoffice'?'deposito/recepcion':'usuarios';
   const login=async page=>{await page.goto(p.base+'ingresar?ReturnUrl='+encodeURIComponent('/'+target));await page.locator('#Email').fill(emails[portal]);await page.locator('#Password').fill(process.env.SESSION_PASSWORD);await Promise.all([page.waitForNavigation(),page.getByRole('button',{name:'Ingresar',exact:true}).click()]);};
   await login(a);await a.goto(p.base+target);assert.equal(new URL(a.url()).pathname,'/'+target);event('initial_authenticated',{portal});
   await control('revoke',portal);let captured;const ready=new Promise(r=>captured=r);const gate={captured};p.gate=gate;const waiting=a.goto(p.base+target);await ready;
   await login(b);event('new_login_completed',{portal});assert.ok((await context.cookies()).some(c=>c.name==='NexoRuta.'+(portal==='backoffice'?'Backoffice':'Commerce')));
   gate.release();await waiting;await b.goto(p.base+target);event('after_late_response',{portal,path:new URL(b.url()).pathname});
   assert.equal(new URL(b.url()).pathname,'/'+target,'A delayed 401 erased the newer sign-in cookie');
   assert.equal(new URL(a.url()).pathname,'/'+target,'Late recovery did not preserve destination');report.checks.push(portal+': stale 401 does not delete newer cookie; latest session revalidated; destination preserved');
   if(process.env.NEXORUTA_SESSION_RACE_ONLY!=='1'){
    const cookieName='NexoRuta.'+(portal==='backoffice'?'Backoffice':'Commerce');
    const cookie=async()=> (await context.cookies()).find(c=>c.name===cookieName)?.value;
    const current=await cookie();
    const statuses=await b.evaluate(async target=>Promise.all(Array.from({length:12},()=>fetch('/'+target,{redirect:'manual'}).then(r=>r.status))),target);
    assert.ok(statuses.every(s=>s===200));assert.ok(await cookie()===current);report.checks.push(portal+': 12 simultaneous authenticated GETs, no cookie replacement');
    await control('deny',portal);await b.goto(p.base+target);assert.equal(new URL(b.url()).pathname,'/acceso-denegado');assert.ok(await cookie()===current);
    await control('allow',portal);await b.goto(p.base+target);assert.equal(new URL(b.url()).pathname,'/'+target);report.checks.push(portal+': permission denial remains 403/access denied; same session recovers');
    await control('offline',portal);await b.goto(p.base+target+'?sensitive=do-not-preserve');assert.equal(new URL(b.url()).pathname,'/api-indisponible');
    assert.ok(!b.url().includes('do-not-preserve'));assert.ok(await cookie()===current);
    await control('online',portal);await b.getByRole('link',{name:'Volver a intentar',exact:true}).click();await b.waitForURL(p.base+target);report.checks.push(portal+': API 503 distinguished from invalid session; path-only manual retry');
    await control('stop_api',portal);await b.goto(p.base+target);assert.equal(new URL(b.url()).pathname,'/api-indisponible');assert.ok(await cookie()===current);
    await control('restart_api',portal);await b.getByRole('link',{name:'Volver a intentar',exact:true}).click();await b.waitForURL(p.base+target);report.checks.push(portal+': real connection refusal preserves cookie; explicit retry after API restart');
    await control('restart_portal',portal);await b.goto(p.base+target);assert.equal(new URL(b.url()).pathname,'/'+target);assert.ok(await cookie()===current);report.checks.push(portal+': API and portal restart preserve session with persistent key ring');
    // Rejection of this same sign-in stops on the form: no whoami/permissions redirect loop.
    await control('revoke',portal);await b.goto(p.base+target);assert.equal(new URL(b.url()).pathname,'/ingresar');assert.match(await b.locator('[role=alert]').innerText(),/sesión ya no es válida/);assert.ok(await cookie()===current);
    await b.reload();assert.equal(new URL(b.url()).pathname,'/ingresar');
    await b.setViewportSize({width:320,height:740});await b.screenshot({path:path.join(out,portal+'-expired-320.png')});assert.ok(await b.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));await b.setViewportSize({width:1280,height:900});report.checks.push(portal+': invalid current session shows explicit sign-in without cookie deletion or loop');
    await control('restart_api',portal);await control('revoke_after_login',portal);await login(b);assert.equal(new URL(b.url()).pathname,'/ingresar');
    assert.match(await b.locator('[role=alert]').innerText(),/validar la sesión recién iniciada/);report.checks.push(portal+': login 200 followed by whoami 401 is not reported as wrong password');
    await login(b);await b.goto(p.base+target);assert.equal(new URL(b.url()).pathname,'/'+target);
    // Explicit logout remains a CSRF-protected POST. GET /salir must retain the cookie.
    const fresh=await cookie();await b.evaluate(()=>fetch('/salir',{redirect:'manual'}));assert.ok(await cookie()===fresh);
    await b.goto(p.base+target);
    await control('revoke',portal);let logoutCaptured;const logoutReady=new Promise(r=>logoutCaptured=r);const logoutGate={captured:logoutCaptured};p.gate=logoutGate;
    const lateAfterLogout=a.goto(p.base+target);await logoutReady;
    const logout=await b.evaluate(async()=>{
      const form=document.querySelector('form[action="/salir"]');if(!form)throw Error('Explicit logout form missing');
      const noCsrf=await fetch('/salir',{method:'POST',body:new URLSearchParams(),redirect:'manual'});
      const result=await fetch('/salir',{method:'POST',body:new URLSearchParams(new FormData(form)),redirect:'manual'});return {noCsrf:noCsrf.status,withCsrf:result.status};
    });assert.equal(logout.noCsrf,400);assert.ok(logout.withCsrf===0||logout.withCsrf===302);assert.ok(!(await cookie()));logoutGate.release();await lateAfterLogout;assert.ok(!(await cookie()));assert.equal(new URL(a.url()).pathname,'/ingresar');report.checks.push(portal+': delayed rejection after explicit logout never restores a session');await b.goto(p.base+target);assert.equal(new URL(b.url()).pathname,'/ingresar');assert.ok(!new URL(b.url()).searchParams.has('expirada'));report.checks.push(portal+': logout only with antiforgery POST; anonymous session is not labelled expired');
    // Form usability at 320px, keyboard focus, zoom-equivalent width and safe error recovery.
    await b.setViewportSize({width:320,height:740});await b.goto(p.base+'ingresar');await b.locator('#Email').focus();await b.keyboard.type(emails[portal]);assert.ok(await b.locator('#Email').evaluate(e=>e===document.activeElement));
    assert.ok(await b.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));await b.screenshot({path:path.join(out,portal+'-login-320.png')});
    const {default:AxeBuilder}=require(path.join(path.dirname(process.env.NEXORUTA_PLAYWRIGHT_MODULE),'@axe-core/playwright'));const axe=await new AxeBuilder({page:b}).analyze();report.accessibility??=[];report.accessibility.push({portal,violations:axe.violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)}))});assert.deepEqual(axe.violations.map(v=>v.id),[]);
    await b.setViewportSize({width:1280,height:900});await b.screenshot({path:path.join(out,portal+'-login-desktop.png')});report.checks.push(portal+': login 320px/desktop, keyboard focus, no horizontal overflow');
   }
   await context.close();
  }
  assert.deepEqual(report.errors,[]);
 }finally{await fs.writeFile(path.join(out,'browser-report.json'),JSON.stringify(report,null,2));await browser.close();for(const p of proxies)await p.close();}
})().catch(e=>{console.error(e.stack);process.exitCode=1;});
