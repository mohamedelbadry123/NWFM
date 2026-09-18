/** Client-side SLA helpers using work-item dueAt — no API inventing. */

export function isOverdue(dueAt: string | null | undefined, status: string | undefined): boolean {
  if (!dueAt || status === 'Completed' || status === 'Cancelled') return false;
  const due = Date.parse(dueAt);
  return !Number.isNaN(due) && due < Date.now();
}

/** Relative due text key payload: overdue | due_soon | due_in | no_due */
export function dueRelativeLabel(
  dueAt: string | null | undefined,
  status: string | undefined,
): { key: string; params?: Record<string, string | number> } {
  if (!dueAt) return { key: 'workflow.runtime.sla.no_due' };
  if (status === 'Completed' || status === 'Cancelled') {
    return { key: 'workflow.runtime.sla.closed' };
  }

  const dueMs = Date.parse(dueAt);
  if (Number.isNaN(dueMs)) return { key: 'workflow.runtime.sla.no_due' };

  const diffMs = dueMs - Date.now();
  const absMin = Math.round(Math.abs(diffMs) / 60_000);

  if (diffMs < 0) {
    if (absMin < 60) return { key: 'workflow.runtime.sla.overdue_minutes', params: { count: absMin } };
    const hours = Math.round(absMin / 60);
    if (hours < 48) return { key: 'workflow.runtime.sla.overdue_hours', params: { count: hours } };
    return { key: 'workflow.runtime.sla.overdue_days', params: { count: Math.round(hours / 24) } };
  }

  if (absMin < 60) return { key: 'workflow.runtime.sla.due_minutes', params: { count: absMin } };
  const hours = Math.round(absMin / 60);
  if (hours < 48) return { key: 'workflow.runtime.sla.due_hours', params: { count: hours } };
  return { key: 'workflow.runtime.sla.due_days', params: { count: Math.round(hours / 24) } };
}

/** MD §14.6 remaining SLA clock: `04h 32m`, negative prefix when breached. */
export function formatRemainingSla(minutes: number | null | undefined): string {
  if (minutes == null || Number.isNaN(Number(minutes))) return '—';
  const rounded = Math.round(Number(minutes));
  const sign = rounded < 0 ? '-' : '';
  const abs = Math.abs(rounded);
  const h = Math.floor(abs / 60);
  const m = abs % 60;
  return `${sign}${String(h).padStart(2, '0')}h ${String(m).padStart(2, '0')}m`;
}

export function formatSlaDuration(minutes: number | null | undefined): string {
  if (minutes == null || Number.isNaN(Number(minutes))) return '—';
  return formatRemainingSla(Math.abs(Math.round(Number(minutes))));
}

export type RemainingSlaTone = 'ok' | 'warning' | 'breached' | 'closed' | 'none';

/** Green ample / amber ≤60m / red breached — matches Requests/Tasks mockups. */
export function remainingSlaTone(
  minutes: number | null | undefined,
  status?: string | null,
): RemainingSlaTone {
  if (status === 'Completed' || status === 'Cancelled') return 'closed';
  if (minutes == null || Number.isNaN(Number(minutes))) return 'none';
  if (minutes < 0) return 'breached';
  if (minutes <= 60) return 'warning';
  return 'ok';
}

export function remainingSlaClass(tone: RemainingSlaTone): string {
  switch (tone) {
    case 'ok':       return 'text-green-700 dark:text-green-300';
    case 'warning':  return 'text-amber-700 dark:text-amber-300';
    case 'breached': return 'text-red-600 dark:text-red-400';
    case 'closed':   return 'text-green-700 dark:text-green-400';
    default:         return 'text-ink-400 dark:text-dark-400';
  }
}
