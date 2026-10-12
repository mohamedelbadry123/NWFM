import type { StepExecutor } from 'e2e';

// Replay belongs to e2e's runner, before runStep. Match the recorded built-in
// executor identity for the pinned e2e 0.19.0 release. Revalidate on upgrades.
// This executor has no model, network client, or ability to repair a recording.
export const replayOnly: StepExecutor = {
  name: 'e2e-default-agent',
  version: '2',
  cache: 'inherit',
  async runStep() {
    return {
      status: 'failed',
      summary:
        'CI requires a complete verified recording. Run locally with ChatGPT and commit the updated replay.',
      errorCode: 'ACTION_FAILED',
    };
  },
};
