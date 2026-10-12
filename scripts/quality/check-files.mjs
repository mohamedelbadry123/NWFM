import { execFileSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import prettier from 'prettier';
import { run } from './process.mjs';

const root = fileURLToPath(new URL('../../', import.meta.url));
process.chdir(root);
const args = process.argv.slice(2);
const write = args.includes('--write');
let files;
const baseIndex = args.indexOf('--base');
if (baseIndex >= 0) {
  const base = args[baseIndex + 1];
  if (!/^[0-9a-f]{40,64}$/.test(base ?? ''))
    throw new Error('--base requires an immutable commit SHA.');
  files = execFileSync(
    'git',
    ['diff', '--name-only', '-z', '--diff-filter=ACMR', base + '...HEAD'],
    { encoding: 'utf8' },
  )
    .split('\0')
    .filter(Boolean);
} else {
  files = args.filter((arg) => arg !== '--write');
}
files = files.map((file) => path.relative(root, path.resolve(root, file)).replaceAll('\\', '/'));
if (files.some((file) => file.startsWith('../') || path.isAbsolute(file)))
  throw new Error('Files must be inside this checkout.');
const excluded =
  /(^|\/)(node_modules|bin|obj|dist|coverage|TestResults|\.angular|\.work|\.e2e)(\/|$)|^\.agents\/skills\/e2e\/|^\.claude\/|\/Persistence\/Migrations\/|^frontend\/src\/app\/shared\/(models|services)\//;
files = [...new Set(files)].filter((file) => existsSync(file) && !excluded.test(file));
const formatted = [];
for (const file of files) {
  const info = await prettier.getFileInfo(file, { ignorePath: '.prettierignore' });
  if (!info.ignored && info.inferredParser) formatted.push(file);
}
const chunks = (list) =>
  Array.from({ length: Math.ceil(list.length / 50) }, (_, index) =>
    list.slice(index * 50, (index + 1) * 50),
  );
for (const group of chunks(formatted))
  run(process.execPath, [
    'node_modules/prettier/bin/prettier.cjs',
    write ? '--write' : '--check',
    '--',
    ...group,
  ]);
const linted = files.filter(
  (file) => /\.(?:[cm]?js|ts)$/.test(file) || /^frontend\/.*\.html$/.test(file),
);
for (const group of chunks(linted))
  run(process.execPath, [
    'node_modules/eslint/bin/eslint.js',
    '--max-warnings=0',
    ...(write ? ['--fix'] : []),
    '--',
    ...group,
  ]);
const csharp = files.filter((file) => file.endsWith('.cs'));
if (csharp.length) {
  run('dotnet', [
    'format',
    'backend/NWFM.sln',
    ...(write ? [] : ['--verify-no-changes']),
    '--include',
    ...csharp,
  ]);
}
