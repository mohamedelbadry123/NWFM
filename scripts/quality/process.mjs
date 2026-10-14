import { spawnSync } from 'node:child_process';

// npm and npx are .cmd shims on Windows, which only a shell can start.
const viaShell = (command) =>
  process.platform === 'win32' && (command === 'npm' || command === 'npx');
const quote = (arg) => (/[\s"]/.test(arg) ? `"${arg.replaceAll('"', '\\"')}"` : arg);

export function run(command, args, options = {}) {
  const result = viaShell(command)
    ? spawnSync([command, ...args].map(quote).join(' '), {
        stdio: 'inherit',
        shell: true,
        ...options,
      })
    : spawnSync(command, args, { stdio: 'inherit', ...options });
  if (result.error) throw result.error;
  if (result.status !== 0)
    throw new Error(`${command} failed (exit ${result.status ?? result.signal})`);
  return result;
}
