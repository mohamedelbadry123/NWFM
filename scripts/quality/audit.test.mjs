import test from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { auditDependencies } from './audit.mjs';
import { packageCommand } from './process.mjs';

const clean = {
  version: 1,
  sources: ['https://api.nuget.org/v3/index.json'],
  projects: [{ path: 'clean.csproj', frameworks: [{}] }],
};

function audit(results) {
  const calls = [];
  const errors = [];
  const status = auditDependencies({
    command: (command, args) =>
      packageCommand(command, args, {
        platform: 'win32',
        execPath: 'C:\\Node runtime\\node.exe',
        npmExecPath: 'C:\\npm install\\bin\\npm-cli.js',
      }),
    spawn: (command, args, options) => {
      calls.push({ command, args, options });
      return results[calls.length - 1];
    },
    logError: (message) => errors.push(message),
  });
  assert.equal(calls.length, 3, 'Every invocation must audit both npm trees and NuGet');
  for (const [index, directory] of ['.', 'frontend'].entries()) {
    assert.equal(calls[index].command, 'C:\\Node runtime\\node.exe');
    assert.deepEqual(calls[index].args, [
      'C:\\npm install\\bin\\npm-cli.js',
      '--prefix',
      directory,
      'audit',
      '--audit-level=high',
    ]);
    assert.equal(calls[index].options.shell, undefined);
  }
  assert.equal(calls[2].command, 'dotnet');
  assert.deepEqual(calls[2].args, [
    'list',
    'backend/NWFM.sln',
    'package',
    '--vulnerable',
    '--include-transitive',
    '--format',
    'json',
  ]);
  return { status, errors };
}

test('dependency gate audits both npm trees and NuGet and accepts clean or moderate reports', () => {
  const report = structuredClone(clean);
  report.projects[0].frameworks[0].transitivePackages = [
    { id: 'moderate', vulnerabilities: [{ severity: 'Moderate' }] },
  ];
  assert.equal(
    audit([{ status: 0 }, { status: 0 }, { status: 0, stdout: JSON.stringify(report) }]).status,
    0,
  );
});

test('either npm high/critical exit, spawn error, or signal fails the combined gate', () => {
  for (const index of [0, 1]) {
    for (const failure of [
      { status: 1 },
      { error: new Error('npm unavailable') },
      { status: null, signal: 'SIGTERM' },
    ]) {
      const results = [{ status: 0 }, { status: 0 }, { status: 0, stdout: JSON.stringify(clean) }];
      results[index] = failure;
      assert.equal(audit(results).status, 1);
    }
  }
});

test('NuGet high/critical findings, scanner errors and invalid JSON fail after both npm audits', () => {
  const failures = [
    { status: 1, stderr: 'feed unavailable' },
    { error: new Error('dotnet unavailable') },
    { status: 0, stdout: 'not JSON' },
    { status: 0, stdout: JSON.stringify({ ...clean, logs: [{ level: 'warning' }] }) },
  ];
  for (const severity of ['High', 'Critical']) {
    for (const kind of ['topLevelPackages', 'transitivePackages']) {
      const report = structuredClone(clean);
      report.projects[0].frameworks[0][kind] = [{ id: 'unsafe', vulnerabilities: [{ severity }] }];
      failures.push({ status: 0, stdout: JSON.stringify(report) });
    }
  }
  for (const failure of failures) {
    const result = audit([{ status: 0 }, { status: 0 }, failure]);
    assert.equal(result.status, 1);
    assert.equal(result.errors.length, 1);
  }
});

test('npm/npx resolve to actual Node CLI arguments on Windows, including paths with spaces', () => {
  const args = ['--prefix', 'a path & literal', '--version'];
  for (const command of ['npm', 'npx']) {
    assert.deepEqual(
      packageCommand(command, args, {
        platform: 'win32',
        execPath: 'C:\\Node runtime\\node.exe',
        npmExecPath: null,
      }),
      [
        'C:\\Node runtime\\node.exe',
        [`C:\\Node runtime\\node_modules\\npm\\bin\\${command}-cli.js`, ...args],
      ],
    );
    assert.deepEqual(packageCommand(command, args, { platform: 'linux' }), [command, args]);
    const [executable, executableArgs] = packageCommand(command, ['--version']);
    const result = spawnSync(executable, executableArgs, { encoding: 'utf8' });
    assert.equal(result.status, 0, result.error?.message || result.stderr);
    assert.match(result.stdout.trim(), /^\d+\.\d+\.\d+$/);
  }
});
