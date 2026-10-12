import { readFileSync } from 'node:fs';
import { execFileSync, spawnSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

export function validateBody(body) {
  const required = ['Description', 'Issue Ticket', 'Type of Change', 'Testing', 'Review'];
  const sections = new Map(
    [...body.matchAll(/^## (.+)\r?\n([\s\S]*?)(?=^## |$(?![\s\S]))/gm)].map((match) => [
      match[1].trim(),
      match[2].replace(/<!--[\s\S]*?-->/g, '').trim(),
    ]),
  );
  for (const heading of required) {
    if (!sections.get(heading)) throw new Error('Complete the PR section: ' + heading);
  }
  if (
    !/(?:#\d+|https:\/\/github\.com\/[^\s]+\/issues\/\d+|Not applicable:\s*\S.{15,})/i.test(
      sections.get('Issue Ticket'),
    )
  )
    throw new Error('Link an issue or explain "Not applicable:" (at least 16 characters).');
  if (
    !/^- \[[xX]\] (Bug fix|Feature|Breaking change|Maintenance\/tooling)\s*$/m.test(
      sections.get('Type of Change'),
    )
  )
    throw new Error('Select a type of change.');
  for (const heading of ['Testing', 'Review']) {
    const section = sections.get(heading);
    if (!/^- \[[xX]\] /m.test(section) || /^- \[ \] /m.test(section))
      throw new Error('Complete every checkbox in ' + heading + '.');
  }
}
export function lintMessage(message) {
  const result = spawnSync(process.execPath, ['node_modules/@commitlint/cli/cli.js'], {
    input: message,
    encoding: 'utf8',
  });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(result.stdout + result.stderr);
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const event = JSON.parse(readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));
  if (!event.pull_request) {
    console.log('PR policy applies to pull requests.');
    process.exit(0);
  }
  validateBody(event.pull_request.body ?? '');
  lintMessage(event.pull_request.title);
  const { sha: base } = event.pull_request.base;
  const { sha: head } = event.pull_request.head;
  if (![base, head].every((sha) => /^[0-9a-f]{40}$/.test(sha)))
    throw new Error('Invalid PR revision.');
  const commits = execFileSync('git', ['rev-list', base + '..' + head], { encoding: 'utf8' })
    .trim()
    .split('\n')
    .filter(Boolean);
  if (!commits.length) throw new Error('No PR commits found.');
  for (const sha of commits)
    lintMessage(execFileSync('git', ['show', '-s', '--format=%B', sha], { encoding: 'utf8' }));
  console.log('PR documentation, title and commit messages passed.');
}
