import test from 'node:test';
import assert from 'node:assert/strict';
import {
  mkdtempSync,
  mkdirSync,
  writeFileSync,
  readFileSync,
  copyFileSync,
  chmodSync,
  symlinkSync,
  rmSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
const root = fileURLToPath(new URL('../../', import.meta.url));
test('real hooks preserve partial staging, reject lint and test failures, and enforce commit messages', () => {
  const fixture = mkdtempSync(path.join(tmpdir(), 'nwfm-hooks-'));
  const execute = (command, args) =>
    spawnSync(command, args, {
      cwd: fixture,
      encoding: 'utf8',
      env: { ...process.env, HUSKY: '1' },
    });
  const git = (...args) => execute('git', args);
  const ok = (result) => assert.equal(result.status, 0, result.stdout + result.stderr);
  try {
    ok(git('init', '-b', 'feature/hooks'));
    ok(git('config', 'user.name', 'Hook verifier'));
    ok(git('config', 'user.email', 'hooks@example.test'));
    mkdirSync(path.join(fixture, 'scripts/quality'), { recursive: true });
    for (const file of ['check-files.mjs', 'process.mjs', 'branch-guard.mjs'])
      copyFileSync(
        path.join(root, 'scripts/quality', file),
        path.join(fixture, 'scripts/quality', file),
      );
    for (const file of [
      'lint-staged.config.mjs',
      'commitlint.config.cjs',
      '.prettierrc.json',
      '.prettierignore',
      'eslint.config.mjs',
    ])
      copyFileSync(path.join(root, file), path.join(fixture, file));
    symlinkSync(path.join(root, 'node_modules'), path.join(fixture, 'node_modules'), 'dir');
    writeFileSync(path.join(fixture, '.gitignore'), 'node_modules/\n');
    writeFileSync(
      path.join(fixture, 'package.json'),
      JSON.stringify({
        private: true,
        type: 'module',
        scripts: { 'test:unit': 'node verify.cjs' },
      }),
    );
    writeFileSync(
      path.join(fixture, 'verify.cjs'),
      "if (JSON.parse(require('fs').readFileSync('sample.json')).fail) process.exit(1);\n",
    );
    writeFileSync(
      path.join(fixture, 'sample.json'),
      JSON.stringify({ value: 1, fail: false }, null, 2) + '\n',
    );
    writeFileSync(path.join(fixture, 'disposable.json'), '{}\n');
    ok(git('add', '.'));
    ok(git('commit', '-m', 'chore: initialize hook fixture'));
    mkdirSync(path.join(fixture, '.husky'));
    for (const name of ['pre-commit', 'commit-msg', 'pre-push']) {
      const target = path.join(fixture, '.husky', name);
      writeFileSync(
        target,
        '#!/bin/sh\nset -e\n' + readFileSync(path.join(root, '.husky', name), 'utf8'),
      );
      chmodSync(target, 0o755);
    }
    ok(git('config', 'core.hooksPath', '.husky'));
    writeFileSync(
      path.join(fixture, 'sample.json'),
      JSON.stringify({ value: 2, fail: false }, null, 2) + '\n',
    );
    ok(git('add', 'sample.json'));
    writeFileSync(
      path.join(fixture, 'sample.json'),
      JSON.stringify({ value: 2, fail: true }, null, 2) + '\n',
    );
    ok(git('commit', '-m', 'feat: verify staged snapshot'));
    assert.equal(JSON.parse(git('show', 'HEAD:sample.json').stdout).fail, false);
    assert.equal(JSON.parse(readFileSync(path.join(fixture, 'sample.json'), 'utf8')).fail, true);
    ok(git('add', 'sample.json'));
    const staged = git('show', ':sample.json').stdout;
    assert.notEqual(git('commit', '-m', 'fix: failing unit fixture').status, 0);
    assert.equal(git('show', ':sample.json').stdout, staged);
    assert.equal(JSON.parse(readFileSync(path.join(fixture, 'sample.json'), 'utf8')).fail, true);
    writeFileSync(path.join(fixture, 'sample.json'), '{ "value": 3, "fail": false }\n');
    writeFileSync(path.join(fixture, 'bad.js'), 'const unused = 1;\n');
    ok(git('add', 'sample.json', 'bad.js'));
    assert.notEqual(git('commit', '-m', 'fix: lint failure fixture').status, 0);
    ok(git('rm', '--cached', 'bad.js'));
    rmSync(path.join(fixture, 'bad.js'));
    assert.notEqual(git('commit', '-m', 'fixed stuff').status, 0);
    ok(git('commit', '-m', 'fix: valid fixture'));
    ok(git('rm', 'disposable.json'));
    const deletion = git('commit', '-m', 'chore: remove fixture file');
    ok(deletion);
    assert.match(deletion.stdout + deletion.stderr, /npm run test:unit/);
  } finally {
    // fixture is the exact directory created by mkdtemp, never a computed user path.
    rmSync(fixture, { recursive: true, force: true });
  }
});
