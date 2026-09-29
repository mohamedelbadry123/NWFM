import { Component, OnInit, inject, signal } from '@angular/core';
import { LocaleService } from '@core/i18n/locale.service';
import { AuthStore } from '@core/auth/auth.store';
import { WorkflowSlaRulesService, SlaRule } from './workflow-sla-rules.service';
import { WorkflowWorkspaceService, ReferenceItem } from './workflow-workspace.service';
import { WorkflowSlaEditorComponent, SlaEditorRequest } from './workflow-sla-editor.component';
import { WorkflowSlaCalendarManagerComponent } from './workflow-sla-calendar-manager.component';
import { SLA_UNIT_LABELS, slaErrorMessage } from './workflow-sla-rule.validation';

/** The standalone SLA page. Creating and editing go through the same editor the workflow designer opens. */
@Component({standalone:true,imports:[WorkflowSlaEditorComponent,WorkflowSlaCalendarManagerComponent],template:`
<main class="p-6 space-y-6"><header class="flex flex-wrap justify-between gap-4"><div><h1 class="text-2xl font-semibold">{{ t('SLA rules','قواعد مدة الخدمة') }}</h1><p>{{ t('Automatically applied by Department and Field Activity Type. Published workflows retain their existing rules.','تُطبق تلقائياً حسب القسم ونوع النشاط. تحتفظ مسارات العمل المنشورة بقواعدها الحالية.') }}</p></div><button type="button" id="sla-new-rule" class="wf-btn-primary self-start" (click)="edit()">{{ t('New SLA rule','قاعدة جديدة') }}</button></header>
@if(error()){<p role="alert" class="text-red-600">{{ error() }}</p>}
@if(loading()){<p role="status" class="wf-muted">{{ t('Loading SLA rules…','جارٍ تحميل قواعد الخدمة…') }}</p>}
@else if(!rules().length){<p class="wf-muted">{{ t('No SLA rules yet. Create one for each Department and Field Activity Type your workflows use.','لا توجد قواعد خدمة بعد. أنشئ قاعدة لكل قسم ونوع نشاط تستخدمه مسارات العمل.') }}</p>}
@else {<div class="overflow-auto border rounded-xl"><table class="w-full text-start"><thead><tr><th class="p-3 text-start">{{ t('Rule','القاعدة') }}</th><th class="text-start">{{ t('Department / FA Type','القسم / نوع النشاط') }}</th><th class="text-start">{{ t('Duration','المدة') }}</th><th class="text-start">{{ t('Status','الحالة') }}</th><th><span class="sr-only">{{ t('Actions','الإجراءات') }}</span></th></tr></thead><tbody>@for(r of rules();track r.id){<tr class="border-t"><td class="p-3">{{ r.name }}</td><td>{{ departmentName(r.departmentCode) }} / {{ r.fieldActivityCode }}</td><td>{{ r.duration }} {{ unit(r.durationUnit) }}</td><td>{{ r.isActive?t('Active','نشط'):t('Inactive','غير نشط') }}</td><td class="p-2"><button type="button" class="wf-btn-secondary" [id]="'sla-edit-' + r.id" (click)="edit(r)" [attr.aria-label]="t('Edit','تعديل') + ' ' + r.name">{{ t('Edit','تعديل') }}</button></td></tr>}</tbody></table></div>}
@if(canCalendars()){<details class="border rounded-xl p-4" (toggle)="calendarsOpen.set($any($event.target).open)"><summary class="cursor-pointer font-semibold">{{ t('Calendars, working periods and holidays','التقاويم وساعات العمل والعطلات') }}</summary>
@if(calendarsOpen()){<div class="mt-4"><app-workflow-sla-calendar-manager /></div>}</details>}
@if(editor(); as request){<app-workflow-sla-editor [request]="request" (closed)="closeEditor($event)" />}
</main>`})
export class WorkflowSlaRulesComponent implements OnInit {
  private api=inject(WorkflowSlaRulesService);private references=inject(WorkflowWorkspaceService);private locale=inject(LocaleService);private auth=inject(AuthStore);
  rules=signal<SlaRule[]>([]);departments=signal<ReferenceItem[]>([]);error=signal('');loading=signal(true);calendarsOpen=signal(false);editor=signal<SlaEditorRequest|null>(null);
  private opener:HTMLElement|null=null;
  t(en:string,ar:string){return this.locale.locale()==='ar'?ar:en;}label(d:ReferenceItem){return this.locale.locale()==='ar'?d.nameAr:d.nameEn;}departmentName(code:string){const d=this.departments().find(x=>x.code===code);return d?this.label(d):code;}
  unit(u:string){const l=SLA_UNIT_LABELS[u];return l?this.t(l[0],l[1]):u;}
  canCalendars(){return this.auth.roles().includes('Administrator')||this.auth.hasAnyPermission('ManageCalendars');}
  ngOnInit(){this.load();this.references.references('departments').subscribe({next:r=>this.departments.set(r),error:e=>this.error.set(slaErrorMessage(e,this.t.bind(this)))});}
  load(){this.api.list().subscribe({next:r=>{this.rules.set(r);this.loading.set(false);},error:e=>{this.loading.set(false);this.error.set(slaErrorMessage(e,this.t.bind(this)));}});}
  edit(rule?:SlaRule){this.opener=document.activeElement as HTMLElement|null;this.editor.set({locked:false,rule:rule??null});}
  closeEditor(saved:SlaRule|null){this.editor.set(null);if(saved)this.load();const opener=this.opener;setTimeout(()=>(opener?.isConnected?opener:document.getElementById('sla-new-rule'))?.focus());}
}
