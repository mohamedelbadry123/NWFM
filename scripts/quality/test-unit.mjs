import { createRequire } from 'node:module';
import { existsSync } from 'node:fs';
import { run } from './process.mjs';
const requireWeb = createRequire(import.meta.resolve('@e2e-dev/web'));
const { chromium } = requireWeb('playwright-core');
const chrome = process.env.CHROME_BIN || chromium.executablePath();
if (!existsSync(chrome))
  throw new Error('Install Chromium first: npx @e2e-dev/web install chromium --with-deps');
run('dotnet', ['test', 'backend/NWFM.sln', '--configuration', 'Release', '--logger', 'trx']);
run('npm', ['--prefix', 'frontend', 'test', '--', '--watch=false', '--browsers=ChromeHeadless'], {
  env: { ...process.env, CHROME_BIN: chrome },
});
