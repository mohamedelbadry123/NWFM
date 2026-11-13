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

test('per-unit hashes, unique identities, exact locations and safe evidence remain mandatory', () => {
  const hash = structuredClone(catalog);
  hash.controls[0].sha256 = '0'.repeat(64);
  assert.throws(() => validateCoverage(source, hash, root), /changed requirement/);
  const duplicate = structuredClone(catalog);
  duplicate.controls[1] = structuredClone(duplicate.controls[0]);
  assert.throws(() => validateCoverage(source, duplicate, root), /requirement/);
  const line = structuredClone(catalog);
  line.controls[0].end++;
  assert.throws(() => validateCoverage(source, line, root), /misrepresented/);
  const group = structuredClone(catalog);
  group.controls[0].group = 'invented-evidence';
  assert.throws(() => validateCoverage(source, group, root), /evidence required/);
  const unsafe = structuredClone(catalog);
  unsafe.groups[unsafe.controls[0].group].implementation = ['../AGENTS.md'];
  assert.throws(() => validateCoverage(source, unsafe, root), /unsafe evidence/);
});

test('expanded requirements retain explicit operational gaps instead of claiming certification', () => {
  // Locate by content: inserting another source unit must not attach evidence to its old ordinal.
  const find = (text) => {
    const unit = sourceUnits(source).find((u) => u.text.includes(text));
    assert.ok(unit, 'Missing expanded requirement: ' + text);
    const control = catalog.controls.find((c) => c.sha256 === unit.sha256);
    assert.ok(control);
    return control;
  };
  for (const text of ['shared counters with atomic updates', 'tamper-evident mechanisms']) {
    const control = find(text);
    assert.equal(control.status, 'deployment');
    assert.ok(control.deploymentRequirement.length > 30);
  }
  const appendOnly = find('Enforce append-only audit behavior');
  assert.equal(appendOnly.status, 'repository-and-deployment');
  assert.match(appendOnly.reason, /not a WORM ledger/);
  assert.match(appendOnly.deploymentRequirement, /every required audit class/);
  assert.equal(find('Authorize the exact tenant, object, and operation').status, 'repository');
  assert.equal(find('Restrict signed URLs to the required object').status, 'not-applicable');
  assert.equal(
    find('Generate tenant API credentials with a cryptographically secure').status,
    'repository-and-deployment',
  );
  assert.match(
    find('Treat onboarding as an all-or-nothing operation').deploymentRequirement,
    /owned by that attempt/,
  );
});
