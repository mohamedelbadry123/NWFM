import type { ActivityInstanceDto } from '@shared/models/models/Workflow/Application/DTOs/activity-instance-dto';
import type { WorkflowHistoryEvent } from '@core/models/workflow-ops.models';

const HUMAN_EVENT_TYPES = new Set([
  'InstanceStarted',
  'InstanceCompleted',
  'InstanceCancelled',
  'InstanceFailed',
  'WorkItemClaimed',
  'WorkItemCompleted',
  'WorkItemReleased',
  'WorkItemReassigned',
  'WorkItemDelegated',
  'ActivityFailed',
]);

const STEP_ACTIVITY_TYPES = new Set(['UserTask', 'Start', 'End']);

export function isHumanHistoryEvent(event: WorkflowHistoryEvent): boolean {
  return !!event.eventType && HUMAN_EVENT_TYPES.has(event.eventType);
}

export function stepActivities(activities: ActivityInstanceDto[] | null | undefined): ActivityInstanceDto[] {
  return [...(activities ?? [])]
    .filter(a => !a.activityType || STEP_ACTIVITY_TYPES.has(String(a.activityType)))
    .sort((a, b) => Date.parse(a.startedAt ?? '') - Date.parse(b.startedAt ?? ''));
}

const GENERIC_STEP_NAME = /^(user\s*task|usertask|new\s*task)$/i;

export function activityDisplayName(activity: { name?: string | null; activityNodeKey?: string | null } | null | undefined): string {
  const name = activity?.name?.trim();
  if (name && !GENERIC_STEP_NAME.test(name)) {
    return name;
  }
  return activity?.activityNodeKey ?? '—';
}

export function historyStepName(event: WorkflowHistoryEvent, rtl: boolean): string {
  const name = rtl
    ? (event.activityNameAr || event.activityNameEn)
    : (event.activityNameEn || event.activityNameAr);
  if (name && !GENERIC_STEP_NAME.test(name.trim())) return name.trim();
  return event.activityNodeKey ?? '';
}

export function historyActorName(event: WorkflowHistoryEvent, rtl: boolean): string | null {
  const name = rtl
    ? (event.actorNameAr || event.actorName)
    : (event.actorName || event.actorNameAr);
  return name?.trim() || null;
}
