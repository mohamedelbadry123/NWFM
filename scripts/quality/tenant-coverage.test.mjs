import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { sourceUnits, validateCoverage } from '../security/tenant-coverage.mjs';

const root = fileURLToPath(new URL('../../', import.meta.url));
const source = readFileSync(
  new URL('../../docs/security/vendor/owasp-multi-tenant.md', import.meta.url),
  'utf8',
);
const catalog = JSON.parse(
  readFileSync(new URL('../../docs/security/tenant-controls.json', import.meta.url), 'utf8'),
);

test('coverage accounts for source examples, paragraphs, risks, do/dont points and strategies', () => {
  const units = validateCoverage(source, catalog, root);
  assert.ok(units.length > 100);
  for (const kind of ['example', 'requirement', 'strategy', 'prose'])
    assert.ok(units.some((unit) => unit.kind === kind));
  assert.equal(sourceUnits(source.replaceAll('\n', '\r\n')).length, units.length);
  const prohibition = units.find((unit) => unit.text.includes('Skip authorization merely'));
  assert.ok(prohibition.heading.endsWith("/ Don't"));
  const obligation = units.find((unit) => unit.text.includes('Derive tenant context from'));
  assert.ok(obligation.heading.endsWith('/ Do'));
});

test('omissions, source changes, unsupported status and absent evidence fail closed', () => {
  const missing = structuredClone(catalog);
  missing.controls.pop();
  assert.throws(() => validateCoverage(source, missing, root), /Every source/);
  assert.throws(() => validateCoverage(source + '\nChanged', catalog, root), /digest/);
  const status = structuredClone(catalog);
  status.controls[0].status = 'waived';
  assert.throws(() => validateCoverage(source, status, root), /status/);
  const description = structuredClone(catalog);
  description.controls[0].requirement = 'Different requirement with the original digest';
  assert.throws(() => validateCoverage(source, description, root), /misrepresented/);
  const polarity = structuredClone(catalog);
  const prohibited = polarity.controls.find((unit) => unit.heading.endsWith("/ Don't"));
  prohibited.heading = prohibited.heading.replace("/ Don't", '/ Do');
  assert.throws(() => validateCoverage(source, polarity, root), /misrepresented/);
  const evidence = structuredClone(catalog);
  evidence.groups[evidence.controls[0].group].tests = ['missing-security-test.cs'];
  assert.throws(() => validateCoverage(source, evidence, root), /evidence/);
});

test('deployment obligations cannot silently be marked as covered', () => {
  const missing = structuredClone(catalog);
  const deployment = missing.controls.find((row) => row.status.includes('deployment'));
  assert.ok(deployment);
  delete deployment.deploymentRequirement;
  assert.throws(() => validateCoverage(source, missing, root), /deployment obligation/);
});
