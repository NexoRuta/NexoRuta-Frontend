/** Real Chromium + Kestrel + owned ephemeral PostgreSQL; no npm dependencies.
 * Run after Release builds: node scripts/check-reactivation-browser.mjs (from frontend).
 * Browser executable may be selected with NEXORUTA_BROWSER. Never uses Compose or persistent DB settings.
 * ponytail: This duplicates the small existing browser harness to keep the onboarding regression isolated;
 * extract a shared launcher only if another end-to-end journey needs the same setup.
 */
import { spawn, execFileSync } from 'node:child_process';
import { mkdtempSync, openSync, closeSync, writeFileSync, readFileSync, existsSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomBytes, randomUUID } from 'node:crypto';
import { createServer } from 'node:net';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const scratch = mkdtempSync(join(tmpdir(), 'nexoruta-reactivation-'));
const name = `nexoruta-reactivation-${randomUUID()}`;
const processes = [], logs = [];
let ownedContainer = false, browserSocket;
const assert = (value, message) => { if (!value) throw new Error(message); };
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
const port = () => new Promise(resolve => { const server = createServer(); server.listen(0, '127.0.0.1', () => { const n = server.address().port; server.close(() => resolve(n)); }); });
function start(command, args, env, cwd, label) {
    const fd = openSync(join(scratch, `${label}.log`), 'w'); logs.push(fd);
    const process = spawn(command, args, { env: { ...globalThis.process.env, ...env }, cwd, stdio: ['ignore', fd, fd], windowsHide: true });
    processes.push(process); return process;
}
async function waitFor(url) {
    for (let i = 0; i < 150; i++) {
        try { if ((await fetch(url)).ok) return; } catch { /* Startup is bounded below. */ }
        await delay(200);
    }
    throw new Error(`Host did not become ready: ${new URL(url).pathname}`);
}
async function connect(url) {
    const ws = new WebSocket(url), pending = new Map(); let sequence = 0;
    await new Promise((resolve, reject) => { ws.addEventListener('open', resolve, { once: true }); ws.addEventListener('error', reject, { once: true }); });
    ws.addEventListener('message', event => { const response = JSON.parse(event.data); const promise = pending.get(response.id); if (promise) { pending.delete(response.id); response.error ? promise.reject(new Error(JSON.stringify(response.error))) : promise.resolve(response.result); } });
    return { close: () => ws.close(), send: (method, params = {}) => new Promise((resolve, reject) => { const id = ++sequence; pending.set(id, { resolve, reject }); ws.send(JSON.stringify({ id, method, params })); }) };
}
try {
    const dbPassword = randomBytes(24).toString('hex');
    execFileSync('docker', ['run', '--rm', '-d', '--name', name, '-e', `POSTGRES_PASSWORD=${dbPassword}`, '-e', 'POSTGRES_USER=onboarding', '-e', 'POSTGRES_DB=onboarding', '-p', '127.0.0.1::5432', 'postgres:18-alpine'], { stdio: 'pipe' });
    ownedContainer = true;
    const inspected = JSON.parse(execFileSync('docker', ['inspect', name], { encoding: 'utf8' }))[0];
    assert(inspected.Name === `/${name}` && inspected.Config.Image === 'postgres:18-alpine', 'Unexpected owned container identity');
    const dbPort = inspected.NetworkSettings.Ports['5432/tcp'][0].HostPort;
    for (let i = 0; ; i++) {
        try { execFileSync('docker', ['exec', name, 'pg_isready', '-U', 'onboarding'], { stdio: 'pipe' }); break; }
        catch { if (i === 100) throw new Error('Owned PostgreSQL did not start'); await delay(200); }
    }
    const apiPort = await port(), commercePort = await port(), operatorPort = await port(), adminPort = await port();
    const base = `http://127.0.0.1:${commercePort}`, operator = `http://127.0.0.1:${operatorPort}`, admin = `http://127.0.0.1:${adminPort}`;
    const platformEmail = `platform-${randomUUID()}@browser.example.test`;
    const platformPassword = randomBytes(24).toString('hex');
    start('dotnet', ['bin/Release/net10.0/NexoRuta.Api.dll'], {
        ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: `http://127.0.0.1:${apiPort}`,
        ConnectionStrings__Postgres: `Host=127.0.0.1;Port=${dbPort};Database=onboarding;Username=onboarding;Password=${dbPassword}`,
        DatabaseInitialization__ApplyMigrations: 'true', DatabaseInitialization__SeedData: 'false', PlatformInitialization__Enabled: 'true',
        PlatformInitialization__Email: platformEmail, PlatformInitialization__Password: platformPassword,
        OpenTelemetry__Enabled: 'false', Jwt__Issuer: 'browser-check', Jwt__Audience: 'browser-check', Jwt__SigningKeyBase64: randomBytes(32).toString('base64')
    }, resolve(root, '../backend/src/NexoRuta.Api'), 'api');
    await waitFor(`http://127.0.0.1:${apiPort}/health/ready`);
    for (const [app, appPort] of [['Commerce', commercePort], ['Backoffice', operatorPort]]) {
        start('dotnet', [`bin/Release/net10.0/NexoRuta.${app}.dll`], {
            ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: `http://127.0.0.1:${appPort}`,
            Api__BaseAddress: `http://127.0.0.1:${apiPort}/`, Portales__BackofficeLoginUrl: `${operator}/ingresar`
        }, join(root, `src/NexoRuta.${app}`), app);
        await waitFor(`http://127.0.0.1:${appPort}/health/ready`);
    }
    start('dotnet', ['bin/Release/net10.0/NexoRuta.Admin.dll'], {
        ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: admin,
        Api__BaseAddress: `http://127.0.0.1:${apiPort}/`
    }, join(root, 'src/NexoRuta.Admin'), 'admin');
    await waitFor(`http://127.0.0.1:${adminPort}/health/ready`);
    const executable = process.env.NEXORUTA_BROWSER ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
    assert(existsSync(executable), 'Set NEXORUTA_BROWSER to an installed Chromium executable');
    const profile = join(scratch, 'profile');
    start(executable, ['--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check', '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank'], {}, root, 'browser');
    let debugging;
    for (let i = 0; i < 100; i++) { if (existsSync(join(profile, 'DevToolsActivePort'))) { debugging = readFileSync(join(profile, 'DevToolsActivePort'), 'utf8').split(/\r?\n/)[0]; break; } await delay(200); }
    assert(debugging, 'Browser debugging endpoint unavailable');
    const targets = await (await fetch(`http://127.0.0.1:${debugging}/json/list`)).json();
    browserSocket = await connect(targets.find(x => x.type === 'page').webSocketDebuggerUrl);
    const cdp = browserSocket;
    const evaluate = async expression => {
        const result = await cdp.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
        assert(!result.exceptionDetails, 'Browser JavaScript evaluation failed'); return result.result?.value;
    };
    async function until(expression) {
        for (let i = 0; i < 100; i++) { try { if (await evaluate(expression)) return; } catch { /* Navigation replaces execution contexts. */ } await delay(100); }
        throw new Error(`Browser condition failed: ${expression}`);
    }
    async function navigate(url) { await cdp.send('Page.navigate', { url }); await until(`location.href === ${JSON.stringify(url)} && document.readyState === 'complete'`); }
    async function submit(fields) {
        await evaluate(`(() => { for (const [name, value] of Object.entries(${JSON.stringify(fields)})) document.querySelector('[name="' + name + '"]').value = value; const form = document.querySelector('form[data-single-submit]'); form.requestSubmit(); form.requestSubmit(); })()`);
    }
    const password = randomBytes(18).toString('hex');
    const api = `http://127.0.0.1:${apiPort}`;
    async function call(path, body, token) {
        const response = await fetch(`${api}${path}`, { method: 'POST', headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) }, body: JSON.stringify(body) });
        if (!response.ok) throw new Error(`${path}: ${response.status} ${await response.text()}`);
        return response.json();
    }
    const platformToken = (await call('/api/auth/login', { email: platformEmail, password: platformPassword })).accessToken;
    if (process.argv.includes('--email-edit') || process.argv.includes('--email-edit-operator')) {
        const kind = process.argv.includes('--email-edit-operator') ? 'operador' : 'comercio';
        const portal = kind === 'operador' ? operator : base;
        const email = `owner-${randomUUID()}@browser.example.test`;
        const employeeEmail = `employee-${randomUUID()}@browser.example.test`;
        const changedEmail = `changed-${randomUUID()}@browser.example.test`;
        await call('/api/auth/register', { email, password });
        const identityToken = (await call('/api/auth/login', { email, password })).accessToken;
        await call(`/api/alta-propia/${kind === 'operador' ? 'operadores' : 'comercios'}`, { nombre: 'Organización efímera de prueba' }, identityToken);
        const ownerToken = (await call('/api/auth/login', { email, password })).accessToken;
        const role = await call('/api/roles', { codigo: `empleado_${randomUUID().replaceAll('-', '')}`, nombre: 'Empleado' }, ownerToken);
        const grant = await fetch(`${api}/api/roles/${role.id}/permisos`, {
            method: 'PUT', headers: { Authorization: `Bearer ${ownerToken}`, 'Content-Type': 'application/json' },
            body: JSON.stringify({ permisos: ['envios.consultar'] })
        });
        assert(grant.ok, `Could not grant employee permission: ${grant.status}`);
        await navigate(`${portal}/ingresar`);
        await submit({ Email: email, Password: password });
        await until("location.pathname === '/' || location.pathname === '/envios'");
        await navigate(`${portal}/usuarios`);
        await until("document.querySelector('form[action*=Crear]') !== null");
        await evaluate(`(() => { const form = document.querySelector('form[action*=Crear]'); form.querySelector('[name=Email]').value=${JSON.stringify(employeeEmail)}; form.querySelector('[name=Password]').value=${JSON.stringify(password)}; form.querySelector('[name=RolId]').value=${JSON.stringify(role.id)}; form.requestSubmit(); })()`);
        await until(`document.body.innerText.includes(${JSON.stringify(employeeEmail)})`);
        const employeeToken = (await call('/api/auth/login', { email: employeeEmail, password })).accessToken;
        await evaluate(`(() => { const form = [...document.querySelectorAll('form[action*=Email]')].find(x => x.closest('tr').innerText.includes(${JSON.stringify(employeeEmail)})); if (!form) throw new Error('Email edit form not rendered'); form.querySelector('[name=nuevoEmail]').value=${JSON.stringify(changedEmail)}; form.requestSubmit(); form.requestSubmit(); })()`);
        await until(`document.body.innerText.includes(${JSON.stringify(changedEmail)})`);
        const revoked = await fetch(`${api}/api/usuarios/actual`, { headers: { Authorization: `Bearer ${employeeToken}` } });
        assert(revoked.status === 401, `Old employee session remained valid: ${revoked.status}`);
        await evaluate("document.querySelector('form[action*=" + JSON.stringify('salir') + " i]')?.requestSubmit()");
        await until("location.pathname !== '/usuarios'");
        await delay(60_000); // Login limiter is fixed-window per IP in the ephemeral API.
        await navigate(`${portal}/ingresar`);
        await submit({ Email: changedEmail, Password: password });
        try { await until("location.pathname !== '/ingresar' && !document.body.innerText.includes('Demasiadas solicitudes')"); }
        catch (error) { console.log('After changed-email login:', await evaluate('location.href + "\\n" + document.body.innerText')); throw error; }
        assert(!(await evaluate('document.documentElement.outerHTML')).includes(password), 'Password leaked into HTML');
        console.log(`PASS: real ${kind} browser created employee, edited email, revoked old session and logged in with new email.`);
    } else {
    for (const kind of ['comercio', 'operador']) {
        if (kind === 'operador') await delay(60_000); // Existing login limiter is fixed-window per IP.
        await cdp.send('Network.clearBrowserCookies');
        const email = `${kind}-${randomUUID()}@browser.example.test`;
        await call('/api/auth/register', { email, password });
        const identityToken = (await call('/api/auth/login', { email, password })).accessToken;
        const created = await call(`/api/alta-propia/${kind === 'comercio' ? 'comercios' : 'operadores'}`, { nombre: `Reactivación ${kind}` }, identityToken);
        const organizationId = created.organizacion.id;
        const route = kind === 'comercio' ? 'comercios' : 'operadores';
        const hold = await fetch(`${api}/api/admin/${route}/${organizationId}/estado`, {
            method: 'PUT', headers: { Authorization: `Bearer ${platformToken}`, 'Content-Type': 'application/json' },
            body: JSON.stringify({ estado: 'Suspendida', motivo: 'Pausa para prueba efímera', version: 0 })
        });
        assert(hold.ok, `Could not suspend ephemeral ${kind}: ${hold.status}`);

        const portal = kind === 'comercio' ? base : operator;
        await navigate(`${portal}/ingresar`);
        await submit({ Email: email, Password: password });
        await until(`location.pathname.toLowerCase().includes('organizacionsuspendida') || location.pathname === '/organizacion-suspendida'`);
        if (kind === 'comercio')
        {
            await navigate(`${portal}/reactivacion`);
            try { await until("document.querySelector('#motivo-reactivacion') !== null"); }
            catch (error) { console.log('Commerce suspended page:', await evaluate('document.body.innerText')); throw error; }
        }
        else await until("document.querySelector('textarea[name=Motivo]') !== null");
        assert(await evaluate("document.body.innerText.includes('Organización suspendida')"), 'Suspended notice missing');
        const reasonSelector = kind === 'comercio' ? '#motivo-reactivacion' : 'textarea[name=Motivo]';
        async function requestReactivation(reason) {
            await evaluate(`(() => { const field = document.querySelector(${JSON.stringify(reasonSelector)}); const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set; setter.call(field, ${JSON.stringify(reason)}); field.dispatchEvent(new Event('input', { bubbles: true })); field.dispatchEvent(new Event('change', { bubbles: true })); })()`);
            await delay(300);
            await evaluate(`document.querySelector(${JSON.stringify(reasonSelector)}).closest('form').querySelector('input[type=checkbox]').click()`);
            await delay(300);
            assert(await evaluate(`document.querySelector(${JSON.stringify(reasonSelector)}).value === ${JSON.stringify(reason)} && document.querySelector(${JSON.stringify(reasonSelector)}).closest('form').querySelector('input[type=checkbox]').checked`), 'Request form lost reason or confirmation before submit');
            await evaluate(`(() => { const form = document.querySelector(${JSON.stringify(reasonSelector)}).closest('form'); form.requestSubmit(); form.requestSubmit(); })()`);
            try { await until("document.body.innerText.includes('La solicitud quedó pendiente')"); }
            catch (error) { console.log('After request:', await evaluate('document.body.innerText'), await evaluate(`document.querySelector(${JSON.stringify(reasonSelector)})?.closest('form')?.outerHTML`)); throw error; }
        }
        await requestReactivation('Necesitamos volver a operar');
        const suspendedHtml = await evaluate('document.documentElement.outerHTML');
        assert(!/eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+/.test(suspendedHtml), 'JWT exposed in HTML');
        writeFileSync(join(scratch, `${kind}-suspendido.png`), Buffer.from((await cdp.send('Page.captureScreenshot', { format: 'png' })).data, 'base64'));

        await navigate(`${admin}/ingresar`);
        await submit({ Email: platformEmail, Password: platformPassword });
        await until("location.pathname === '/' && !!document.querySelector('a[href=\"/solicitudes-reactivacion\"]')");
        async function resolve(decision, reason) {
            await navigate(`${admin}/solicitudes-reactivacion?Estado=Pendiente&Tipo=${kind === 'comercio' ? 'Comercio' : 'Operador'}`);
            await until("document.querySelector('a[href^=\"/solicitudes-reactivacion/\"]') !== null");
            const href = await evaluate("document.querySelector('a[href^=\"/solicitudes-reactivacion/\"]').getAttribute('href')");
            await navigate(`${admin}${href}`);
            await until("document.querySelector('button[name=decision][value=Aprobar]') !== null");
            const formData = await evaluate(`(() => { const form = document.querySelector('textarea[name=Motivo]').closest('form'); form.querySelector('[name=Motivo]').value = ${JSON.stringify(reason)}; form.querySelector('[name=Confirmar]').checked = true; const button = form.querySelector('button[value=${decision}]'); const data = new FormData(form, button); return { decision: data.get('decision'), reason: data.get('Motivo'), version: data.get('Version'), confirmation: data.getAll('Confirmar'), antiforgery: !!data.get('__RequestVerificationToken') }; })()`);
            assert(formData.decision === decision && formData.reason === reason && formData.version !== null && formData.confirmation.includes('true') && formData.antiforgery,
                `Resolver form lost decision or protected fields for ${decision}`);
            await evaluate(`(() => { const form = document.querySelector('textarea[name=Motivo]').closest('form'); form.requestSubmit(form.querySelector('button[value=${decision}]')); form.requestSubmit(form.querySelector('button[value=${decision}]')); })()`);
            await until(`document.body.innerText.includes(${JSON.stringify(decision === 'Aprobar' ? 'La solicitud fue aprobada' : 'La solicitud fue rechazada')})`);
            assert(await evaluate(`document.body.innerText.includes(${JSON.stringify(decision === 'Aprobar' ? 'Aprobada' : 'Rechazada')})`), 'Resolution state not rendered');
        }
        await resolve('Rechazar', 'Falta información para reactivar');
        await navigate(`${portal}/${kind === 'comercio' ? 'reactivacion' : 'OrganizacionSuspendida'}`);
        await until("document.body.innerText.includes('Falta información para reactivar')");
        if (kind === 'comercio') await until("document.querySelector('#motivo-reactivacion') !== null");
        else await until("document.querySelector('textarea[name=Motivo]') !== null");
        await requestReactivation('Información completa para reactivar');
        await resolve('Aprobar', 'Reactivación aprobada');
        await navigate(`${portal}/ingresar`);
        await submit({ Email: email, Password: password });
        try { await until(`location.pathname.toLowerCase() === '/envios' || document.body.innerText.includes('Mi ${kind}')`); }
        catch (error) { console.log('Fresh login:', await evaluate('location.href + "\\n" + document.body.innerText')); throw error; }
        assert(!(await evaluate('document.documentElement.outerHTML')).includes(password), 'Password rendered after login');
        console.log(`PASS: ${kind} suspended owner -> request -> rejection reason -> retry -> platform approval -> fresh operational login.`);
    }
    }
    console.log(`Browser evidence: ${scratch}`);
} finally {
    browserSocket?.close();
    for (const process of processes.reverse()) {
        if (process.exitCode === null) { const exited = new Promise(resolve => process.once('exit', resolve)); process.kill(); await Promise.race([exited, delay(5000)]); }
    }
    for (const fd of logs) closeSync(fd);
    const profile = join(scratch, 'profile');
    assert(dirname(resolve(profile)) === resolve(scratch), 'Browser profile must remain inside owned scratch');
    if (existsSync(profile)) rmSync(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
    if (ownedContainer) execFileSync('docker', ['stop', name], { stdio: 'pipe' });
}
