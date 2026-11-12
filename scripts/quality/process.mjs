import { spawnSync } from 'node:child_process';
import path from 'node:path';

// Windows cannot spawn npm.cmd/npx.cmd without a shell. Invoke the installed
// JavaScript CLI with Node so paths and arguments never become shell text.
export function packageCommand(
  command,
  args,
  {
    platform = process.platform,
    execPath = process.execPath,
    npmExecPath = process.env.npm_execpath,
  } = {},
) {
  if (platform !== 'win32' || !['npm', 'npx'].includes(command)) return [command, args];
  const cliDirectory = npmExecPath
    ? path.win32.dirname(npmExecPath)
    : path.win32.join(path.win32.dirname(execPath), 'node_modules', 'npm', 'bin');
  return [execPath, [path.win32.join(cliDirectory, command + '-cli.js'), ...args]];
}

export function run(command, args, options = {}) {
  const [executable, executableArgs] = packageCommand(command, args);
  const result = spawnSync(executable, executableArgs, { stdio: 'inherit', ...options });
  if (result.error) throw result.error;
  if (result.status !== 0)
    throw new Error(`${command} failed (exit ${result.status ?? result.signal})`);
  return result;
}
