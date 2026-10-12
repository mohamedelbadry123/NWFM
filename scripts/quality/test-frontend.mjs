import { createRequire } from 'node:module';
import { existsSync } from 'node:fs';
import { run } from './process.mjs';
const { chromium } = createRequire(import.meta.resolve('@e2e-dev/web'))('playwright-core');
const chrome = process.env.CHROME_BIN || chromium.executablePath();
if (!existsSync(chrome))
  throw new Error('Run npx @e2e-dev/web install chromium --with-deps first.');
run('npm', ['--prefix', 'frontend', 'test', '--', '--watch=false', '--browsers=ChromeHeadless'], {
  env: { ...process.env, CHROME_BIN: chrome },
});
