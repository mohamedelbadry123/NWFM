import test from 'node:test';
import assert from 'node:assert/strict';
import { verifyReplay } from '../e2e/verify-replay.mjs';
test('CI rejects incomplete, skipped, stale and model-backed replay runs', () => {
  const report = {
    run: {
      status: 'passed',
      exitCode: 0,
      startedAt: new Date().toISOString(),
      summary: { selected: 2, discovered: 2 },
      usage: { modelTokens: 0 },
      results: ['tests/example.e2e.ts', 'tests/workflow-lifecycle.e2e.ts'].map((file) => ({
        file,
        selected: true,
        status: 'passed',
        attempts: [
          {
            steps: [
              { api: 'agent.act', metrics: { modelCalls: 0 }, cache: { mode: 'self-finalized' } },
            ],
          },
        ],
      })),
    },
  };
  assert.doesNotThrow(() => verifyReplay(report, 0));
  assert.throws(() => verifyReplay(report, Date.now() + 1000));
  for (const change of [
    (r) => {
      r.run.results.pop();
    },
    (r) => {
      r.run.results[0].status = 'skipped';
    },
    (r) => {
      r.run.summary.selected = 1;
    },
    (r) => {
      r.run.usage.modelTokens = 10;
    },
    (r) => {
      r.run.results[1].attempts[0].steps = [{ api: 'agent.act', metrics: { modelCalls: 1 } }];
    },
    (r) => {
      r.run.results[1].attempts[0].steps = [{ api: 'agent.act', cache: { mode: 'missed' } }];
    },
  ]) {
    const invalid = structuredClone(report);
    change(invalid);
    assert.throws(() => verifyReplay(invalid, 0));
  }
});
