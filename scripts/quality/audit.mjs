import { spawnSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

export function inspectNuget(report) {
  if (
    report.version !== 1 ||
    !Array.isArray(report.sources) ||
    !report.sources.length ||
    report.logs?.some((log) => ['error', 'warning'].includes(log.level?.toLowerCase())) ||
    !Array.isArray(report.projects) ||
    !report.projects.length ||
    report.errors?.length ||
    report.problems?.length
  )
    throw new Error('NuGet audit returned incomplete results or scanner errors.');
  const findings = [];
  for (const project of report.projects) {
    if (
      !project.path ||
      project.errors?.length ||
      project.problems?.length ||
      (project.frameworks !== undefined && !Array.isArray(project.frameworks))
    )
      throw new Error('NuGet audit failed for ' + project.path);
    for (const framework of project.frameworks ?? []) {
      for (const dependency of [
        ...(framework.topLevelPackages ?? []),
        ...(framework.transitivePackages ?? []),
      ]) {
        for (const vulnerability of dependency.vulnerabilities ?? []) {
          if (['high', 'critical'].includes(vulnerability.severity?.toLowerCase()))
            findings.push(
              dependency.id + ': ' + vulnerability.severity + ' ' + vulnerability.advisoryurl,
            );
        }
      }
    }
  }
  if (findings.length) throw new Error(findings.join('\n'));
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  let failed = false;
  for (const directory of ['.', 'frontend']) {
    const result = spawnSync('npm', ['--prefix', directory, 'audit', '--audit-level=high'], {
      stdio: 'inherit',
    });
    if (result.error || result.status !== 0) failed = true;
  }
  const audit = spawnSync(
    'dotnet',
    [
      'list',
      'backend/NWFM.sln',
      'package',
      '--vulnerable',
      '--include-transitive',
      '--format',
      'json',
    ],
    { encoding: 'utf8', maxBuffer: 20 * 1024 * 1024 },
  );
  try {
    if (audit.error || audit.status !== 0)
      throw new Error(audit.error?.message || audit.stderr || 'NuGet audit failed.');
    inspectNuget(JSON.parse(audit.stdout));
  } catch (error) {
    console.error(error.message);
    failed = true;
  }
  process.exitCode = failed ? 1 : 0;
}
