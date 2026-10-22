import { execFileSync, spawn } from 'node:child_process';
import { platform, stdin, stdout } from 'node:process';
import { StringDecoder } from 'node:string_decoder';

export async function initializePlatformAccount({ email, apiDirectory, environment }) {
    if (!stdin.isTTY || typeof stdin.setRawMode !== 'function')
        throw new Error('Run this smoke from an interactive terminal; platform-admin create requires hidden terminal input.');

    const password = await readHiddenPassword();
    stdin.setRawMode(false);
    stdin.pause();
    const consoleMode = disableConsoleEcho();
    stdout.write(`\nPlatform bootstrap email: ${email}\n`);
    try {
        const child = spawn('dotnet', ['bin/Release/net10.0/NexoRuta.Api.dll', 'platform-admin', 'create'], {
            cwd: apiDirectory,
            env: {
                ...environment,
                DatabaseInitialization__ApplyMigrations: 'false',
                DatabaseInitialization__SeedData: 'false'
            },
            stdio: 'inherit',
            windowsHide: false
        });
        const exitCode = await new Promise((resolve, reject) => {
            child.once('error', reject);
            child.once('exit', code => resolve(code ?? 1));
        });
        if (exitCode !== 0) throw new Error(`Explicit platform bootstrap exited with code ${exitCode}.`);
        return password;
    } finally {
        restoreConsoleMode(consoleMode);
        stdin.setRawMode(false);
        stdin.pause();
    }
}

const consoleModeInterop = `
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class BrowserConsoleMode {
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr GetStdHandle(int handle);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetConsoleMode(IntPtr handle, out uint mode);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool SetConsoleMode(IntPtr handle, uint mode);
}
'@
$handle = [BrowserConsoleMode]::GetStdHandle(-10)
`;

function consolePowerShell(script) {
    return execFileSync('powershell.exe', ['-NoLogo', '-NoProfile', '-NonInteractive', '-Command', `${consoleModeInterop}${script}`], {
        encoding: 'utf8', stdio: ['inherit', 'pipe', 'pipe'], windowsHide: true
    }).trim();
}

function disableConsoleEcho() {
    if (platform !== 'win32') {
        const originalMode = execFileSync('stty', ['-g'], { encoding: 'utf8', stdio: ['inherit', 'pipe', 'pipe'] }).trim();
        execFileSync('stty', ['-echo'], { stdio: 'inherit' });
        return originalMode;
    }
    const originalMode = Number(consolePowerShell(`
[uint32]$mode = 0
if (-not [BrowserConsoleMode]::GetConsoleMode($handle, [ref]$mode)) { throw 'Console mode unavailable' }
$quietMode = [uint32]($mode -band 4294967291)
if (-not [BrowserConsoleMode]::SetConsoleMode($handle, $quietMode)) { throw 'Could not disable console echo' }
[Console]::Write($mode)
`));
    if (!Number.isInteger(originalMode)) throw new Error('Could not read the original console mode.');
    return originalMode;
}

function restoreConsoleMode(mode) {
    if (platform !== 'win32') {
        execFileSync('stty', [mode], { stdio: 'inherit' });
        return;
    }
    consolePowerShell(`if (-not [BrowserConsoleMode]::SetConsoleMode($handle, [uint32]${mode})) { throw 'Could not restore console mode' }`);
}

function readHiddenPassword() {
    stdout.write('Temporary platform password for the ephemeral database (hidden, 12+ characters): ');
    stdin.setRawMode(true);
    stdin.resume();
    const decoder = new StringDecoder('utf8');
    return new Promise((resolve, reject) => {
        let password = '';
        const cleanup = () => {
            stdin.off('data', onData);
            stdin.pause();
            stdout.write('\n');
        };
        const onData = chunk => {
            for (const char of decoder.write(chunk)) {
                if (char === '\u0003') {
                    cleanup();
                    stdin.setRawMode(false);
                    reject(new Error('Platform bootstrap cancelled.'));
                    return;
                }
                if (char === '\r' || char === '\n') {
                    cleanup();
                    if (password.trim().length < 12) {
                        stdin.setRawMode(false);
                        reject(new Error('Temporary password must contain at least 12 non-whitespace characters.'));
                    } else resolve(password);
                    return;
                }
                if (char === '\u007f' || char === '\b') password = Array.from(password).slice(0, -1).join('');
                else if (!/\p{Cc}/u.test(char)) password += char;
            }
        };
        stdin.on('data', onData);
    });
}
