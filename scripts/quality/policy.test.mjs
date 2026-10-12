import test from 'node:test';
import assert from 'node:assert/strict';
import { assertBranchAllowed } from './branch-guard.mjs';
import { validateBody, lintMessage } from './pr-policy.mjs';
import { inspectNuget } from './audit.mjs';

const body =
  '## Description\nAdds quality gates.\n\n## Issue Ticket\nNot applicable: repository tooling requested by the owner.\n\n## Type of Change\n- [x] Maintenance/tooling\n\n## Testing\n- [x] Tests pass.\n\n## Review\n- [x] Reviewed the diff.\n';
test('protected branches reject commits and arbitrary source refs cannot push to them', () => {
  for (const branch of ['main', 'develop', ''])
    assert.throws(() => assertBranchAllowed('commit', branch));
  assert.doesNotThrow(() => assertBranchAllowed('commit', 'codex/quality-gates-e2e'));
  for (const target of ['main', 'develop'])
    assert.throws(() =>
      assertBranchAllowed('push', 'feature', 'HEAD a refs/heads/' + target + ' b'),
    );
  assert.doesNotThrow(() =>
    assertBranchAllowed('push', 'feature', 'refs/heads/feature a refs/heads/feature b'),
  );
});
test('PR body must contain meaningful sections, issue rationale and checked tests/review', () => {
  assert.doesNotThrow(() => validateBody(body));
  for (const invalid of [
    body.replace('Adds quality gates.', '<!-- Fill me -->'),
    body.replace('Not applicable: repository tooling requested by the owner.', 'N/A'),
    body.replace('[x] Tests', '[ ] Tests'),
    body.replace('## Review', '## Something else'),
  ])
    assert.throws(() => validateBody(invalid));
});
test('Conventional Commits rejects vague messages', () => {
  assert.doesNotThrow(() => lintMessage('feat: add quality gates'));
  assert.doesNotThrow(() => lintMessage('fix: resolve memory leak'));
  assert.throws(() => lintMessage('fixed stuff'));
});
test('NuGet audit fails closed and detects transitive high severity findings', () => {
  const valid = {
    version: 1,
    sources: ['https://api.nuget.org/v3/index.json'],
    projects: [{ path: 'test', frameworks: [{}] }],
  };
  assert.doesNotThrow(() => inspectNuget(valid));
  assert.doesNotThrow(() => inspectNuget({ ...valid, projects: [{ path: 'clean.csproj' }] }));
  assert.throws(() =>
    inspectNuget({ ...valid, logs: [{ level: 'error', message: 'feed failed' }] }),
  );
  assert.throws(() => inspectNuget({}));
  assert.throws(() => inspectNuget({ ...valid, problems: ['feed unavailable'] }));
  assert.throws(() =>
    inspectNuget({
      ...valid,
      projects: [
        {
          path: 'vulnerable.csproj',
          frameworks: [
            { transitivePackages: [{ id: 'unsafe', vulnerabilities: [{ severity: 'High' }] }] },
          ],
        },
      ],
    }),
  );
});
