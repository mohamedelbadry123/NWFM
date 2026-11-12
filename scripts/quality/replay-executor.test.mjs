import test from 'node:test';
import assert from 'node:assert/strict';
import { replayOnly } from '../e2e/replay-only.ts';

test('replay executor refuses every fallback without touching a model or engine', async () => {
  // This private identity is deliberately checked against the pinned runner:
  // a future change must be reviewed before existing recordings can be reused.
  const { BUILT_IN_AGENT } = await import(
    new URL('./agent/agent-brand.js', import.meta.resolve('e2e'))
  );
  assert.equal(replayOnly.name, BUILT_IN_AGENT.name);
  assert.equal(replayOnly.version, BUILT_IN_AGENT.version);
  assert.equal(replayOnly.cache, 'inherit');
  const context = new Proxy(
    {},
    {
      get() {
        throw new Error('Replay fallback must not access a model, network, or engine');
      },
    },
  );
  const result = await replayOnly.runStep(context);
  assert.equal(result.status, 'failed');
  assert.equal(result.errorCode, 'ACTION_FAILED');
  assert.match(result.summary, /complete verified recording/);
});
