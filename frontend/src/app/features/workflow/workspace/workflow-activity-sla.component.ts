import { NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, EventEmitter, Input, OnChanges, OnInit, Output, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LocaleService } from '@core/i18n/locale.service';
import { AuthStore } from '@core/auth/auth.store';
import { WorkflowSlaRulesService, SlaContext, slaKey } from './workflow-sla-rules.service';
import { SLA_UNIT_LABELS, formatMinutes, slaErrorMessage, slaUnitName } from './workflow-sla-rule.validation';

/** What the tab shows; published snapshots and live rules share the fields a reader needs. */
interface SlaSummary { name: string; duration: number; durationUnit: string | number; reminderMinutes: number[]; overdueMinutes: number[]; calendarName?: string; timeZone?: string; isActive?: boolean }
export interface SlaManageRequest { departmentCode: string; fieldActivityCode: string }
type ContextField = 'departmentCode' | 'fieldActivityCode';

/**
 * The activity's SLA tab. Draft activities resolve the shared rule by Department + FA Type and open the SLA editor
 * in place; read-only versions show the snapshot captured at publication and nothing that edits.
 */
@Component({selector:'app-workflow-activity-sla',standalone:true,template:`
<section class="space-y-4 p-4" aria-labelledby="wf-activity-sla-title">
  <h3 id="wf-activity-sla-title" tabindex="-1" class="text-sm font-semibold outline-none">{{ t('Service level (SLA)','مستوى الخدمة (SLA)') }}</h3>
  @if(legacy || (readonly && !snapshot() && hasLegacySettings())){
    <p class="text-sm">{{ t('This workflow uses the earlier activity SLA settings, not Department and Field Activity Type rules.','يستخدم مسار العمل هذا إعدادات الخدمة السابقة على مستوى النشاط، وليس قواعد القسم ونوع النشاط.') }}</p>
    <dl class="space-y-1 rounded-lg border p-3 text-sm">
      <div><dt class="inline font-medium">{{ t('SLA policy','سياسة الخدمة') }}:</dt> <dd class="inline">{{ legacyPolicyName || '—' }}</dd></div>
      <div><dt class="inline font-medium">{{ t('Duration (hours)','المدة (ساعات)') }}:</dt> <dd class="inline">{{ config()['slaDurationHours'] || '—' }}</dd></div>
    </dl>
  } @else if(readonly){
    @if(snapshot(); as s){
      <p class="text-sm">{{ t('Captured when this version was published. Later rule changes do not affect it or its running requests.','تم حفظها عند نشر هذه النسخة. لا تؤثر تغييرات القاعدة اللاحقة عليها أو على طلباتها الجارية.') }}</p>
      <ng-container *ngTemplateOutlet="summary; context: {$implicit: s}" />
    } @else {
      <p class="text-sm">{{ t('This version has no SLA snapshot. It is read-only, so SLA rules cannot be changed from here.','لا توجد لقطة خدمة لهذه النسخة. وهي للقراءة فقط، لذا لا يمكن تغيير قواعد الخدمة من هنا.') }}</p>
    }
  } @else if(missing().length){
    <div role="status" class="space-y-2 rounded-lg px-3 py-3 text-sm" style="background:var(--acc-warn-bg);color:var(--acc-warn-fg)">
      <p>{{ t('The SLA is chosen from the activity’s Department and Field Activity Type. Still missing:','تُحدد مدة الخدمة من قسم النشاط ونوع النشاط الميداني. ما زال ناقصاً:') }}</p>
      <ul class="list-disc ps-5">@for(m of missing();track m){<li>{{ m === 'departmentCode' ? t('Department','القسم') : t('Field Activity Type','نوع النشاط الميداني') }}</li>}</ul>
      <button type="button" class="underline" (click)="editContext.emit(missing()[0])">{{ t('Go to General settings','الانتقال إلى الإعدادات العامة') }}</button>
    </div>
  } @else if(loading()){
    <p role="status" class="text-sm">{{ t('Finding the SLA rule…','جارٍ البحث عن قاعدة الخدمة…') }}</p>
  } @else if(error()){
    <div role="alert" class="space-y-2 rounded-lg px-3 py-3 text-sm" style="background:var(--acc-danger-bg);color:var(--acc-danger-fg)"><p>{{ error() }}</p>
      <button type="button" class="underline" (click)="load()">{{ t('Try again','حاول مرة أخرى') }}</button></div>
  } @else if(context(); as c){
    @if(c.rule; as r){
      <p class="text-sm"><span class="app-badge app-badge--rule">{{ t('Shared rule','قاعدة مشتركة') }}</span>
        {{ t('Every activity with this Department and Field Activity Type uses this rule.','كل نشاط بهذا القسم ونوع النشاط يستخدم هذه القاعدة.') }}
        @if(c.usage.draftActivities){ {{ t('Other draft workflows using it:','مسارات عمل مسودة أخرى تستخدمها:') }} {{ c.usage.draftWorkflows }}.}</p>
      <ng-container *ngTemplateOutlet="summary; context: {$implicit: r}" />
      @if(!c.calendarActive){<p class="text-sm" style="color:var(--acc-danger-fg)">{{ t('Its calendar is inactive, so this activity cannot be published until the rule uses an active calendar.','تقويمها غير نشط، لذا لا يمكن نشر هذا النشاط حتى تستخدم القاعدة تقويماً نشطاً.') }}</p>}
      @if(canManage()){<button type="button" id="wf-activity-sla-action" class="wf-btn-secondary" (click)="open()">{{ t('Edit SLA','تعديل الخدمة') }}</button>}
    } @else {
      <p role="status" class="rounded-lg px-3 py-2 text-sm" style="background:var(--acc-warn-bg);color:var(--acc-warn-fg)">{{ t('No active SLA rule matches this Department and Field Activity Type. The workflow cannot be published until one exists.','لا توجد قاعدة خدمة نشطة لهذا القسم ونوع النشاط. لا يمكن نشر مسار العمل حتى توجد قاعدة.') }}
        @if(c.inactiveRules.length){ {{ t('An inactive rule exists and can be reactivated.','توجد قاعدة غير نشطة يمكن إعادة تفعيلها.') }}}</p>
      @if(canManage()){<button type="button" id="wf-activity-sla-action" class="wf-btn-primary" (click)="open()">{{ t('Create SLA','إنشاء الخدمة') }}</button>}
    }
    @if(!canManage()){<p class="wf-muted text-xs">{{ t('Creating or editing SLA rules requires the Manage SLA Policies permission. Ask an administrator to make the change.','إنشاء قواعد الخدمة أو تعديلها يتطلب صلاحية إدارة سياسات الخدمة. اطلب من المسؤول إجراء التغيير.') }}</p>}
  }
</section>
<ng-template #summary let-s>
  <dl class="space-y-1 rounded-lg border p-3 text-sm">
    <div><dt class="sr-only">{{ t('Rule','القاعدة') }}</dt><dd class="font-semibold">{{ s.name }}@if(s.isActive === false){ <span class="app-badge app-badge--neutral">{{ t('Inactive','غير نشط') }}</span>}</dd></div>
    <div><dt class="inline font-medium">{{ t('Duration','المدة') }}:</dt> <dd class="inline">{{ s.duration }} {{ unit(s.durationUnit) }}</dd></div>
    <div><dt class="inline font-medium">{{ t('Calendar','التقويم') }}:</dt> <dd class="inline">{{ s.calendarName || '—' }} · <span dir="ltr">{{ s.timeZone || '—' }}</span></dd></div>
    <div><dt class="inline font-medium">{{ t('Reminders before deadline','التذكير قبل الاستحقاق') }}:</dt> <dd class="inline" dir="ltr">{{ minutes(s.reminderMinutes) }}</dd></div>
    <div><dt class="inline font-medium">{{ t('Overdue alerts after deadline','تنبيهات التأخير بعد الاستحقاق') }}:</dt> <dd class="inline" dir="ltr">{{ minutes(s.overdueMinutes) }}</dd></div>
  </dl>
</ng-template>`, imports:[NgTemplateOutlet]})
export class WorkflowActivitySlaComponent implements OnChanges, OnInit {
  @Input() configuration='{}'; @Input() readonly=false;
  /** No workspace settings: activities keep their own slaPolicyId / slaDurationHours. */
  @Input() legacy=false; @Input() legacyPolicyName='';
  /** The draft being designed, left out of "other draft workflows". */
  @Input() versionId='';
  /** Open the shared SLA editor for this combination. */
  @Output() manage=new EventEmitter<SlaManageRequest>();
  /** Take the user to the General field that is still missing. */
  @Output() editContext=new EventEmitter<ContextField>();
  private api=inject(WorkflowSlaRulesService); private locale=inject(LocaleService); private auth=inject(AuthStore); private destroyRef=inject(DestroyRef); private request=0; private key='';
  readonly config=signal<Record<string, any>>({}); readonly context=signal<SlaContext|null>(null); readonly loading=signal(false); readonly error=signal(''); readonly missing=signal<ContextField[]>([]);
  readonly snapshot=signal<SlaSummary|null>(null);
  t(en:string,ar:string){return this.locale.locale()==='ar'?ar:en;}
  unit(u:string|number){const name=slaUnitName(u);const l=SLA_UNIT_LABELS[name];return l?this.t(l[0],l[1]):name;}
  minutes(values:number[]|undefined){return formatMinutes(values??[],this.t.bind(this));}
  hasLegacySettings(){const c=this.config();return !!c['slaPolicyId']||Number(c['slaDurationHours'])>0;}
  canManage(){return this.auth.roles().includes('Administrator')||this.auth.hasAnyPermission('ManageSlaPolicies');}
  ngOnInit(){
    // A rule saved from any editor (this activity's, another activity's, or the SLA page) refreshes a matching display.
    this.api.changes$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(change=>{
      const keys=[slaKey(change.rule.departmentCode,change.rule.fieldActivityCode)];
      if(change.previous)keys.push(slaKey(change.previous.departmentCode,change.previous.fieldActivityCode));
      if(this.key&&!this.readonly&&!this.legacy&&keys.includes(this.key))this.load();
    });
  }
  ngOnChanges(){
    let c:Record<string, any>={};try{c=JSON.parse(this.configuration||'{}')??{};}catch{c={};}
    this.config.set(c);this.snapshot.set(this.readonly&&c['publishedSla']?this.fromSnapshot(c['publishedSla']):null);
    const missing:ContextField[]=[];if(!c['departmentCode'])missing.push('departmentCode');if(!c['fieldActivityCode'])missing.push('fieldActivityCode');this.missing.set(missing);
    const key=missing.length||this.readonly||this.legacy?'':slaKey(c['departmentCode'],c['fieldActivityCode']);
    // Only a different combination needs a new lookup; unrelated edits to the activity keep the loaded rule on screen.
    if(key===this.key&&(this.context()||this.loading()))return;
    this.key=key;this.load();
  }
  load(){
    const request=++this.request;this.context.set(null);this.error.set('');this.loading.set(false);
    if(!this.key)return;
    const c=this.config();this.loading.set(true);
    this.api.context(c['departmentCode'],c['fieldActivityCode'],this.versionId||undefined).subscribe({
      next:r=>{if(request!==this.request)return;this.context.set(r);this.loading.set(false);},
      error:e=>{if(request!==this.request)return;this.loading.set(false);this.error.set(slaErrorMessage(e,this.t.bind(this)));}});
  }
  open(){const c=this.config();if(this.key)this.manage.emit({departmentCode:c['departmentCode'],fieldActivityCode:c['fieldActivityCode']});}
  private fromSnapshot(s:any):SlaSummary{return {name:s.name,duration:s.duration,durationUnit:s.durationUnit,reminderMinutes:s.reminderMinutes??[],overdueMinutes:s.overdueMinutes??[],timeZone:s.timeZone,calendarName:this.t('Captured calendar','التقويم المحفوظ')};}
}
