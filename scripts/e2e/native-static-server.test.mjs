import { test } from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { createInterface } from 'node:readline';
import { randomUUID } from 'node:crypto';

test('owned static server proxies local API, serves SPA routes and refuses path escape', async () => {
  const requests = [];
  const api = http.createServer(async (req, res) => {
    let body = '';
    for await (const chunk of req) body += chunk;
    requests.push({ method: req.method, url: req.url, body });
    res.writeHead(202, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ accepted: true }));
  });
  api.listen(0, '127.0.0.1');
  await once(api, 'listening');
  const id = randomUUID();
  const child = spawn(process.execPath, ['scripts/e2e/native-static-server.mjs'], {
    windowsHide: true,
    env: {
      ...process.env,
      NWFM_E2E_API_URL: 'http://127.0.0.1:' + api.address().port,
      NWFM_E2E_UI_PORT: '0',
      NWFM_E2E_RUN_ID: id,
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  const lines = createInterface({ input: child.stdout });
  try {
    const line = await Promise.race([
      once(lines, 'line'),
      once(child, 'exit').then(([code]) => {
        throw new Error('Static server exited before ready: ' + code);
      }),
    ]);
    const origin = 'http://127.0.0.1:' + JSON.parse(line[0]).port;
    assert.equal(await (await fetch(origin + '/__e2e_owner')).text(), id);
    const response = await fetch(origin + '/api/owned-probe?query=1', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ test: true }),
    });
    assert.equal(response.status, 202);
    assert.deepEqual(await response.json(), { accepted: true });
    assert.deepEqual(requests, [
      { method: 'POST', url: '/api/owned-probe?query=1', body: '{"test":true}' },
    ]);
    const page = await fetch(origin + '/admin/workflow/definitions');
    assert.equal(page.status, 200);
    assert.match(page.headers.get('content-type'), /text\/html/);
    assert.match(await page.text(), /<app-root/);
    assert.equal((await fetch(origin + '/%2e%2e%2fpackage.json')).status, 403);
  } finally {
    lines.close();
    if (child.exitCode === null) {
      const exited = once(child, 'exit');
      child.kill();
      await exited;
    }
    api.closeAllConnections();
    await new Promise((resolve, reject) =>
      api.close((error) => (error ? reject(error) : resolve())),
    );
  }
});
