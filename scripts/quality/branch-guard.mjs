import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';

export function assertBranchAllowed(mode, branch, input = '') {
  const protectedRefs = new Set(['refs/heads/main', 'refs/heads/develop']);
  if (mode === 'commit' && (!branch || protectedRefs.has('refs/heads/' + branch))) {
    throw new Error('Commit on a feature branch; main, develop, and detached HEAD are protected.');
  }
  if (mode === 'push') {
    for (const line of input.trim().split(/\r?\n/).filter(Boolean)) {
      const fields = line.split(/\s+/);
      if (fields.length !== 4) throw new Error('Could not validate push refs.');
      if (protectedRefs.has(fields[2])) {
        throw new Error(
          'Direct pushes to main/develop are blocked. Open a PR and merge in GitHub.',
        );
      }
    }
  }
}
if (process.argv[1]?.endsWith('branch-guard.mjs')) {
  try {
    const mode = process.argv[2];
    if (!['commit', 'push'].includes(mode)) throw new Error('Expected commit or push.');
    const branch = execFileSync('git', ['branch', '--show-current'], { encoding: 'utf8' }).trim();
    assertBranchAllowed(mode, branch, mode === 'push' ? readFileSync(0, 'utf8') : '');
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
