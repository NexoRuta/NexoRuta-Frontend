/** Real Chromium + Kestrel + owned ephemeral PostgreSQL; no npm dependencies.
 * Run after Release builds: node scripts/check-onboarding-browser.mjs (from frontend).
 * Browser executable may be selected with NEXORUTA_BROWSER. Never uses Compose or persistent DB settings.
 */
import { spawn, execFileSync } from 'node:child_process';
import { mkdtempSync, openSync, closeSync, writeFileSync, readFileSync, existsSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomBytes, randomUUID } from 'node:crypto';
import { createServer } from 'node:net';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const scratch = mkdtempSync(join(tmpdir(), 'nexoruta-onboarding-'));
const name = `nexoruta-onboarding-${randomUUID()}`;
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
    const apiPort = await port(), commercePort = await port(), operatorPort = await port();
    const base = `http://127.0.0.1:${commercePort}`, operator = `http://127.0.0.1:${operatorPort}`;
    start('dotnet', ['bin/Release/net10.0/NexoRuta.Api.dll'], {
        ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: `http://127.0.0.1:${apiPort}`,
        ConnectionStrings__Postgres: `Host=127.0.0.1;Port=${dbPort};Database=onboarding;Username=onboarding;Password=${dbPassword}`,
        DatabaseInitialization__ApplyMigrations: 'true', DatabaseInitialization__SeedData: 'false', PlatformInitialization__Enabled: 'false',
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
    for (const kind of ['comercio', 'operador']) {
        // Separate contexts prevent ambient cookies and accumulated cookies from one journey influencing the next.
        await cdp.send('Network.clearBrowserCookies');
        const email = `${kind}@browser.example.test`;
        await navigate(`${base}/`);
        assert(await evaluate("document.querySelector('a[href=\"/registrarse\"]').innerText === 'Crear cuenta'"), 'Public registration link missing');
        await cdp.send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
        assert(await evaluate('document.documentElement.scrollWidth <= window.innerWidth'), 'Mobile home overflows horizontally');
        writeFileSync(join(scratch, `${kind}-public-mobile.png`), Buffer.from((await cdp.send('Page.captureScreenshot', { format: 'png' })).data, 'base64'));
        await evaluate("document.querySelector('a[href=\"/registrarse\"]').click()");
        await until("location.pathname === '/registrarse' && document.querySelector('[name=ConfirmPassword]') !== null");
        assert(await evaluate("!!document.querySelector('a[href=\"/\"]') && !!document.querySelector('a[href=\"/ingresar\"]')"), 'Registration back/reciprocal links missing');
        await submit({ Email: email, Password: password, ConfirmPassword: password });
        await until("location.pathname === '/ingresar' && document.querySelector('[name=Email]') !== null");
        await submit({ Email: email, Password: password });
        await until("location.pathname === '/organizacion' && document.querySelector('[name=Nombre]') !== null");
        await navigate(`${base}/`);
        assert(await evaluate("document.querySelector('a[href=\"/organizacion\"]').innerText === 'Crear mi organización'"), 'Identity-only home action missing');
        await evaluate("document.querySelector('a[href=\"/organizacion\"]').click()");
        await until("location.pathname === '/organizacion' && document.querySelector('[name=Nombre]') !== null");
        const html = await evaluate('document.documentElement.outerHTML');
        assert(html.includes('Todavía no pertenecés a una organización'), 'Missing identity-only explanation');
        assert(!/eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+/.test(html), 'JWT exposed in HTML');
        writeFileSync(join(scratch, `${kind}-onboarding.png`), Buffer.from((await cdp.send('Page.captureScreenshot', { format: 'png' })).data, 'base64'));
        await submit({ Tipo: kind, Nombre: `Browser ${kind}` });
        await until(`location.pathname === '/organizacion-creada/${kind}' && document.querySelector('a') !== null`);
        const loginUrl = await evaluate('document.querySelector("a").href');
        assert(loginUrl === `${kind === 'operador' ? operator : base}/ingresar`, 'Unexpected destination or token-bearing URL');
        await navigate(loginUrl);
        await submit({ Email: email, Password: password });
        await until("location.pathname !== '/ingresar' && document.body.innerText.includes('envíos')");
        if (kind === 'comercio') {
            await navigate(`${base}/`);
            assert(await evaluate("document.querySelector('a[href=\"/envios\"]').innerText === 'Área de trabajo'"), 'Commerce work-area action missing');
            await evaluate("document.querySelector('a[href=\"/envios\"]').click()");
            await until("location.pathname === '/envios' && document.querySelector('a[href=\"/envios/crear\"]') !== null");
            await evaluate("document.querySelector('a[href=\"/envios/crear\"]').click()");
            await until("location.pathname === '/envios/crear' && document.querySelector('#envio-operador') !== null");
        }
        const cookies = (await cdp.send('Network.getCookies')).cookies;
        assert(cookies.some(x => x.name === `NexoRuta.${kind === 'operador' ? 'Backoffice' : 'Commerce'}` && x.httpOnly), 'Missing protected portal cookie');
        assert(await evaluate('localStorage.length === 0 && sessionStorage.length === 0'), 'Unexpected browser token storage');
        console.log(`PASS: public mobile home -> visible registration -> identity-only login -> ${kind} -> fresh destination login, protected cookie and no browser JWT.`);
    }
    console.log(`Screenshots/logs: ${scratch}`);
} finally {
    browserSocket?.close();
    for (const process of processes.reverse()) {
        if (process.exitCode === null) { const exited = new Promise(resolve => process.once('exit', resolve)); process.kill(); await Promise.race([exited, delay(5000)]); }
    }
    for (const fd of logs) closeSync(fd);
    // Retain screenshots/logs but never the browser profile containing cookies or cached form state.
    const profile = join(scratch, 'profile');
    assert(dirname(resolve(profile)) === resolve(scratch), 'Browser profile must remain inside the owned scratch directory');
    if (existsSync(profile)) rmSync(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
    if (ownedContainer) execFileSync('docker', ['stop', name], { stdio: 'pipe' });
}
