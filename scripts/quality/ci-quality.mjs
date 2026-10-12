import { readFileSync } from 'node:fs';
import { run } from './process.mjs';
const event = JSON.parse(readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));
const base = event.pull_request?.base.sha ?? event.before;
if (!/^[0-9a-f]{40}$/.test(base ?? '') || /^0+$/.test(base))
  throw new Error('No valid base revision for changed-file checks.');
run(process.execPath, ['scripts/quality/check-files.mjs', '--base', base]);
