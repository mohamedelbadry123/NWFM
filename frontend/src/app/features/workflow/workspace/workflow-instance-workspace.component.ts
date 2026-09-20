import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { interval } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { AuthStore } from '@core/auth/auth.store';
import { WorkflowIntegrationsService } from '../integrations/workflow-integrations.service';
import { WorkflowWorkspaceService, WorkspaceDetail, WorkspaceExecution, WorkspaceTask } from './workflow-workspace.service';

@Component({ selector: 'app-workflow-instance-workspace', standalone: true, imports: [FormsModule, DatePipe, RouterLink, NgTemplateOutlet], template: `
<main class="p-5 sm:p-8 space-y-6 max-w-7xl mx-auto">
  <a routerLink="/admin/workflow/instances" class="text-primary underline">{{ t('Back to instances','العودة إلى النسخ') }}</a>
  @if (error()) {<p class="p-4 rounded-xl border border-red-300 bg-red-50 text-red-800" role="alert">{{ error() }}</p>}
  @if (detail(); as model) {
    <header class="flex flex-wrap justify-between gap-4"><div><h1 class="text-2xl font-semibold">{{ model.tree[0].name }}</h1><p class="mt-2">{{ model.tree[0].status }} · {{ geography(model.geographyJson) }}</p></div><button class="wf-btn-secondary" (click)="load()" [disabled]="loading()">{{ t('Refresh','تحديث') }}</button></header>
    @if (model.isDemo) {
      <div class="rounded-xl bg-amber-50 border border-amber-300 text-amber-950 p-4 space-y-2"><strong>{{ t('Demo instance','نسخة تجريبية') }}</strong>
      @if (model.demoActors.length) {<label class="block">{{ t('Test as user','التجربة كمستخدم') }}<select class="wf-input max-w-md" [(ngModel)]="demoActor" (ngModelChange)="load()"><option value="">{{ t('Signed-in user','المستخدم الحالي') }}</option>@for (actor of model.demoActors; track actor.userId) {<option [value]="actor.userId">{{ actor.name }}</option>}</select></label><p class="text-sm">{{ t('Every action records your account and the selected demo user.','يتم تسجيل حسابك والمستخدم التجريبي المحدد مع كل إجراء.') }}</p>}
      </div>
    }
    <section class="space-y-4" [attr.aria-label]="t('Workflow progress','تقدم سير العمل')"><ng-container *ngTemplateOutlet="execution; context: {$implicit:model.tree[0]}" /></section>
    <details class="rounded-xl border bg-white dark:bg-dark-800 p-5" open><summary class="text-lg font-medium cursor-pointer">{{ t('Events and integrations','الأحداث والتكاملات') }}</summary><div class="space-y-3 mt-4">
      @for (operation of model.operations; track operation.id) {
        <article class="border rounded-lg p-3"><div class="flex flex-wrap items-center justify-between gap-3"><strong>{{ operation.name || operation.kind }}</strong><span>{{ operation.status }} · {{ operation.attempts }} {{ t('attempts','محاولات') }}</span></div><p class="text-sm mt-1">{{ operation.trigger }} · {{ operation.required ? t('Required before advancing','مطلوب قبل الانتقال') : t('Background delivery','إرسال في الخلفية') }}</p>
        @if (operation.error) {<p class="text-red-600 mt-2">{{ operation.error }}</p>}
        @if (operation.status === 'Failed' && canManage()) {<button class="wf-btn-secondary mt-2" [disabled]="busy()" (click)="retry(operation.id)">{{ t('Retry delivery','إعادة محاولة الإرسال') }}</button>}
        @if (operation.responseJson) {<details class="mt-2"><summary>{{ t('Response details','تفاصيل الاستجابة') }}</summary><pre class="text-xs overflow-auto max-h-56 p-3">{{ operation.responseJson }}</pre></details>}
        </article>
      } @empty {<p class="text-sm opacity-70">{{ t('No integration events yet.','لا توجد أحداث تكامل حتى الآن.') }}</p>}
    </div></details>
    <details class="rounded-xl border bg-white dark:bg-dark-800 p-5" open><summary class="text-lg font-medium cursor-pointer">{{ t('History and comments','السجل والتعليقات') }}</summary><ol class="mt-4 space-y-3">
      @for (event of model.history; track event.id) {<li class="border-s-2 ps-4"><div class="text-sm"><strong>{{ event.type }}</strong> · {{ event.actorName || t('System','النظام') }} · {{ event.occurredAt | date:'medium' }}</div>@if (comment(event.payloadJson)) {<p class="whitespace-pre-wrap mt-1">{{ comment(event.payloadJson) }}</p>}<span class="text-xs opacity-60">{{ event.nodeKey }}</span></li>}
    </ol></details>
  } @else if (loading()) {<p>{{ t('Loading instance…','جارٍ تحميل النسخة…') }}</p>}
</main>
<ng-template #execution let-run>
  <div class="space-y-3">
  @for (activity of run.activities; track activity.id) {
    <article class="rounded-xl border p-4 bg-white dark:bg-dark-800" [class.border-primary]="activity.status === 'Active'">
      <div class="flex flex-wrap justify-between gap-3"><h2 class="font-semibold">{{ activity.name }}</h2><span>{{ phase(activity.phase) || activity.status }}</span></div>
      <p class="text-xs opacity-70 mt-2">{{ activity.departmentCode }} @if (activity.fieldActivityCode) {· {{ activity.fieldActivityCode }}} @if (activity.dueAt) {· {{ t('Due','الاستحقاق') }} {{ activity.dueAt | date:'medium' }} @if (overdue(activity.dueAt,activity.status)) {<strong class="text-red-600">{{ t('Overdue','متأخر') }}</strong>}}</p>
      @if (activity.assignedGroup && !activity.tasks.length) {<p class="text-sm mt-2">{{ t('Assigned group','المجموعة المسؤولة') }}: {{ activity.assignedGroup }}</p>}
      @if (activity.phase === 'WaitingForChild') {<p class="text-sm mt-2">{{ t('Complete the child workflow before approving this activity.','أكمل سير العمل الفرعي قبل اعتماد هذا النشاط.') }}</p>}
      @if (activity.phase === 'ChildFailed') {<p class="text-red-600 mt-2">{{ t('The child workflow failed or was cancelled. Parent approval is blocked.','فشل سير العمل الفرعي أو تم إلغاؤه. الاعتماد الرئيسي متوقف.') }}</p>}
      @for (child of children(run.id,activity.id); track child.id) {<details class="mt-4 border-s-2 ps-4" open><summary class="cursor-pointer font-medium">{{ child.name }} · {{ child.status }}</summary><div class="mt-3"><ng-container *ngTemplateOutlet="execution; context: {$implicit:child}" /></div></details>}
      @for (entry of activity.tasks; track entry.task.id) {
        <div class="mt-4 border-t pt-4 space-y-3"><p class="text-sm">{{ t('Assigned group','المجموعة المسؤولة') }}: <strong>{{ entry.task.assignmentGroupName }}</strong></p>
        @if (entry.canAct) {
          <p class="text-sm">{{ entry.task.instructionsEn }}</p>
          @for (field of entry.task.formFields || []; track field.key) {
            <label class="block text-sm">{{ field.labelEn || field.key }} @if (field.required) {*}
              @if (field.type === 'select') {<select class="wf-input" [(ngModel)]="formValues[entry.task.id][field.key]" [required]="field.required"><option value=""></option>@for (option of field.options || []; track option) {<option [value]="option">{{ option }}</option>}</select>}
              @else if (field.type === 'boolean' || field.type === 'checkbox') {<input type="checkbox" [(ngModel)]="formValues[entry.task.id][field.key]">}
              @else if (field.type === 'textarea') {<textarea class="wf-input" rows="4" [(ngModel)]="formValues[entry.task.id][field.key]" [required]="field.required"></textarea>}
              @else {<input class="wf-input" [type]="field.type === 'number' ? 'number' : field.type === 'date' ? 'date' : field.type === 'email' ? 'email' : 'text'" [(ngModel)]="formValues[entry.task.id][field.key]" [required]="field.required">}
            </label>
          }
          <label class="block text-sm">{{ t('Comment','تعليق') }}<textarea class="wf-input" rows="3" maxlength="4000" [(ngModel)]="comments[entry.task.id]"></textarea></label>
          <div class="flex flex-wrap gap-2">@for (outcome of entry.task.availableOutcomes || []; track outcome.id) {<button class="wf-btn-primary" [disabled]="busy()" (click)="act(entry,outcome.outcomeKey)">{{ outcome.name }}</button>}<button class="wf-btn-secondary" [disabled]="busy() || !comments[entry.task.id]?.trim()" (click)="act(entry,'comment')">{{ t('Add comment','إضافة تعليق') }}</button></div>
        } @else {<p class="text-sm opacity-70">{{ entry.disabledReason }}</p>}
        </div>
      }
    </article>
  }
  </div>
</ng-template>` })
export class WorkflowInstanceWorkspaceComponent implements OnInit {
  private readonly api = inject(WorkflowWorkspaceService); private readonly integrations = inject(WorkflowIntegrationsService);
  private readonly route = inject(ActivatedRoute); private readonly locale = inject(LocaleService); private readonly destroy = inject(DestroyRef); private readonly auth = inject(AuthStore);
  readonly detail = signal<WorkspaceDetail | null>(null); readonly error = signal(''); readonly loading = signal(false); readonly busy = signal(false);
  demoActor = ''; comments: Record<string,string> = {}; formValues: Record<string,Record<string,unknown>> = {}; private id = '';
  private pending: {fingerprint:string;id:string} | null = null;
  private loadSequence = 0;
  t(en: string, ar: string) { return this.locale.locale() === 'ar' ? ar : en; }
  canManage() { return this.auth.roles().includes('Administrator') || this.auth.hasAnyPermission('ManageDefinitions'); }
  ngOnInit() { this.route.paramMap.pipe(takeUntilDestroyed(this.destroy)).subscribe(params=>{this.id=params.get('id') || '';this.detail.set(null);this.demoActor='';this.load();}); interval(4000).pipe(takeUntilDestroyed(this.destroy)).subscribe(()=>{if(!this.busy()&&!this.loading())this.load(true);}); }
  load(quiet=false) {
    const sequence=++this.loadSequence;
    this.loading.set(true);
    this.api.detail(this.id,this.demoActor || undefined).pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next:d=>{
        if(sequence!==this.loadSequence)return;
        for(const run of d.tree)for(const activity of run.activities)for(const entry of activity.tasks){
          const id=entry.task.id!;this.formValues[id] ??= {...entry.task.formValues};this.comments[id] ??= '';
        }
        this.detail.set(d);this.loading.set(false);if(!quiet)this.error.set('');
      },
      error:e=>{if(sequence!==this.loadSequence)return;this.loading.set(false);this.error.set(e.error?.message || 'Could not load instance.');}
    });
  }
  children(instance: string, activity: string): WorkspaceExecution[] { return this.detail()?.tree.filter(x=>x.parentInstanceId===instance&&x.parentActivityInstanceId===activity)||[]; }
  geography(raw?: string) { try { const g=JSON.parse(raw||'{}');return [g.clusterCode,g.regionCode,g.cityCode].filter(Boolean).join(' / '); } catch{return '';} }
  comment(raw?: string) { try{return JSON.parse(raw||'{}').comment || '';}catch{return '';} }
  overdue(due: string, status: string) { return status==='Active'&&Date.parse(due)<Date.now(); }
  phase(value?: string) { const labels:Record<string,string>={WaitingForChild:this.t('Waiting for child workflow','بانتظار سير العمل الفرعي'),AwaitingApproval:this.t('Awaiting approval','بانتظار الاعتماد'),WaitingForEnterEvents:this.t('Processing entry events','معالجة أحداث الدخول'),WaitingForOutcomeEvents:this.t('Processing required events','معالجة الأحداث المطلوبة')};return value ? labels[value] || value : ''; }
  act(entry: WorkspaceTask, action: string) { if(this.busy()||!entry.task.id)return;const body={action,comment:this.comments[entry.task.id],formValues:this.formValues[entry.task.id],demoActorId:this.demoActor||undefined};const fingerprint=JSON.stringify({id:entry.task.id,...body});if(this.pending?.fingerprint!==fingerprint)this.pending={fingerprint,id:crypto.randomUUID()};this.busy.set(true);this.error.set('');this.api.act(entry.task.id,{...body,requestId:this.pending!.id}).subscribe({next:()=>{this.pending=null;this.comments[entry.task.id!]='';this.busy.set(false);this.load();},error:e=>{this.busy.set(false);this.error.set(e.error?.message || 'Action could not be completed.');}}); }
  retry(id: string) { this.busy.set(true);this.integrations.replay(id).subscribe({next:()=>{this.busy.set(false);this.load();},error:e=>{this.busy.set(false);this.error.set(e.error?.message || 'Retry failed.');}}); }
}
