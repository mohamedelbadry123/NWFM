import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { ApiConfiguration } from '@shared/models/api-configuration';
import { LocaleService } from '@core/i18n/locale.service';
import { AuthStore } from '@core/auth/auth.store';
import { WorkflowSlaRulesService, SlaRule } from './workflow-sla-rules.service';
import { WorkflowWorkspaceService, ReferenceItem } from './workflow-workspace.service';
import { WorkflowCalendarsComponent } from '../../admin/workflow-calendars/workflow-calendars.component';
@Component({standalone:true,imports:[FormsModule,WorkflowCalendarsComponent],template:`
<main class="p-6 space-y-6"><header class="flex justify-between gap-4"><div><h1 class="text-2xl font-semibold">{{ t('SLA rules','قواعد مدة الخدمة') }}</h1><p>{{ t('Automatically applied by Department and Field Activity Type. Published workflows retain their existing rules.','تُطبق تلقائياً حسب القسم ونوع النشاط. تحتفظ مسارات العمل المنشورة بقواعدها الحالية.') }}</p></div><button class="wf-btn-primary" (click)="edit()">{{ t('New SLA rule','قاعدة جديدة') }}</button></header>
@if(error()){<p role="alert" class="text-red-600">{{ error() }}</p>}
<div class="overflow-auto border rounded-xl"><table class="w-full text-start"><thead><tr><th class="p-3 text-start">{{ t('Rule','القاعدة') }}</th><th>{{ t('Department / FA Type','القسم / نوع النشاط') }}</th><th>{{ t('Duration','المدة') }}</th><th>{{ t('Status','الحالة') }}</th><th></th></tr></thead><tbody>@for(r of rules();track r.id){<tr class="border-t"><td class="p-3">{{ r.name }}</td><td>{{ departmentName(r.departmentCode) }} / {{ r.fieldActivityCode }}</td><td>{{ r.duration }} {{ r.durationUnit }}</td><td>{{ r.isActive?t('Active','نشط'):t('Inactive','غير نشط') }}</td><td><button class="wf-btn-secondary" (click)="edit(r)">{{ t('Edit','تعديل') }}</button></td></tr>}</tbody></table></div>
@if(form;as f){<form (ngSubmit)="save()" class="border rounded-xl p-5 grid md:grid-cols-2 gap-4">
<label>{{ t('Name','الاسم') }}<input class="wf-input" name="name" [(ngModel)]="f.name" required maxlength="200"></label>
<label>{{ t('Department','القسم') }}<select class="wf-input" name="department" [(ngModel)]="f.departmentCode" (ngModelChange)="fields($event);f.fieldActivityCode=''" required><option value="">—</option>@for(d of departments();track d.code){<option [value]="d.code">{{ label(d) }}</option>}</select></label>
<label>{{ t('Field Activity Type','نوع النشاط الميداني') }}<select class="wf-input" name="field" [(ngModel)]="f.fieldActivityCode" required><option value="">—</option>@for(d of fieldTypes();track d.code){<option [value]="d.code">{{ label(d) }}</option>}</select></label>
<label>{{ t('Duration','المدة') }}<input class="wf-input" name="duration" type="number" min="1" max="87600" [(ngModel)]="f.duration" required></label>
<label>{{ t('Unit','الوحدة') }}<select class="wf-input" name="unit" [(ngModel)]="f.durationUnit">@for(unit of units;track unit){<option>{{ unit }}</option>}</select></label>
<label>{{ t('Calendar','التقويم') }}<select class="wf-input" name="calendar" [(ngModel)]="f.calendarId" required><option value="">—</option>@for(c of calendars();track c.id){<option [value]="c.id">{{ c.name }} · {{ c.timeZone }}</option>}</select></label>
<label>{{ t('Reminders: minutes before deadline, comma separated','التذكيرات: دقائق قبل الاستحقاق، مفصولة بفواصل') }}<input class="wf-input" name="reminders" [(ngModel)]="reminders" placeholder="60, 15"></label>
<label>{{ t('Overdue alerts: minutes after deadline, comma separated','تنبيهات التأخير: دقائق بعد الاستحقاق، مفصولة بفواصل') }}<input class="wf-input" name="overdue" [(ngModel)]="overdue" placeholder="0, 60"></label>
<label><input type="checkbox" name="active" [(ngModel)]="f.isActive"> {{ t('Active','نشط') }}</label>
<div class="flex gap-3"><button class="wf-btn-primary" [disabled]="busy()">{{ t('Save rule','حفظ القاعدة') }}</button><button type="button" class="wf-btn-secondary" (click)="form=null">{{ t('Cancel','إلغاء') }}</button></div></form>}
@if(canCalendars()){<details class="border rounded-xl p-4" (toggle)="loadCalendars()"><summary class="cursor-pointer font-semibold">{{ t('Calendars, working periods and holidays','التقاويم وساعات العمل والعطلات') }}</summary><app-workflow-calendars />
<div class="space-y-3 mt-4"><label>{{ t('Edit calendar schedule','تعديل جدول التقويم') }}<select class="wf-input" [(ngModel)]="calendarId" (ngModelChange)="loadCalendar()"><option value="">—</option>@for(c of calendars();track c.id){<option [value]="c.id">{{ c.name }}</option>}</select></label>
@if(calendar();as c){<p>{{ c.timeZone }}</p><ul>@for(p of c.periods;track p.id){<li>{{ p.dayOfWeek }} {{ p.startTime }} – {{ p.endTime }} <button (click)="removeCalendarItem('periods',p.id)">×</button></li>}@for(h of c.holidays;track h.id){<li>{{ h.name }} · {{ h.holidayDate }} <button (click)="removeCalendarItem('holidays',h.id)">×</button></li>}</ul>
<div class="flex flex-wrap gap-3"><label>{{ t('Day','اليوم') }}<select class="wf-input" [(ngModel)]="period.dayOfWeek">@for(day of days;track day){<option>{{ day }}</option>}</select></label><label>{{ t('Start','البداية') }}<input class="wf-input" type="time" [(ngModel)]="period.startTime"></label><label>{{ t('End','النهاية') }}<input class="wf-input" type="time" [(ngModel)]="period.endTime"></label><button class="wf-btn-secondary" (click)="addPeriod()">{{ t('Add working period','إضافة فترة عمل') }}</button></div>
<div class="flex flex-wrap gap-3"><label>{{ t('Holiday','العطلة') }}<input class="wf-input" [(ngModel)]="holiday.name"></label><label>{{ t('Date','التاريخ') }}<input class="wf-input" type="date" [(ngModel)]="holiday.holidayDate"></label><label><input type="checkbox" [(ngModel)]="holiday.isRecurring">{{ t('Every year','سنوية') }}</label><button class="wf-btn-secondary" (click)="addHoliday()">{{ t('Add holiday','إضافة عطلة') }}</button></div>}
</div></details>}
</main>`})
export class WorkflowSlaRulesComponent implements OnInit {
  private api=inject(WorkflowSlaRulesService);private references=inject(WorkflowWorkspaceService);private http=inject(HttpClient);private base=inject(ApiConfiguration).rootUrl+'/api/workflow/calendars';private locale=inject(LocaleService);private auth=inject(AuthStore);
  rules=signal<SlaRule[]>([]);departments=signal<ReferenceItem[]>([]);fieldTypes=signal<ReferenceItem[]>([]);calendars=signal<any[]>([]);calendar=signal<any>(null);error=signal('');busy=signal(false);form:SlaRule|null=null;reminders='60';overdue='0';calendarId='';
  units=['Minutes','Hours','Days','BusinessHours','BusinessDays'];days=['Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday'];period={dayOfWeek:'Sunday',startTime:'08:00',endTime:'16:00'};holiday={name:'',holidayDate:'',isRecurring:false};
  t(en:string,ar:string){return this.locale.locale()==='ar'?ar:en;}label(d:ReferenceItem){return this.locale.locale()==='ar'?d.nameAr:d.nameEn;}departmentName(code:string){const d=this.departments().find(x=>x.code===code);return d?this.label(d):code;}
  canCalendars(){return this.auth.roles().includes('Administrator')||this.auth.hasAnyPermission('ManageCalendars');}
  ngOnInit(){this.load();this.loadCalendars();this.references.references('departments').subscribe({next:r=>this.departments.set(r),error:e=>this.fail(e)});}
  fail(e:any){this.error.set(e.error?.message??e.error?.detail??'Could not save or load data.');this.busy.set(false);}
  load(){this.api.list().subscribe({next:r=>this.rules.set(r),error:e=>this.fail(e)});}
  fields(code:string){this.fieldTypes.set([]);this.references.references('field-activity-types',code).subscribe({next:r=>{if(this.form?.departmentCode===code)this.fieldTypes.set(r);},error:e=>this.fail(e)});}
  edit(rule?:SlaRule){this.form=rule?structuredClone(rule):{id:'',name:'',departmentCode:'',fieldActivityCode:'',duration:8,durationUnit:'Hours',calendarId:'',reminderMinutes:[60],overdueMinutes:[0],isActive:true};this.reminders=this.form.reminderMinutes.join(', ');this.overdue=this.form.overdueMinutes.join(', ');if(rule)this.fields(rule.departmentCode);}
  save(){if(!this.form)return;const parse=(s:string)=>s.split(',').map(x=>x.trim()).filter(Boolean).map(Number);const r={...this.form,reminderMinutes:parse(this.reminders),overdueMinutes:parse(this.overdue)};if([...r.reminderMinutes,...r.overdueMinutes].some(n=>!Number.isInteger(n))){this.error.set(this.t('Thresholds must be whole minutes.','يجب أن تكون المدد أعداداً صحيحة بالدقائق.'));return;}this.busy.set(true);this.error.set('');this.api.save(r).subscribe({next:()=>{this.form=null;this.busy.set(false);this.load();},error:e=>this.fail(e)});}
  loadCalendars(){this.http.get<any[]>(injectApiBase(this.base)).subscribe({next:r=>this.calendars.set(r),error:e=>this.fail(e)});}
  loadCalendar(){if(!this.calendarId){this.calendar.set(null);return;}this.http.get<any>(this.base+'/'+this.calendarId).subscribe({next:r=>this.calendar.set(r),error:e=>this.fail(e)});}
  addPeriod(){this.http.post(this.base+'/'+this.calendarId+'/periods',{...this.period,startTime:this.period.startTime+':00',endTime:this.period.endTime+':00',isWorkingTime:true}).subscribe({next:()=>this.loadCalendar(),error:e=>this.fail(e)});}
  addHoliday(){this.http.post(this.base+'/'+this.calendarId+'/holidays',this.holiday).subscribe({next:()=>this.loadCalendar(),error:e=>this.fail(e)});}
  removeCalendarItem(kind:string,id:string){this.http.delete(this.base+'/'+this.calendarId+'/'+kind+'/'+id).subscribe({next:()=>this.loadCalendar(),error:e=>this.fail(e)});}
}

function injectApiBase(calendarBase:string){return calendarBase.replace(/calendars$/,'workspace/sla/calendars');}
