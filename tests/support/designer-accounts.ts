import { credentials } from 'e2e';
import { designerCredentialName, type DesignerSuite } from '../../scripts/e2e/fixture-users.js';

/** Independent principals exercise ordinary per-user budgets without raising server limits.
 * e2e runs passing cases in declaration order within one file realm. No retry or request replay.
 */
export function designerAccounts(suite: DesignerSuite) {
  let index = 0;
  return {
    next: () => credentials.user(designerCredentialName(suite, ++index)),
    current: () => credentials.user(designerCredentialName(suite, index)),
  };
}
