import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash, randomUUID } from 'node:crypto';
import { verifyReplay } from '../e2e/verify-replay.mjs';
function invocation(report) {
  const nonce = randomUUID();
  return {
    nonce,
    receipt: {
      nonce,
      runId: report.run.id,
      digest: createHash('sha256').update(JSON.stringify(report)).digest('hex'),
    },
  };
}

test('CI rejects incomplete, skipped, stale and model-backed replay runs', () => {
  const report = {
    schemaVersion: 'report-1',
    run: {
      id: randomUUID(),
      status: 'passed',
      exitCode: 0,
      startedAt: new Date().toISOString(),
      finishedAt: new Date().toISOString(),
      summary: { selected: 4, discovered: 4 },
      usage: { modelTokens: 0 },
      results: [
        'tests/example.e2e.ts',
        'tests/workflow-lifecycle.e2e.ts',
        'tests/workflow-activity-forms.e2e.ts',
        'tests/tenant-security.e2e.ts',
      ].map((file) => ({
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
  const current = invocation(report);
  assert.doesNotThrow(() => verifyReplay(report, current));
  assert.throws(() => verifyReplay(report), /fresh report/);
  assert.throws(() => verifyReplay(report, { nonce: current.nonce }), /fresh report/);
  assert.throws(() => verifyReplay(report, { ...current, nonce: randomUUID() }), /fresh report/);
  assert.throws(
    () =>
      verifyReplay(report, { ...current, receipt: { ...current.receipt, runId: randomUUID() } }),
    /fresh report/,
  );
  const previousReport = structuredClone(report);
  previousReport.run.id = randomUUID();
  assert.throws(() => verifyReplay(previousReport, current), /fresh report/);
  const alteredReport = structuredClone(report);
  alteredReport.run.summary.discovered++;
  assert.throws(() => verifyReplay(alteredReport, current), /fresh report/);

  // This invocation's receipt proves freshness even if NTP/VM time moved back.
  const clockAdjusted = structuredClone(report);
  clockAdjusted.run.startedAt = '2000-01-01T00:00:01.000Z';
  clockAdjusted.run.finishedAt = '2000-01-01T00:00:00.000Z';
  assert.doesNotThrow(() => verifyReplay(clockAdjusted, invocation(clockAdjusted)));
  for (const change of [
    (r) => {
      r.run.startedAt = 'invalid';
    },
    (r) => {
      r.run.finishedAt = 'invalid';
    },
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
    assert.throws(() => verifyReplay(invalid, invocation(invalid)));
  }
});
