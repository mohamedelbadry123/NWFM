export function verifyReplay(report, startedAt) {
  const run = report.run;
  const requiredFiles = ['tests/example.e2e.ts', 'tests/workflow-lifecycle.e2e.ts'];
  if (!run || Date.parse(run.startedAt) < startedAt || !Number.isFinite(Date.parse(run.startedAt)))
    throw new Error('Replay did not produce a fresh report.');
  if (run.status !== 'passed' || run.exitCode !== 0 || run.errors?.length)
    throw new Error('Replay run failed.');
  if (!run.results?.length || run.summary?.selected !== run.summary?.discovered)
    throw new Error('CI must run the complete e2e suite.');
  for (const file of requiredFiles)
    if (!run.results.some((result) => result.file === file && result.selected))
      throw new Error('Required e2e test is missing: ' + file);
  for (const result of run.results) {
    if (!result.selected || result.status !== 'passed' || result.attempts?.length !== 1)
      throw new Error('Every e2e test must pass on its first attempt: ' + result.file);
    for (const step of result.attempts[0].steps ?? []) {
      if ((step.metrics?.modelCalls ?? 0) !== 0)
        throw new Error('Credential-free replay must make zero model calls.');
      if (step.api === 'agent.act' && step.cache?.mode !== 'self-finalized')
        throw new Error('Every business action must replay a verified recording.');
    }
  }
  if (run.usage?.modelTokens !== 0) throw new Error('Replay report must prove zero model tokens.');
}
