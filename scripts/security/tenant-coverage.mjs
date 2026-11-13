import { createHash } from 'node:crypto';
import { readFileSync, existsSync, mkdirSync, writeFileSync, appendFileSync } from 'node:fs';
import { resolve, relative } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

export const sourceUrl =
  'https://raw.githubusercontent.com/OWASP/CheatSheetSeries/master/cheatsheets/Multi_Tenant_Security_Cheat_Sheet.md';
export const canonical = (text) => text.replaceAll('\r\n', '\n');
export const digest = (text) => createHash('sha256').update(canonical(text)).digest('hex');

// Preserve every prose block, list point, strategy row and complete code example.
// Presentation-only markup/headings and the bibliography are not security controls.
export function sourceUnits(source) {
  const lines = canonical(source).split('\n');
  const units = [];
  let heading = '';
  let section = '';
  let index = 0;
  while (index < lines.length) {
    const line = lines[index];
    if (line.startsWith('## References')) break;
    if (/^#{1,6} /.test(line)) {
      heading = section = line.replace(/^#+ /, '');
      index++;
      continue;
    }
    const instruction = /^\*\*(Do|Don't):\*\*$/.exec(line);
    if (instruction) {
      heading = section + ' / ' + instruction[1];
      index++;
      continue;
    }
    if (!line.trim() || /^<\/?details>/.test(line)) {
      index++;
      continue;
    }
    const start = index;
    let kind = 'prose';
    if (line.startsWith('```')) {
      kind = 'example';
      index++;
      while (index < lines.length && !lines[index].startsWith('```')) index++;
      if (index === lines.length) throw new Error('Unclosed source example.');
      index++;
    } else if (line.startsWith('- ')) {
      kind = 'requirement';
      index++;
      while (index < lines.length && /^ {2,}\S/.test(lines[index])) index++;
    } else if (line.startsWith('|')) {
      kind = 'strategy';
      index++;
    } else if (line.startsWith('<summary>')) {
      kind = 'example-title';
      index++;
    } else {
      index++;
      while (index < lines.length && lines[index].trim() && !/^(#|- |```|<|\|)/.test(lines[index]))
        index++;
    }
    const text = lines.slice(start, index).join('\n');
    units.push({
      id: 'MT-' + String(units.length + 1).padStart(3, '0'),
      heading,
      kind,
      start: start + 1,
      end: index,
      sha256: digest(text),
      text,
    });
  }
  return units;
}

export function validateCoverage(source, catalog, root) {
  if (catalog.source.sha256 !== digest(source))
    throw new Error('Pinned OWASP source digest changed.');
  const units = sourceUnits(source);
  if (catalog.controls.length !== units.length)
    throw new Error('Every source unit needs a control.');
  const seen = new Set();
  for (const unit of units) {
    const control = catalog.controls.find((item) => item.id === unit.id);
    if (!control || seen.has(control.id) || control.sha256 !== unit.sha256)
      throw new Error('Missing, duplicated or changed requirement: ' + unit.id);
    const requirement =
      unit.kind === 'example' ? 'Complete illustrative example; see pinned source.' : unit.text;
    if (
      control.start !== unit.start ||
      control.end !== unit.end ||
      control.kind !== unit.kind ||
      control.heading !== unit.heading ||
      control.requirement !== requirement
    )
      throw new Error('Source text or location was misrepresented: ' + unit.id);
    seen.add(control.id);
    if (
      !['repository', 'deployment', 'repository-and-deployment', 'not-applicable'].includes(
        control.status,
      )
    )
      throw new Error('Invalid coverage status: ' + unit.id);
    if (!control.reason || control.reason.length < 30 || !control.group)
      throw new Error('Explain the implementation/applicability of ' + unit.id);
    const group = catalog.groups[control.group];
    if (!group || !group.implementation?.length || !group.tests?.length)
      throw new Error('Concrete source and test evidence required: ' + unit.id);
    if (control.status.includes('deployment') && !control.deploymentRequirement)
      throw new Error('Missing deployment obligation: ' + unit.id);
    for (const file of [...group.implementation, ...group.tests]) {
      const location = resolve(root, file);
      const local = relative(root, location);
      if (local.startsWith('..') || !existsSync(location))
        throw new Error('Missing or unsafe evidence: ' + file);
    }
  }
  return units;
}

async function main() {
  const root = fileURLToPath(new URL('../../', import.meta.url));
  const source = readFileSync(resolve(root, 'docs/security/vendor/owasp-multi-tenant.md'), 'utf8');
  const catalog = JSON.parse(
    readFileSync(resolve(root, 'docs/security/tenant-controls.json'), 'utf8'),
  );
  const units = validateCoverage(source, catalog, root);
  if (process.argv.includes('--check-upstream')) {
    const response = await fetch(sourceUrl, { signal: AbortSignal.timeout(30_000) });
    if (!response.ok) throw new Error('Cannot verify current OWASP article: ' + response.status);
    if (digest(await response.text()) !== catalog.source.sha256)
      throw new Error(
        'OWASP article changed. Review the complete new source and update every affected control.',
      );
  }
  const deployment = catalog.controls.filter((row) => row.status.includes('deployment'));
  const report = [
    '# OWASP tenant security coverage',
    '',
    `${units.length} source units accounted for, including all list points, prose, tables and examples.`,
    'This inventory checks traceability. Passing tests supply behavioral evidence; this is not proof of production configuration or complete vulnerability absence.',
    '',
    `**Deployment evidence still required for ${deployment.length} source units.**`,
    'See docs/security/tenant-security.md for rollout, retention, encryption, monitoring and restore requirements.',
    '',
    '| Control | Source lines | Status | Evidence group |',
    '| --- | --- | --- | --- |',
    ...catalog.controls.map(
      (row) => `| ${row.id} | ${row.start}–${row.end} | ${row.status} | ${row.group} |`,
    ),
    '',
  ].join('\n');
  mkdirSync(resolve(root, '.e2e/security'), { recursive: true });
  writeFileSync(resolve(root, '.e2e/security/coverage.md'), report);
  if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, report);
  console.log(
    `OWASP coverage: ${units.length} source units; ${deployment.length} require deployment evidence.`,
  );
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) await main();
