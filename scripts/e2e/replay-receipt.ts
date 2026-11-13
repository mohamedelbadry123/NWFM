import { createHash } from 'node:crypto';
import { writeFileSync } from 'node:fs';
import type { Reporter } from 'e2e';

// The wrapper gives each invocation a new, empty receipt directory. This hook
// runs only after this runner has persisted its report; timestamps alone cannot
// prove freshness when the host clock changes during a long integration run.
export const replayReceipt: Reporter = {
  name: 'nwfm-replay-receipt',
  async onRunFinished({ report, reportPath }) {
    const receiptPath = process.env.NWFM_E2E_RECEIPT_PATH;
    const nonce = process.env.NWFM_E2E_RECEIPT_NONCE;
    if (!receiptPath || !nonce || !reportPath) return;
    writeFileSync(
      receiptPath,
      JSON.stringify({
        nonce,
        runId: report.run.id,
        digest: createHash('sha256').update(JSON.stringify(report)).digest('hex'),
      }),
      { flag: 'wx', mode: 0o600 },
    );
  },
};
