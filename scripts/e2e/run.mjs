import {
  cpSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmdirSync,
  unlinkSync,
  writeFileSync,
} from 'node:fs';
import { createHash, randomUUID } from 'node:crypto';
import { tmpdir } from 'node:os';
import { join, relative, resolve } from 'node:path';
import { verifyReplay } from './verify-replay.mjs';
import { spawn, spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { packageCommand } from '../quality/process.mjs';
// The app's existing development seed creates this account only in the fresh,
// disposable database owned by start-app. It is never an external credential.
const env = {
  ...process.env,
  NWFM_TEST_PASSWORD: process.env.NWFM_TEST_PASSWORD || 'Administrator1!',
  E2E_TELEMETRY_DISABLED: '1',
};
if (process.platform === 'win32') env.NWFM_E2E_OWNER_RUN_ID = randomUUID();
const replay = env.NWFM_E2E_REPLAY === '1' || (!!env.CI && !['0', 'false'].includes(env.CI));
const auditRoot = env.NWFM_E2E_AUDIT_DIR ? resolve(env.NWFM_E2E_AUDIT_DIR) : undefined;
if (
  auditRoot &&
  (relative(resolve('.work'), auditRoot).startsWith('..') ||
    relative(resolve('.work'), auditRoot).includes(':'))
)
  throw new Error('E2E audit output must stay inside this worktree .work directory.');
const receiptDirectory = replay ? mkdtempSync(join(tmpdir(), 'nwfm-e2e-receipt-')) : undefined;
const previousReport = existsSync('.e2e/report.json')
  ? readFileSync('.e2e/report.json', 'utf8')
  : undefined;
if (receiptDirectory) {
  env.NWFM_E2E_RECEIPT_PATH = join(receiptDirectory, 'receipt.json');
  env.NWFM_E2E_RECEIPT_NONCE = randomUUID();
}
let [executable, args] = packageCommand('npx', [
  '--no-install',
  'e2e',
  'run',
  ...process.argv.slice(2),
]);
if (process.platform === 'win32') {
  const built = spawnSync(
    'dotnet',
    [
      'build',
      'scripts/e2e/ProcessOwner/ProcessOwner.csproj',
      '--configuration',
      'Release',
      '--nologo',
    ],
    { env, stdio: 'inherit' },
  );
  if (built.error || built.status !== 0)
    throw new Error('Cannot build the Windows e2e process owner.');
  args = [
    'scripts/e2e/ProcessOwner/bin/Release/net10.0/ProcessOwner.dll',
    process.execPath,
    'scripts/e2e/owned-bootstrap.mjs',
    executable,
    ...args,
  ];
  executable = 'dotnet';
}
const child = spawn(executable, args, {
  cwd: fileURLToPath(new URL('../../', import.meta.url)),
  env,
  stdio: process.platform === 'win32' ? ['pipe', 'inherit', 'inherit'] : 'inherit',
});
for (const signal of ['SIGINT', 'SIGTERM'])
  process.on(signal, () => {
    if (process.platform === 'win32') child.stdin?.write('stop\n');
    else child.kill(signal);
  });
child.on('error', (error) => {
  console.error(error.message);
  process.exitCode = 1;
});
child.on('close', (code) => {
  process.exitCode = code ?? 1;
  let verified = false;
  if (code === 0 && replay) {
    try {
      verifyReplay(
        JSON.parse(readFileSync(new URL('../../.e2e/report.json', import.meta.url), 'utf8')),
        {
          nonce: env.NWFM_E2E_RECEIPT_NONCE,
          receipt: JSON.parse(readFileSync(env.NWFM_E2E_RECEIPT_PATH, 'utf8')),
        },
      );
      verified = true;
    } catch (error) {
      console.error(error.message);
      process.exitCode = 1;
    }
  }
  if (auditRoot) {
    const directory = join(auditRoot, env.NWFM_E2E_OWNER_RUN_ID ?? randomUUID());
    mkdirSync(directory, { recursive: true });
    const reportWritten =
      existsSync('.e2e/report.json') && readFileSync('.e2e/report.json', 'utf8') !== previousReport;
    for (const name of reportWritten ? ['report.json', 'junit.xml', 'results', 'logs'] : ['logs']) {
      const source = resolve('.e2e', name);
      if (existsSync(source)) cpSync(source, join(directory, name), { recursive: true });
    }
    const receipt =
      receiptDirectory && existsSync(env.NWFM_E2E_RECEIPT_PATH)
        ? JSON.parse(readFileSync(env.NWFM_E2E_RECEIPT_PATH, 'utf8'))
        : null;
    if (receipt) cpSync(env.NWFM_E2E_RECEIPT_PATH, join(directory, 'receipt.json'));
    const report = reportWritten ? JSON.parse(readFileSync('.e2e/report.json', 'utf8')) : null;
    writeFileSync(
      join(directory, 'verification.json'),
      JSON.stringify(
        {
          childExitCode: code,
          wrapperExitCode: process.exitCode,
          fullReplayVerified: verified,
          reportWritten,
          freshReceiptMatches:
            !!receipt &&
            receipt.nonce === env.NWFM_E2E_RECEIPT_NONCE &&
            receipt.runId === report?.run?.id &&
            receipt.digest === createHash('sha256').update(JSON.stringify(report)).digest('hex'),
          modelTokens: report?.run?.usage?.modelTokens ?? null,
        },
        null,
        2,
      ),
    );
    console.log('E2E audit artifacts: ' + directory);
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
