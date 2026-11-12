import test from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import {
  mkdtempSync,
  mkdirSync,
  writeFileSync,
  readFileSync,
  copyFileSync,
  symlinkSync,
  rmSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
import config from '../../lint-staged.config.mjs';

// Resolve the parser from the installed runner, even when npm nests its dependency.
const { parseArgsStringToArgv } = createRequire(import.meta.resolve('lint-staged'))('string-argv');

const root = fileURLToPath(new URL('../../', import.meta.url));
const { chunkFilesForCommand } = await import(
  new URL('./getSpawnedTasks.js', import.meta.resolve('lint-staged'))
);

test('installed lint-staged prepares 925 paths once, bounded quality batches then one unit suite', async () => {
  const files = Array.from(
    { length: 924 },
    (_, index) =>
      `D:/Work/NWFM/.work/designer-security-integration/frontend/src/app/shared/models/folder with spaces & literal/generated-client-${index}.ts`,
  );
  // Deleted paths still reach quality (which ignores missing files) and cannot
  // suppress the full test suite. Match lint-staged's normalized absolute paths.
  files.push('D:/Work/NWFM/.work/designer-security-integration/deleted file.json');
  const original = [...files];
  let evaluations = 0;
  const commands = await chunkFilesForCommand(
    (received) => {
      evaluations++;
      return config(received);
    },
    files,
    8191,
  );
  assert.equal(evaluations, 1, 'Oversized commands cause recursive config evaluation');
  assert.deepEqual(files, original);
  assert.ok(commands.length > 2, 'The fixture must require multiple quality batches');
  assert.equal(commands.at(-1).command, 'npm run test:unit');
  assert.equal(commands.filter(({ command }) => command === 'npm run test:unit').length, 1);
  const received = [];
  for (const { command, files: appended } of commands.slice(0, -1)) {
    assert.ok(command.length <= 7000, `Command has ${command.length} characters`);
    assert.deepEqual(appended, [], 'Function commands must not append filenames again');
    const [node, script, write, ...paths] = parseArgsStringToArgv(command);
    assert.deepEqual([node, script, write], ['node', 'scripts/quality/check-files.mjs', '--write']);
    assert.ok(paths.length > 0);
    received.push(...paths);
  }
  assert.deepEqual(
    received,
    original,
    'Every full path, including spaces and deletion, occurs once',
  );
  const deletion = await chunkFilesForCommand(config, [files.at(-1)], 8191);
  assert.equal(deletion.at(-1).command, 'npm run test:unit');
});

test('single oversized quoted path fails preparation instead of being omitted or spawned', async () => {
  const prefix = 'node scripts/quality/check-files.mjs --write';
  const boundary = 'D:/' + 'x'.repeat(7000 - prefix.length - 1 - 2 - 3);
  assert.equal(config([boundary])[0].length, 7000);
  await assert.rejects(chunkFilesForCommand(config, [boundary + 'x'], 8191), /path exceeds/);
  const file = 'D:\\' + 'folder\\'.repeat(900);
  assert.ok(
    file.length < 7000,
    'JSON escaping, rather than raw path length, must exceed the limit',
  );
  await assert.rejects(chunkFilesForCommand(config, [file], 8191), /path exceeds/);
  for (const unusual of ['/tmp/quote"name.ts', '/tmp/back\\slash.ts', '/tmp/new\nline.ts'])
    await assert.rejects(chunkFilesForCommand(config, [unusual], 8191), /unsupported path/);
});

test('real lint-staged writes spaced filenames and runs units once; formatting failure blocks units', () => {
  const fixture = mkdtempSync(path.join(tmpdir(), 'nwfm-hook-batching-'));
  const fixtureEnv = Object.fromEntries(
    Object.entries(process.env).filter(([name]) => !name.toUpperCase().startsWith('GIT_')),
  );
  const execute = (command, args) =>
    spawnSync(command, args, { cwd: fixture, encoding: 'utf8', env: fixtureEnv });
  const git = (...args) => execute('git', args);
  const ok = (result) => assert.equal(result.status, 0, result.stdout + result.stderr);
  const run = () =>
    execute(process.execPath, [
      path.join(root, 'node_modules/lint-staged/bin/lint-staged.js'),
      '--diff-filter=ACMRD',
      '--hide-unstaged',
      '--concurrent',
      'false',
      '--max-arg-length',
      '8191',
    ]);
  try {
    ok(git('init', '-b', 'feature/hook-batching'));
    ok(git('config', 'user.name', 'Hook verifier'));
    ok(git('config', 'user.email', 'hooks@example.test'));
    mkdirSync(path.join(fixture, 'scripts/quality'), { recursive: true });
    for (const file of ['check-files.mjs', 'process.mjs'])
      copyFileSync(
        path.join(root, 'scripts/quality', file),
        path.join(fixture, 'scripts/quality', file),
      );
    for (const file of [
      'lint-staged.config.mjs',
      '.prettierrc.json',
      '.prettierignore',
      '.gitattributes',
    ])
      copyFileSync(path.join(root, file), path.join(fixture, file));
    symlinkSync(
      path.join(root, 'node_modules'),
      path.join(fixture, 'node_modules'),
      process.platform === 'win32' ? 'junction' : 'dir',
    );
    writeFileSync(path.join(fixture, '.gitignore'), 'node_modules/\nunit-runs.txt\n');
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
      "require('node:fs').appendFileSync('unit-runs.txt', 'unit\\n');\n",
    );
    ok(git('add', '.'));
    ok(git('commit', '-m', 'chore: initialize batching fixture'));
    const folder = 'long folder ' + 'x'.repeat(70);
    mkdirSync(path.join(fixture, folder));
    const names = Array.from({ length: 80 }, (_, index) =>
      path.join(folder, `file ${String(index).padStart(3, '0')} with spaces & literal.json`),
    );
    for (const name of names) writeFileSync(path.join(fixture, name), '{"value":1}\n');
    assert.ok(
      config(names.map((name) => path.join(fixture, name).replaceAll('\\', '/'))).length > 2,
    );
    ok(git('add', '--', folder));
    ok(run());
    for (const name of names)
      assert.equal(readFileSync(path.join(fixture, name), 'utf8'), '{ "value": 1 }\n');
    assert.equal(readFileSync(path.join(fixture, 'unit-runs.txt'), 'utf8'), 'unit\n');
    const name = names.at(-1);
    writeFileSync(path.join(fixture, name), '{ invalid json\n');
    ok(git('add', '--', name));
    const failed = run();
    assert.notEqual(failed.status, 0);
    assert.match(failed.stdout + failed.stderr, /SyntaxError/);
    assert.equal(readFileSync(path.join(fixture, 'unit-runs.txt'), 'utf8'), 'unit\n');
    const staged = git('show', ':' + name.replaceAll('\\', '/'));
    ok(staged);
    assert.match(staged.stdout, /invalid json/);
    // A failure in the first batch must also stop later formatting and the suite.
    writeFileSync(path.join(fixture, names[0]), '{ invalid early json\n');
    writeFileSync(path.join(fixture, name), '{"value":2}\n');
    ok(git('add', '--', folder));
    assert.notEqual(run().status, 0);
    assert.equal(readFileSync(path.join(fixture, 'unit-runs.txt'), 'utf8'), 'unit\n');
    assert.equal(readFileSync(path.join(fixture, name), 'utf8'), '{"value":2}\n');
  } finally {
    // fixture is exactly the disposable directory created above.
    rmSync(fixture, { recursive: true, force: true });
  }
});
