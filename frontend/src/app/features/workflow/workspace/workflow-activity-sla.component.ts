import { Component, Input, OnChanges, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LocaleService } from '@core/i18n/locale.service';
import { AuthStore } from '@core/auth/auth.store';
import { WorkflowSlaRulesService, SlaRule } from './workflow-sla-rules.service';
@Component({selector:'app-workflow-activity-sla',standalone:true,imports:[RouterLink],template:`
<section class="p-4 space-y-4" aria-live="polite">
  <p>{{ t('SLA is selected automatically from the Department and Field Activity Type.','يتم تحديد مدة الخدمة تلقائياً حسب القسم ونوع النشاط الميداني.') }}</p>
  @if(loading()){<p>{{ t('Finding SLA rule…','جارٍ البحث عن قاعدة الخدمة…') }}</p>}
  @if(rule();as r){<div class="rounded-lg border p-4 space-y-2"><strong>{{ r.name }}</strong><p>{{ r.duration }} {{ r.durationUnit }}</p><p>{{ r.calendarName }} · {{ r.timeZone }}</p><p>{{ t('Reminders before deadline (minutes)','التذكير قبل الاستحقاق بالدقائق') }}: {{ r.reminderMinutes.join(', ') || '—' }}</p><p>{{ t('Overdue alerts after deadline (minutes)','تنبيهات التأخير بعد الاستحقاق بالدقائق') }}: {{ r.overdueMinutes.join(', ') || '0' }}</p></div>}
  @if(message()){<p role="status">{{ message() }}</p>}
  @if(canManage()){<a routerLink="/admin/workflow/sla-policies" class="underline">{{ t('Manage SLA rules','إدارة قواعد الخدمة') }}</a>}
</section>`})
export class WorkflowActivitySlaComponent implements OnChanges {
  @Input() configuration='{}'; @Input() readonly=false;
  private api=inject(WorkflowSlaRulesService); private locale=inject(LocaleService); private auth=inject(AuthStore); private request=0;
  rule=signal<Pick<SlaRule,'name'|'duration'|'durationUnit'|'reminderMinutes'|'overdueMinutes'|'calendarName'|'timeZone'>|null>(null); loading=signal(false); message=signal('');
  t(en:string,ar:string){return this.locale.locale()==='ar'?ar:en;}
  canManage(){return this.auth.roles().includes('Administrator')||this.auth.hasAnyPermission('ManageSlaPolicies');}
  ngOnChanges(){const request=++this.request;this.rule.set(null);this.loading.set(false);this.message.set('');let c:any;try{c=JSON.parse(this.configuration);}catch{return;}
    if(this.readonly&&c.publishedSla){this.rule.set(c.publishedSla);return;}
    if(!c.departmentCode||!c.fieldActivityCode){this.message.set(this.t('Select Department and FA Type in General.','اختر القسم ونوع النشاط في التبويب العام.'));return;}
    this.loading.set(true);this.api.resolve(c.departmentCode,c.fieldActivityCode).subscribe({next:r=>{if(request!==this.request)return;this.rule.set(r);this.loading.set(false);if(!r)this.message.set(this.t('No matching SLA rule. Create one before publishing.','لا توجد قاعدة خدمة مطابقة. أنشئ قاعدة قبل النشر.'));},error:()=>{if(request!==this.request)return;this.loading.set(false);this.message.set(this.t('Could not load the SLA rule.','تعذر تحميل قاعدة الخدمة.'));}});
  }
}
