import http from 'node:http';
import { createReadStream } from 'node:fs';
import { stat } from 'node:fs/promises';
import { resolve, extname, sep } from 'node:path';

const root = resolve('frontend/dist/frontend/browser');
const api = new URL(process.env.NWFM_E2E_API_URL);
if (api.hostname !== '127.0.0.1' || !process.env.NWFM_E2E_RUN_ID)
  throw new Error('An owned loopback API is required.');
const types = {
  '.html': 'text/html',
  '.js': 'text/javascript',
  '.css': 'text/css',
  '.json': 'application/json',
  '.svg': 'image/svg+xml',
  '.woff2': 'font/woff2',
};
http
  .createServer(async (req, res) => {
    if (req.url === '/__e2e_owner') return res.end(process.env.NWFM_E2E_RUN_ID);
    if (/^\/(api|swagger)(\/|\?)/.test(req.url)) {
      const upstream = http.request(
        new URL(req.url, api),
        { method: req.method, headers: { ...req.headers, host: api.host } },
        (response) => {
          res.writeHead(response.statusCode, response.headers);
          response.pipe(res);
        },
      );
      upstream.on('error', () => res.writeHead(502).end());
      req.pipe(upstream);
      return;
    }
    try {
      const path = resolve(root, '.' + decodeURIComponent(new URL(req.url, api).pathname));
      if (path !== root && !path.startsWith(root + sep)) return res.writeHead(403).end();
      const info = await stat(path).catch(() => null);
      const file = info?.isFile() ? path : resolve(root, 'index.html');
      res.setHeader('Content-Type', types[extname(file)] ?? 'application/octet-stream');
      res.setHeader('Cache-Control', 'no-store');
      createReadStream(file)
        .on('error', () => res.destroy())
        .pipe(res);
    } catch {
      res.writeHead(400).end();
    }
  })
  .listen(Number(process.env.NWFM_E2E_UI_PORT), '127.0.0.1', function () {
    console.log(JSON.stringify({ port: this.address().port }));
  });
