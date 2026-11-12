import { randomUUID } from 'node:crypto';

// Only mounted by the explicitly started loopback demo server. No real system is contacted.
export function createUnrcSimulator() {
  const operations = new Map();
  const recovered = new Set();
  return function handle(req, res, body) {
    if (!req.url?.startsWith('/unrc/')) return false;
    const reply = (status, value) => {
      res.writeHead(status, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify(value));
      return true;
    };
    if (req.method !== 'POST') return reply(405, { error: 'Use POST.' });
    let input;
    try {
      input = JSON.parse(body || '{}');
    } catch {
      return reply(400, { error: 'Invalid JSON.' });
    }
    if (!input || typeof input !== 'object' || Array.isArray(input))
      return reply(400, { error: 'An object is required.' });
    if (req.url === '/unrc/recover') {
      if (typeof input.reference !== 'string')
        return reply(400, { error: 'Reference is required.' });
      recovered.add(input.reference);
      return reply(200, { recovered: true });
    }
    if (!['/unrc/release', '/unrc/close', '/unrc/route', '/unrc/notify'].includes(req.url))
      return reply(404, { error: 'Unknown UNRC operation.' });
    const key = req.headers['idempotency-key'];
    if (typeof key !== 'string' || !key)
      return reply(400, { error: 'Idempotency-Key is required.' });
    const signature = req.url + ':' + body;
    let operation = operations.get(key);
    if (operation && operation.signature !== signature)
      return reply(409, { error: 'The operation key was reused with a different request.' });
    if (!operation) {
      operation = { signature, attempts: 0, result: null };
      operations.set(key, operation);
    }
    operation.attempts++;
    if (operation.result) return reply(200, operation.result);
    if (
      req.url === '/unrc/release' &&
      (operation.attempts === 1 ||
        (String(input.reference).startsWith('FAIL-') && !recovered.has(input.reference)))
    )
      return reply(503, { error: 'Simulated contractor outage. No release has been created.' });
    if (req.url === '/unrc/notify' && operation.attempts === 1)
      return reply(503, { error: 'Simulated background notification retry.' });
    operation.result = {
      id: randomUUID(),
      accepted: true,
      ...(req.url === '/unrc/route'
        ? { nextTask: String(input.reference).startsWith('COMPLETE-') ? 'complete' : 'activation' }
        : {}),
    };
    return reply(200, operation.result);
  };
}
