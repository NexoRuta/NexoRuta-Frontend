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
import { initializePlatformAccount } from './browser-platform-bootstrap.mjs';

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
    assert(process.stdin.isTTY && typeof process.stdin.setRawMode === 'function', 'Run this browser smoke from an interactive terminal; platform bootstrap uses hidden terminal input.');
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
    const apiDirectory = resolve(root, '../backend/src/NexoRuta.Api');
    const apiEnvironment = {
        ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: `http://127.0.0.1:${apiPort}`,
        ConnectionStrings__Postgres: `Host=127.0.0.1;Port=${dbPort};Database=onboarding;Username=onboarding;Password=${dbPassword}`,
        DatabaseInitialization__ApplyMigrations: 'true', DatabaseInitialization__SeedData: 'false',
        OpenTelemetry__Enabled: 'false', Jwt__Issuer: 'browser-check', Jwt__Audience: 'browser-check', Jwt__SigningKeyBase64: randomBytes(32).toString('base64')
    };
    start('dotnet', ['bin/Release/net10.0/NexoRuta.Api.dll'], apiEnvironment, apiDirectory, 'api');
    await waitFor(`http://127.0.0.1:${apiPort}/health/ready`);
    // Health-ready follows ephemeral migration completion; the explicit one-shot CLI creates only platform authority.
    const platformPassword = await initializePlatformAccount({ email: platformEmail, apiDirectory, environment: apiEnvironment });
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
    for (const kind of ['comercio', 'operador']) {
        if (kind === 'operador') {
            // Keep both isolated browser journeys under the real fixed-window login limiter.
            await delay(60_000);
        }
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
        await until(`location.pathname !== '/ingresar' && document.body.innerText.includes(${JSON.stringify(kind === 'operador' ? 'Envíos' : 'envíos')})`);
        if (kind === 'comercio') {
            await navigate(`${base}/`);
            assert(await evaluate("document.querySelector('a[href=\"/envios\"]').innerText === 'Área de trabajo'"), 'Commerce work-area action missing');
            await evaluate("document.querySelector('a[href=\"/envios\"]').click()");
            await until("location.pathname === '/envios' && document.querySelector('a[href=\"/envios/crear\"]') !== null");
            await evaluate("document.querySelector('a[href=\"/envios/crear\"]').click()");
            await until("location.pathname === '/envios/crear' && document.querySelector('#envio-operador') !== null");
            await navigate(`${base}/mi-comercio`);
            await until("location.pathname === '/mi-comercio' && document.querySelector('#nombre-comercio') !== null");
            assert(await evaluate("document.body.innerText.includes('Browser comercio')"), 'Commerce owner profile did not load the authenticated organization');
            for (const [label, width, height] of [['desktop', 1280, 900], ['mobile', 390, 844]]) {
                await cdp.send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: label === 'mobile' });
                if (label === 'desktop') {
                    await evaluate(`(() => { const input = document.querySelector('#nombre-comercio'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, 'Browser comercio actualizado'); input.dispatchEvent(new Event('change', { bubbles: true })); const form = input.closest('form'); form.requestSubmit(); form.requestSubmit(); })()`);
                    await until("document.body.innerText.includes('Nombre del comercio actualizado.')");
                    assert(await evaluate("document.body.innerText.includes('Browser comercio actualizado')"), 'Commerce name edit did not appear in the page');
                } else {
                    assert(await evaluate('document.documentElement.scrollWidth <= window.innerWidth'), 'Commerce profile overflows horizontally on mobile');
                    writeFileSync(join(scratch, `mi-comercio-${label}.png`), Buffer.from((await cdp.send('Page.captureScreenshot', { format: 'png' })).data, 'base64'));
                }
            }
            console.log('PASS: Commerce owner -> own profile read/edit with persisted concurrency version; desktop and mobile layouts.');
            for (const [label, width, height] of [['desktop', 1280, 900], ['mobile', 390, 844]]) {
                await cdp.send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: label === 'mobile' });
                await navigate(`${base}/usuarios`);
                assert(await evaluate("['/','/envios','/envios/crear','/mi-comercio','/usuarios','/roles'].every(path => !!document.querySelector('nav[aria-label] a[href=\"' + path + '\"]'))"), 'Direct user page lacks commerce navigation');
                await evaluate("document.querySelector('nav[aria-label] a[href=\"/roles\"]').click()");
                await until("location.pathname === '/roles' && document.querySelector('[name=Codigo]') !== null");
                assert(await evaluate("Array.from(document.querySelectorAll('article.card')).find(card => card.querySelector('h3')?.innerText.includes('(dueno)'))?.querySelectorAll('form').length === 0"), 'Own protected role must remain read-only');
                const code = `equipo-${label}`, roleName = `Equipo ${label}`, employeeEmail = `employee-${label}@browser.example.test`;
                await evaluate(`(() => { const form = document.querySelector('form[action*="handler=Crear"]'); form.querySelector('[name=Codigo]').value = ${JSON.stringify(code)}; form.querySelector('[name=Nombre]').value = ${JSON.stringify(roleName)}; form.requestSubmit(); })()`);
                await until(`document.body.innerText.includes('Rol creado.') && Array.from(document.querySelectorAll('article.card')).some(card => card.querySelector('h3')?.innerText.includes(${JSON.stringify(code)}))`);
                const roleId = await evaluate(`Array.from(document.querySelectorAll('article.card')).find(card => card.querySelector('h3')?.innerText.includes(${JSON.stringify(code)})).querySelector('input[name=id]').value`);
                const cardExpression = `Array.from(document.querySelectorAll('article.card')).find(card => card.querySelector('input[name=id]')?.value === ${JSON.stringify(roleId)})`;
                const available = await evaluate(`Array.from((${cardExpression}).querySelectorAll('input[name=permisos]')).map(input => input.value).sort()`);
                assert(JSON.stringify(available) === JSON.stringify(['configuracion.gestionar', 'envios.consultar', 'envios.crear', 'usuarios.gestionar']), 'Commerce permission catalog differs or includes another scope');
                await evaluate(`(() => { const form = (${cardExpression}).querySelector('form[action*="handler=Renombrar"]'); form.querySelector('[name=nombre]').value = ${JSON.stringify(roleName + ' editable')}; form.requestSubmit(); })()`);
                await until(`document.body.innerText.includes('Rol renombrado.') && document.body.innerText.includes(${JSON.stringify(roleName + ' editable')})`);
                await evaluate(`(() => { const form = (${cardExpression}).querySelector('form[action*="handler=Permisos"]'); form.querySelectorAll('input[name=permisos]').forEach(input => input.checked = ['envios.crear','envios.consultar'].includes(input.value)); form.requestSubmit(); })()`);
                await until("document.body.innerText.includes('Permisos del rol actualizados.')");
                assert(await evaluate("!document.body.innerText.includes('field is required.')"), 'Unsubmitted role creation form shows validation errors');
                assert(await evaluate(`Array.from((${cardExpression}).querySelectorAll('input[name=permisos]:checked')).map(input => input.value).sort().join(',') === 'envios.consultar,envios.crear'`), 'Role grants not persisted');
                assert(await evaluate('document.documentElement.scrollWidth <= window.innerWidth'), 'Role administration overflows horizontally');
                writeFileSync(join(scratch, `roles-${label}.png`), Buffer.from((await cdp.send('Page.captureScreenshot', { format: 'png' })).data, 'base64'));
                await evaluate("document.querySelector('nav[aria-label] a[href=\"/usuarios\"]').click()");
                await until("location.pathname === '/usuarios' && document.querySelector('[name=Email]') !== null");
                await evaluate(`(() => { const form = document.querySelector('form[action*="handler=Crear"]'); form.querySelector('[name=Email]').value = ${JSON.stringify(employeeEmail)}; form.querySelector('[name=Password]').value = ${JSON.stringify(password)}; form.querySelector('[name=RolId]').value = ${JSON.stringify(roleId)}; form.requestSubmit(); })()`);
                await until(`document.body.innerText.includes('Usuario creado con acceso al comercio.') && document.body.innerText.includes(${JSON.stringify(employeeEmail)})`);
                const rowExpression = `Array.from(document.querySelectorAll('tbody tr')).find(row => row.innerText.includes(${JSON.stringify(employeeEmail)}))`;
                assert(await evaluate(`(${rowExpression}).querySelector('select[name=rolId]').value === ${JSON.stringify(roleId)}`), 'Employee did not receive selected role');
                assert(!(await evaluate('document.documentElement.outerHTML')).includes(password), 'Employee password rendered after creation');
                for (const selected of ['', roleId]) {
                    await evaluate(`(() => { const form = (${rowExpression}).querySelector('form[action*="handler=Rol"]'); form.querySelector('[name=rolId]').value = ${JSON.stringify(selected)}; form.requestSubmit(); })()`);
                    await until(`document.body.innerText.includes(${JSON.stringify(selected ? 'Rol asignado al acceso.' : 'Rol retirado del acceso.')})`);
                    assert(await evaluate("!document.body.innerText.includes('field is required.')"), 'Unsubmitted employee creation form shows validation errors');
                }
                writeFileSync(join(scratch, `usuarios-${label}.png`), Buffer.from((await cdp.send('Page.captureScreenshot', { format: 'png' })).data, 'base64'));
                await evaluate("document.querySelector('nav[aria-label] a[href=\"/\"]').click()");
                await until("location.pathname === '/' && document.querySelector('a[href=\"/envios\"]') !== null");
                console.log(`PASS: ${label} commerce owner -> roles -> create/rename editable role -> scope permissions -> users -> employee with role -> assignment change -> home; protected Owner read-only.`);
            }
            await navigate(`${base}/mi-comercio`);
            await until("location.pathname === '/mi-comercio' && document.querySelector('#motivo-baja-comercio') !== null");
            await evaluate(`(() => { const reason = document.querySelector('#motivo-baja-comercio'); Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set.call(reason, 'Owner requested closure for browser validation'); reason.dispatchEvent(new Event('input', { bubbles: true })); reason.dispatchEvent(new Event('change', { bubbles: true })); })()`);
            const checkboxPoint = JSON.parse(await evaluate("JSON.stringify((() => { const r = document.querySelector('#motivo-baja-comercio').closest('form').querySelector('input[type=checkbox]').getBoundingClientRect(); return { x: r.x + r.width / 2, y: r.y + r.height / 2 }; })())"));
            await cdp.send('Input.dispatchMouseEvent', { type: 'mousePressed', x: checkboxPoint.x, y: checkboxPoint.y, button: 'left', clickCount: 1 });
            await cdp.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: checkboxPoint.x, y: checkboxPoint.y, button: 'left', clickCount: 1 });
            await delay(200);
            await evaluate("(() => { const form = document.querySelector('#motivo-baja-comercio').closest('form'); form.requestSubmit(); form.requestSubmit(); })()");
            await until("document.body.innerText.includes('La solicitud quedó pendiente de resolución por plataforma') && document.body.innerText.includes('sigue activo')");
            assert(await evaluate("!document.body.innerText.includes('Aprobada')"), 'A pending cancellation request must not suspend the commerce');
            writeFileSync(join(scratch, 'cancellation-pending.png'), Buffer.from((await cdp.send('Page.captureScreenshot', { format: 'png' })).data, 'base64'));

            await navigate(`${admin}/ingresar`);
            await submit({ Email: platformEmail, Password: platformPassword });
            await until("location.pathname === '/' && !!document.querySelector('a[href=\"/solicitudes-baja\"]')");
            await navigate(`${admin}/solicitudes-baja?Estado=Pendiente&Tipo=Comercio`);
            await until("document.querySelector('a[href^=\"/solicitudes-baja/\"]') !== null");
            const requestHref = await evaluate("document.querySelector('a[href^=\"/solicitudes-baja/\"]').getAttribute('href')");
            await navigate(`${admin}${requestHref}`);
            await until("document.querySelector('button[name=decision][value=Aprobar]') !== null");
            const renderedForm = await evaluate(`(() => { const form = document.querySelector('textarea[name=Motivo]').closest('form'); return { buttons: Array.from(form.querySelectorAll('button[type=submit]')).map(x => ({ name: x.name, value: x.value, handler: x.getAttribute('formaction') })), version: form.querySelector('[name=Version]')?.value, confirm: form.querySelector('[name=Confirmar]')?.type, reasonName: form.querySelector('textarea[name=Motivo]')?.name, antiforgery: !!form.querySelector('[name=__RequestVerificationToken]') }; })()`);
            assert(renderedForm.buttons.some(x => x.name === 'decision' && x.value === 'Aprobar' && x.handler?.includes('handler=Resolver'))
                && renderedForm.buttons.some(x => x.name === 'decision' && x.value === 'Rechazar' && x.handler?.includes('handler=Resolver')),
                'Rendered Admin form does not bind its decision buttons to the resolver handler');
            assert(renderedForm.version !== undefined && renderedForm.confirm === 'checkbox' && renderedForm.reasonName === 'Motivo' && renderedForm.antiforgery,
                'Rendered Admin form is missing version, confirmation, reason, or antiforgery fields');
            await evaluate(`(() => { const form = document.querySelector('textarea[name=Motivo]').closest('form'); form.querySelector('[name=Motivo]').value = 'Prueba de regresión del submitter de aprobación'; form.querySelector('[name=Confirmar]').checked = true; const approve = form.querySelector('button[value=Aprobar]'); form.addEventListener('submit', event => { form.querySelectorAll('button[type=submit]').forEach(button => button.disabled = true); const data = new FormData(form, event.submitter); window.name = JSON.stringify({ disabled: event.submitter.disabled, decision: data.get('decision') }); }, { once: true }); form.requestSubmit(approve); })()`);
            await until("document.querySelector('[data-valmsg-for=Decision]')?.innerText.includes('Elegí aprobar o rechazar la solicitud.')");
            const brokenApprovalPayload = JSON.parse(await evaluate('window.name'));
            assert(brokenApprovalPayload.disabled && brokenApprovalPayload.decision === null && await evaluate("document.body.innerText.includes('Pendiente')"),
                'Legacy disable-all behavior did not reproduce the missing approve decision on the rendered form');
            await evaluate('window.name = ""');
            await evaluate(`(() => { const form = document.querySelector('textarea[name=Motivo]').closest('form'); form.querySelector('[name=Motivo]').value = 'Aprobación y suspensión verificadas en navegador'; form.querySelector('[name=Confirmar]').checked = true; const approve = form.querySelector('button[value=Aprobar]'); form.addEventListener('submit', event => { const data = new FormData(form, event.submitter); window.name = JSON.stringify({ id: location.pathname.split('/').at(-1), name: event.submitter.name, value: event.submitter.value, enabled: !event.submitter.disabled, decision: data.get('decision'), reason: data.get('Motivo'), confirmation: data.getAll('Confirmar'), version: data.get('Version') }); }, { once: true }); form.requestSubmit(approve); form.requestSubmit(approve); })()`);
            await until("document.body.innerText.includes('La solicitud fue aprobada y la organización quedó suspendida.') && document.body.innerText.includes('Aprobada')");
            const approvedPayload = JSON.parse(await evaluate('window.name'));
            assert(approvedPayload.id === requestHref.split('/').at(-1) && approvedPayload.name === 'decision' && approvedPayload.value === 'Aprobar'
                && approvedPayload.enabled && approvedPayload.decision === 'Aprobar' && approvedPayload.reason === 'Aprobación y suspensión verificadas en navegador'
                && approvedPayload.confirmation.includes('true') && approvedPayload.version === renderedForm.version,
                'Approve submitter or bound fields were lost after the single-submit JavaScript handler');
            await evaluate('window.name = ""');
            writeFileSync(join(scratch, 'admin-request-approved.png'), Buffer.from((await cdp.send('Page.captureScreenshot', { format: 'png' })).data, 'base64'));

            await navigate(`${admin}/comercios?Estado=Suspendida`);
            await until("Array.from(document.querySelectorAll('tbody tr')).some(row => row.innerText.includes('Browser comercio actualizado'))");
            const organizationHref = await evaluate("Array.from(document.querySelectorAll('tbody tr')).find(row => row.innerText.includes('Browser comercio actualizado')).querySelector('a').getAttribute('href')");
            await navigate(`${admin}${organizationHref}`);
            await until("document.body.innerText.includes('Suspendida') && document.querySelector('form[data-single-submit] textarea[name=Motivo]') !== null");
            await evaluate(`(() => { const form = document.querySelector('textarea[name=Motivo]').closest('form'); form.querySelector('[name=Motivo]').value = 'Reactivated by platform in browser validation'; form.querySelector('[name=Confirmar]').checked = true; form.requestSubmit(); form.requestSubmit(); })()`);
            await until("document.body.innerText.includes('Organization state updated.') && document.body.innerText.includes('Activa')");
            writeFileSync(join(scratch, 'admin-commerce-reactivated.png'), Buffer.from((await cdp.send('Page.captureScreenshot', { format: 'png' })).data, 'base64'));

            await delay(60_000); // The full journey has already used the five-per-minute login budget from this IP.
            await cdp.send('Network.clearBrowserCookies'); // Reactivation requires a fresh operational portal session.
            await navigate(`${base}/ingresar`);
            await submit({ Email: email, Password: password });
            await until("location.pathname !== '/ingresar' && document.body.innerText.includes('envíos')");
            await navigate(`${base}/mi-comercio`);
            await until("location.pathname === '/mi-comercio' && document.querySelector('#motivo-baja-comercio') !== null");
            assert(await evaluate("document.body.innerText.includes('Activa') && document.body.innerText.includes('Aprobada')"), 'A fresh login after reactivation must show the resolved cancellation and active status');
            assert(!(await evaluate('document.documentElement.outerHTML')).includes(platformPassword), 'Platform password rendered in HTML');
            console.log('PASS: Commerce owner -> pending request -> platform approval and suspension -> support reactivation -> old session revoked -> fresh login.');
        } else {
            for (const [label, width, height] of [['desktop', 1280, 900], ['mobile', 390, 844]]) {
                await cdp.send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: label === 'mobile' });
                await navigate(`${operator}/mi-operador`);
                await until("location.pathname === '/mi-operador' && document.querySelector('input[name=Nombre]') !== null");
                assert(await evaluate("document.body.innerText.includes('Browser operador')"), 'Operator owner profile did not load the authenticated organization');
                if (label === 'desktop') {
                    await evaluate(`(() => { const form = document.querySelector('form[action*="handler=Guardar"]'); form.querySelector('[name=Nombre]').value = 'Browser operador actualizado'; form.querySelector('[name=Version]').value = '0'; form.requestSubmit(); form.requestSubmit(); })()`);
                    await until("document.body.innerText.includes('Nombre del operador actualizado.')");
                    assert(await evaluate("document.body.innerText.includes('Browser operador actualizado')"), 'Operator name edit did not appear in the page');
                } else {
                    assert(await evaluate('document.documentElement.scrollWidth <= window.innerWidth'), 'Operator profile overflows horizontally on mobile');
                    writeFileSync(join(scratch, `mi-operador-${label}.png`), Buffer.from((await cdp.send('Page.captureScreenshot', { format: 'png' })).data, 'base64'));
                }
            }
            console.log('PASS: Operator owner -> own profile read/edit with antiforgery and persisted concurrency version; desktop and mobile layouts.');

            await navigate(`${operator}/mi-operador`);
            await until("location.pathname === '/mi-operador' && document.querySelector('textarea[name=MotivoBaja]') !== null");
            await evaluate(`(() => { const form = document.querySelector('textarea[name=MotivoBaja]').closest('form'); form.querySelector('[name=MotivoBaja]').value = 'Solicitud de baja para validar el rechazo desde Admin'; form.querySelector('[name=ConfirmarBaja]').checked = true; form.requestSubmit(); form.requestSubmit(); })()`);
            await until("document.body.innerText.includes('El operador sigue activo mientras plataforma la resuelve')");
            assert(await evaluate("document.body.innerText.includes('Activa')"), 'A pending operator cancellation must leave the operator active');

            await navigate(`${admin}/ingresar`);
            await submit({ Email: platformEmail, Password: platformPassword });
            await until("location.pathname === '/' && !!document.querySelector('a[href=\"/solicitudes-baja\"]')");
            await navigate(`${admin}/solicitudes-baja?Estado=Pendiente&Tipo=Operador`);
            await until("document.querySelector('a[href^=\"/solicitudes-baja/\"]') !== null");
            const operatorRequestHref = await evaluate("document.querySelector('a[href^=\"/solicitudes-baja/\"]').getAttribute('href')");
            await navigate(`${admin}${operatorRequestHref}`);
            await until("document.querySelector('button[name=decision][value=Rechazar]') !== null");
            const rejectedForm = await evaluate(`(() => { const form = document.querySelector('textarea[name=Motivo]').closest('form'); return { version: form.querySelector('[name=Version]').value, antiforgery: !!form.querySelector('[name=__RequestVerificationToken]') }; })()`);
            assert(rejectedForm.antiforgery, 'Rendered rejection form is missing antiforgery protection');
            await evaluate(`(() => { const form = document.querySelector('textarea[name=Motivo]').closest('form'); form.querySelector('[name=Motivo]').value = 'Prueba de regresión del submitter de rechazo'; form.querySelector('[name=Confirmar]').checked = true; const reject = form.querySelector('button[value=Rechazar]'); form.addEventListener('submit', event => { form.querySelectorAll('button[type=submit]').forEach(button => button.disabled = true); const data = new FormData(form, event.submitter); window.name = JSON.stringify({ disabled: event.submitter.disabled, decision: data.get('decision') }); }, { once: true }); form.requestSubmit(reject); })()`);
            await until("document.querySelector('[data-valmsg-for=Decision]')?.innerText.includes('Elegí aprobar o rechazar la solicitud.')");
            const brokenRejectionPayload = JSON.parse(await evaluate('window.name'));
            assert(brokenRejectionPayload.disabled && brokenRejectionPayload.decision === null && await evaluate("document.body.innerText.includes('Pendiente')"),
                'Legacy disable-all behavior did not reproduce the missing reject decision on the rendered form');
            await evaluate('window.name = ""');
            await evaluate(`(() => { const form = document.querySelector('textarea[name=Motivo]').closest('form'); form.querySelector('[name=Motivo]').value = 'Rechazo verificado en navegador'; form.querySelector('[name=Confirmar]').checked = true; const reject = form.querySelector('button[value=Rechazar]'); form.addEventListener('submit', event => { const data = new FormData(form, event.submitter); window.name = JSON.stringify({ value: event.submitter.value, enabled: !event.submitter.disabled, decision: data.get('decision'), confirmation: data.getAll('Confirmar'), version: data.get('Version') }); }, { once: true }); form.requestSubmit(reject); form.requestSubmit(reject); })()`);
            await until("document.body.innerText.includes('La solicitud fue rechazada; el estado de la organización no cambió.') && document.body.innerText.includes('Rechazada')");
            const rejectedPayload = JSON.parse(await evaluate('window.name'));
            assert(rejectedPayload.value === 'Rechazar' && rejectedPayload.enabled && rejectedPayload.decision === 'Rechazar'
                && rejectedPayload.confirmation.includes('true') && rejectedPayload.version === rejectedForm.version,
                'Reject submitter or bound fields were lost after the single-submit JavaScript handler');
            await evaluate('window.name = ""');
            await navigate(`${operator}/mi-operador`);
            await until("location.pathname === '/mi-operador' && document.body.innerText.includes('Rechazada')");
            assert(await evaluate("document.body.innerText.includes('Activa')"), 'Rejecting a cancellation must not suspend the operator');
            writeFileSync(join(scratch, 'admin-request-rejected.png'), Buffer.from((await cdp.send('Page.captureScreenshot', { format: 'png' })).data, 'base64'));
            console.log('PASS: Rendered Admin approval and rejection forms preserve the named submitter and bound fields with JavaScript enabled; rejection keeps the operator active.');
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
