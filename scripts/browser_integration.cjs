// Run after dotnet build. Requires Playwright and Chrome; uses isolated PostgreSQL databases (requires PostgreSQL and psql).
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const { spawn, spawnSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '..');
const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'betty-browser-'));
const processes = [];
let browser;
const databaseNames = {};
const pg = { Host: process.env.PGHOST || '127.0.0.1', Port: process.env.PGPORT || '5432',
    Username: process.env.PGUSER || 'casino_admin', Password: process.env.PGPASSWORD || 'casino_local_admin' };
function sql(command) {
    const executable = process.env.PSQL_PATH || (process.platform === 'win32'
        ? path.join(process.env.LOCALAPPDATA, 'BettyCasinoPostgres/pgsql/bin/psql.exe') : 'psql');
    const result = spawnSync(executable, ['-h', pg.Host, '-p', pg.Port, '-U', pg.Username,
        '-d', 'postgres', '-v', 'ON_ERROR_STOP=1', '-c', command],
        { windowsHide: true, encoding: 'utf8', env: { ...process.env, PGPASSWORD: pg.Password } });
    if (result.error) throw result.error;
    if (result.status !== 0) throw new Error(result.stderr);
}
function connectionString(name) {
    return Object.entries({ ...pg, Database: databaseNames[name] })
        .map(([key, value]) => `${key}="${value.replaceAll('"', '""')}"`).join(';');
}
async function freePort() {
    const server = net.createServer();
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const port = server.address().port;
    await new Promise(resolve => server.close(resolve));
    return port;
}
async function waitFor(action, timeout = 30000) {
    const deadline = Date.now() + timeout;
    while (Date.now() < deadline) {
        try { if (await action()) return; } catch {}
        await new Promise(resolve => setTimeout(resolve, 150));
    }
    throw new Error('Timed out waiting for condition');
}
async function stop(child) {
    if (child.exitCode !== null || child.signalCode !== null) return;
    const exited = new Promise(resolve => child.once('exit', resolve));
    child.kill();
    await exited;
}
(async () => {
    for (const component of ['Wallet', 'Game']) {
        const name = 'casino_test_' + component.toLowerCase() + '_' + crypto.randomUUID().replaceAll('-', '');
        sql(`CREATE DATABASE "${name}"`);
        databaseNames[component] = name;
    }
    const ports = { Wallet: await freePort(), Game: await freePort(), Web: await freePort() };
    const url = name => `http://127.0.0.1:${ports[name]}`;
    async function start(name) {
        const child = spawn('dotnet', [path.join(root, `src/Casino.${name}/bin/Debug/net10.0/Casino.${name}.dll`),
            '--urls', url(name),
            '--Wallet:BaseUrl', url('Wallet') + '/', '--Game:BaseUrl', url('Game') + '/'],
            { cwd: directory, windowsHide: true, env: { ...process.env, Database__ConnectionString: connectionString(name === 'Web' ? 'Wallet' : name) }, stdio: ['ignore', 'pipe', 'pipe'] });
        child.logs = '';
        child.stdout.on('data', data => child.logs += data);
        child.stderr.on('data', data => child.logs += data);
        processes.push(child);
        await waitFor(async () => (await fetch(url(name) + '/health')).ok);
        return child;
    }
    let wallet = await start('Wallet');
    let game = await start('Game');
    await start('Web');
    browser = await chromium.launch({ channel: 'chrome', headless: true });
    const page = await browser.newPage({ viewport: { width: 1280, height: 1000 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const message = () => page.locator('#message').textContent();
    const stored = () => page.evaluate(() => JSON.parse(localStorage.getItem('betty.browser.player.v1')));
    await page.goto(url('Web'));
    await waitFor(async () => (await message()).includes('wallet is ready'));
    const player = (await stored()).playerId;
    async function walletAction(label, amount) {
        await page.locator('#wallet-amount').fill(amount);
        await page.getByRole('button', { name: label, exact: true }).click();
        await waitFor(async () => (await message()).includes('completed.'));
        await waitFor(async () => !(await page.locator('#refresh').isDisabled()));
    }
    await walletAction('Deposit', '10.25');
    assert.equal(await page.locator('#total').textContent(), '$10.25');
    await walletAction('Withdraw', '0.15');
    assert.equal(await page.locator('#total').textContent(), '$10.10');

    // Lose the response after the server commits. Reload and retry must not deposit twice.
    await page.route('**/api/deposits', async route => { await route.fetch(); await route.abort(); });
    await page.locator('#wallet-amount').fill('0.25');
    await page.getByRole('button', { name: 'Deposit', exact: true }).click();
    await waitFor(async () => (await message()).includes('request is saved'));
    const pending = (await stored()).pending;
    assert.ok(pending.request.requestId);
    await page.unroute('**/api/deposits');
    await page.reload();
    await waitFor(async () => (await message()).includes('Retry your saved request'));
    assert.equal((await stored()).pending.request.requestId, pending.request.requestId);
    assert.equal(await page.getByRole('button', { name: 'Deposit', exact: true }).isDisabled(), true);
    await page.getByRole('button', { name: 'Retry saved request' }).click();
    await waitFor(async () => !(await stored()).pending && !(await page.locator('#refresh').isDisabled()));
    assert.equal(await page.locator('#total').textContent(), '$10.35');

    // A Wallet outage leaves a persisted pending round. A restart and retry recover it.
    await stop(wallet);
    await page.locator('#bet-amount').fill('1.01');
    await page.getByRole('button', { name: 'Play round' }).click();
    await waitFor(async () => (await message()).includes('still processing'));
    await waitFor(async () => !(await page.locator('#retry').isDisabled()));
    const bet = (await stored()).pending;
    assert.equal(bet.action, 'bet');
    await stop(game);
    await page.reload();
    await waitFor(async () => (await message()).includes('request is saved'));
    assert.equal((await stored()).pending.request.requestId, bet.request.requestId);
    wallet = await start('Wallet');
    game = await start('Game');
    await page.getByRole('button', { name: 'Retry saved request' }).click();
    await waitFor(async () => !(await stored()).pending && !(await page.locator('#refresh').isDisabled()));
    const result = await (await fetch(url('Game') + '/bets', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(bet.request) })).json();
    assert.equal(result.state, 'Settled');
    const balance = await (await fetch(url('Wallet') + `/wallets/${player}`)).json();
    assert.equal(Math.round(balance.total * 100), 1035 - 101 + Math.round(result.payout * 100));
    assert.equal(balance.held, 0);

    await page.reload();
    await waitFor(async () => (await message()).includes('wallet is ready'));
    assert.equal((await stored()).playerId, player);
    await page.locator('summary').click();
    const otherPlayer = crypto.randomUUID();
    await page.locator('#player-id').fill(otherPlayer);
    await page.getByRole('button', { name: 'Use player' }).click();
    await waitFor(async () => (await message()).includes('Player loaded'));
    assert.equal((await stored()).playerId, otherPlayer);
    assert.equal(await page.locator('#total').textContent(), '$0.00');
    await page.locator('#player-id').fill(player);
    await page.getByRole('button', { name: 'Use player' }).click();
    await waitFor(async () => (await stored()).playerId === player && !(await page.locator('#refresh').isDisabled()));
    await page.locator('summary').click();
    await page.screenshot({ path: path.join(directory, 'desktop.png'), fullPage: true });
    await page.setViewportSize({ width: 390, height: 844 });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    await page.screenshot({ path: path.join(directory, 'mobile.png'), fullPage: true });
    assert.deepEqual(errors, []);
    if (process.env.SCREENSHOT_DIRECTORY) {
        fs.mkdirSync(process.env.SCREENSHOT_DIRECTORY, { recursive: true });
        for (const file of ['desktop.png', 'mobile.png']) fs.copyFileSync(path.join(directory, file), path.join(process.env.SCREENSHOT_DIRECTORY, file));
    }
    console.log('PASS: browser deposit, withdrawal, decimal balance, lost response/reload/idempotent retry, Wallet outage, Game restart/recovery, persistent identity, player reuse, mobile layout, no JavaScript errors');
})().catch(error => {
    console.error(error);
    for (const child of processes) console.error(child.logs.slice(-2500));
    process.exitCode = 1;
}).finally(async () => {
    if (browser) await browser.close();
    for (const child of processes) await stop(child);
    for (const name of Object.values(databaseNames)) {
        if (!/^casino_test_(wallet|game)_[a-f0-9]{32}$/.test(name)) throw new Error('Invalid test database name');
        sql(`DROP DATABASE IF EXISTS "${name}" WITH (FORCE)`);
    }
    fs.rmSync(directory, { recursive: true, force: true });
});
