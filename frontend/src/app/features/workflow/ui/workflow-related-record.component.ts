import { Component, input } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
@Component({selector:'app-workflow-related-record',standalone:true,imports:[TranslatePipe],template:`
  <dl class="grid gap-4 sm:grid-cols-2"><div><dt class="text-xs text-ink-400">{{'workflow.runtime.case.field_entity_type' | translate}}</dt><dd class="mt-1 text-sm">{{entityType() || 'WorkflowRequest'}}</dd></div><div><dt class="text-xs text-ink-400">{{'workflow.runtime.case.field_entity_id' | translate}}</dt><dd class="mt-1 break-all text-sm">{{entityId() || '—'}}</dd></div></dl>`})
export class WorkflowRelatedRecordComponent {readonly entityType=input<string|null|undefined>(null);readonly entityId=input<string|null|undefined>(null);}