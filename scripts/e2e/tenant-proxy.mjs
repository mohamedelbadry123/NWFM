import http from 'node:http';

/** A second origin for the same UI, backed by the second isolated tenant API. */
export function createTenantProxy() {
  return new Promise((resolve, reject) => {
    const server = http.createServer((request, response) => {
      const api = request.url === '/api' || request.url.startsWith('/api/');
      const upstream = http.request(
        {
          host: '127.0.0.1',
          port: api ? 5082 : 4200,
          path: request.url,
          method: request.method,
          headers: { ...request.headers, host: api ? 'localhost:5082' : 'localhost:4200' },
        },
        (result) => {
          response.writeHead(result.statusCode, result.headers);
          result.pipe(response);
        },
      );
      upstream.on('error', () => {
        response.writeHead(502);
        response.end('Test tenant upstream is unavailable.');
      });
      request.pipe(upstream);
    });
    server.once('error', reject);
    server.listen(4201, '127.0.0.1', () => resolve(server));
  });
}
