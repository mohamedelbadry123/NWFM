import { Injectable, computed, signal } from '@angular/core';
import type { WorkflowValidationIssueDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-validation-issue-dto';
import type { WorkflowValidationResultDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-validation-result-dto';

/** One diagnostics state per editor; canvas, settings and issue drawer read the same result. */
@Injectable()
export class WorkflowDesignDiagnostics {
  readonly result = signal<WorkflowValidationResultDto | null>(null);
  readonly state = signal<'checking' | 'ready' | 'issues' | 'error' | 'idle'>('idle');
  readonly count = computed(() => this.result()?.errors?.length ?? 0);
  readonly byNode = computed(() => {
    const map = new Map<string, WorkflowValidationIssueDto[]>();
    for (const issue of this.result()?.errors ?? []) {
      if (!issue.nodeKey) continue;
      const list = map.get(issue.nodeKey) ?? [];
      list.push(issue);
      map.set(issue.nodeKey, list);
    }
    return map;
  });
  readonly byConnection = computed(() => {
    const map = new Map<string, WorkflowValidationIssueDto>();
    for (const issue of this.result()?.errors ?? []) {
      const key = (issue as { connectionKey?: string }).connectionKey;
      if (key) map.set(key, issue);
    }
    return map;
  });
}
