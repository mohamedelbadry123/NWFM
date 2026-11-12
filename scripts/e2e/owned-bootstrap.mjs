import { spawn } from 'node:child_process';

// Do not create any descendants until the native owner has assigned this process
// to its kill-on-close job. No PID lookup or taskkill is involved.
process.stdin.once('data', (admission) => {
  if (admission.toString().trim() !== 'owned') process.exit(3);
  const child = spawn(process.argv[2], process.argv.slice(3), { stdio: 'inherit' });
  child.once('error', () => process.exit(3));
  child.once('close', (code) => process.exit(code ?? 3));
});
