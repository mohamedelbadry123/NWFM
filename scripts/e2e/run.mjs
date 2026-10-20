import { mkdtempSync, readFileSync, rmdirSync, unlinkSync } from 'node:fs';
import { randomUUID } from 'node:crypto';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { verifyReplay } from './verify-replay.mjs';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
// The app's existing development seed creates this account only in the fresh,
// disposable database owned by start-app. It is never an external credential.
const env = {
  ...process.env,
  NWFM_TEST_PASSWORD: process.env.NWFM_TEST_PASSWORD || 'Administrator1!',
  E2E_TELEMETRY_DISABLED: '1',
};
const replay = env.NWFM_E2E_REPLAY === '1' || (!!env.CI && !['0', 'false'].includes(env.CI));
const receiptDirectory = replay ? mkdtempSync(join(tmpdir(), 'nwfm-e2e-receipt-')) : undefined;
if (receiptDirectory) {
  env.NWFM_E2E_RECEIPT_PATH = join(receiptDirectory, 'receipt.json');
  env.NWFM_E2E_RECEIPT_NONCE = randomUUID();
}
const child = spawn('npx', ['--no-install', 'e2e', 'run', ...process.argv.slice(2)], {
  cwd: fileURLToPath(new URL('../../', import.meta.url)),
  env,
  stdio: 'inherit',
});
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => child.kill(signal));
child.on('error', (error) => {
  console.error(error.message);
  process.exitCode = 1;
});
child.on('close', (code) => {
  process.exitCode = code ?? 1;
  if (code === 0 && replay) {
    try {
      verifyReplay(
        JSON.parse(readFileSync(new URL('../../.e2e/report.json', import.meta.url), 'utf8')),
        {
          nonce: env.NWFM_E2E_RECEIPT_NONCE,
          receipt: JSON.parse(readFileSync(env.NWFM_E2E_RECEIPT_PATH, 'utf8')),
        },
      );
    } catch (error) {
      console.error(error.message);
      process.exitCode = 1;
    }
  }
  if (receiptDirectory) {
    try {
      unlinkSync(env.NWFM_E2E_RECEIPT_PATH);
    } catch (error) {
      if (error.code !== 'ENOENT') throw error;
    }
    rmdirSync(receiptDirectory);
  }
});
