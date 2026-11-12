import test from 'node:test';
import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';

const project = 'scripts/e2e/ProcessOwner/ProcessOwner.csproj';
const owner = 'scripts/e2e/ProcessOwner/bin/Release/net10.0/ProcessOwner.dll';

function ownedDirectory(root = process.cwd()) {
  const parent = join(root, '.work', 'tmp');
  mkdirSync(parent, { recursive: true });
  return mkdtempSync(join(parent, 'e2e-owner-'));
}

test('owner fixtures create a missing temp parent and keep each directory distinct', () => {
  const root = mkdtempSync(join(tmpdir(), 'nwfm-owner-parent-'));
  try {
    assert.equal(existsSync(join(root, '.work', 'tmp')), false);
    const first = ownedDirectory(root);
    const second = ownedDirectory(root);
    assert.notEqual(first, second);
    assert.equal(existsSync(first), true);
    assert.equal(existsSync(second), true);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
test.before(() => {
  if (process.platform !== 'win32') return;
  const built = spawnSync('dotnet', ['build', project, '-c', 'Release', '--nologo'], {
    encoding: 'utf8',
  });
  assert.equal(built.status, 0, built.stdout + built.stderr);
});

for (const abandoned of [false, true]) {
  test(
    `Windows owner empties its job after ${abandoned ? 'wrapper EOF' : 'leader exit'}, including detached descendants`,
    { skip: process.platform !== 'win32' },
    async () => {
      const directory = ownedDirectory();
      const marker = join(directory, 'child.json');
      const source = `
      const {spawn}=require('node:child_process');
      const fs=require('node:fs');
      const child=spawn(process.execPath,['-e',"process.send('ready');setInterval(()=>{},1000)"],{detached:true,stdio:['ignore','ignore','ignore','ipc']});
      child.once('message',()=>{fs.writeFileSync(process.argv[1],JSON.stringify({pid:child.pid}));console.log('fixture-ready');${abandoned ? '' : 'process.exit(0);'}});
    `;
      const child = spawn(
        'dotnet',
        [
          owner,
          '--process-only',
          process.execPath,
          'scripts/e2e/owned-bootstrap.mjs',
          process.execPath,
          '-e',
          source,
          marker,
        ],
        { stdio: ['pipe', 'pipe', 'pipe'] },
      );
      let output = '';
      let errors = '';
      let deadlineHit = false;
      const deadline = setTimeout(() => {
        deadlineHit = true;
        child.stdin.end();
      }, 15_000);
      child.stdout.on('data', (chunk) => {
        output += chunk;
        if (abandoned && output.includes('fixture-ready')) child.stdin.end();
      });
      child.stderr.on('data', (chunk) => {
        errors += chunk;
      });
      const status = await new Promise((resolve, reject) => {
        child.once('error', reject);
        child.once('close', resolve);
      });
      clearTimeout(deadline);
      try {
        assert.equal(deadlineHit, false, 'Owned process teardown must finish before its deadline');
        if (!abandoned) assert.equal(status, 0, errors);
        else assert.notEqual(status, 0);
        assert.match(output, /E2E_OWNER_JOB_EMPTY/);
        const { pid } = JSON.parse(readFileSync(marker));
        assert.throws(() => process.kill(pid, 0), /ESRCH/);
      } finally {
        child.stdin.destroy();
        rmSync(directory, { recursive: true, force: true });
      }
    },
  );
}
