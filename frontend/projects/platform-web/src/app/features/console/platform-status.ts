type TagSeverity = 'success' | 'info' | 'warn' | 'danger' | 'secondary' | 'contrast';

const SEVERITIES: Readonly<Record<string, TagSeverity>> = {
  // Tenant status
  Active: 'success',
  Provisioning: 'info',
  Suspended: 'danger',
  Maintenance: 'warn',
  Deleted: 'secondary',
  // Subscription status
  Trial: 'info',
  Expired: 'danger',
  Cancelled: 'secondary',
  // Signup status
  Completed: 'success',
  Queued: 'info',
  PendingEmailVerification: 'secondary',
  AwaitingDatabase: 'warn',
  AwaitingApproval: 'warn',
  Failed: 'danger',
  Rejected: 'secondary',
  // Workspace administrator status
  PendingActivation: 'warn',
};

/** Tag colour for the status words the platform API returns (tenant, subscription and signup). */
export function platformStatusSeverity(status: string | null | undefined): TagSeverity {
  return (status && SEVERITIES[status]) || 'secondary';
}
