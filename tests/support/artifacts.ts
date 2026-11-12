import { mkdirSync, mkdtempSync } from 'node:fs';
import { resolve, join } from 'node:path';

/** Host-side diagnostics stay disposable and do not depend on a particular launcher. */
export function testArtifactDirectory(prefix: string): string {
  if (!/^[a-z-]+$/.test(prefix)) throw new Error('Invalid test artifact prefix.');
  const root = resolve('.work/e2e-artifacts');
  mkdirSync(root, { recursive: true });
  return mkdtempSync(join(root, prefix + '-'));
}
