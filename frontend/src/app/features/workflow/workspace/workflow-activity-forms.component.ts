import { Component, EventEmitter, Input, OnChanges, Output, computed, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { LocaleService } from '@core/i18n/locale.service';
import { ActivityForm, WorkflowActivityFormsService } from './workflow-activity-forms.service';
import { ReferenceItem, WorkflowWorkspaceService } from './workflow-workspace.service';

type ContextField = 'departmentCode' | 'fieldActivityCode';
/** What the designer opens its preview dialog for. */
export interface ActivityFormPreviewRequest { form: ActivityForm; departmentCode: string; fieldActivityCode: string; versionNo: number }

const STATUS_LABELS: Record<string, [string, string]> = {
  PUBLISHED: ['Published', 'منشور'], DRAFT: ['Draft', 'مسودة'], DEPRECATED: ['Deprecated', 'متوقف'], ARCHIVED: ['Archived', 'مؤرشف'],
};

/**
 * The activity's Form tab: every Form Engine form filed under its Department + Field Activity Type, found as soon as both
 * are chosen. Discovery only — listing a form neither attaches it to the activity nor makes it required. Forms that cannot
 * take submissions are listed apart so they are never mistaken for usable ones.
 */
@Component({selector:'app-workflow-activity-forms',standalone:true,template:`
<section class="space-y-4 p-4" aria-labelledby="wf-activity-forms-title" [attr.aria-busy]="loading() || loadingMore()">
  <h3 id="wf-activity-forms-title" tabindex="-1" class="text-sm font-semibold outline-none">{{ t('Related forms','النماذج المرتبطة') }}</h3>
  <p class="wf-muted text-xs">{{ t('Form Engine forms filed under this activity’s Department and Field Activity Type. Listing a form here does not attach it to the activity or make it required.','نماذج محرك النماذج المصنفة تحت قسم هذا النشاط ونوع نشاطه الميداني. ظهور النموذج هنا لا يربطه بالنشاط ولا يجعله إلزامياً.') }}</p>
  @if(legacyFields()){
    <p class="rounded-lg border px-3 py-2 text-xs" data-testid="legacy-form-fields">{{ t('This activity also has ' + legacyFields() + ' task question(s) from the earlier designer. They are kept unchanged.', 'يحتوي هذا النشاط أيضاً على ' + legacyFields() + ' من أسئلة المهمة من المصمم السابق. ستبقى دون تغيير.') }}</p>
  }
  @if(legacy){
    <p role="status" class="text-sm">{{ t('This workflow uses the earlier activity settings, which have no Department and Field Activity Type, so related forms cannot be looked up.','يستخدم مسار العمل هذا إعدادات النشاط السابقة التي لا تتضمن القسم ونوع النشاط الميداني، لذا لا يمكن البحث عن النماذج المرتبطة.') }}</p>
  } @else if(missing().length){
    <div role="status" class="space-y-2 rounded-lg px-3 py-3 text-sm" style="background:var(--acc-warn-bg);color:var(--acc-warn-fg)">
      <p>{{ t('Related forms are found from the activity’s Department and Field Activity Type. Still missing:','تُحدد النماذج المرتبطة من قسم النشاط ونوع النشاط الميداني. ما زال ناقصاً:') }}</p>
      <ul class="list-disc ps-5">@for(m of missing();track m){<li>{{ m === 'departmentCode' ? t('Department','القسم') : t('Field Activity Type','نوع النشاط الميداني') }}</li>}</ul>
      <button type="button" id="wf-activity-forms-context" class="underline" (click)="editContext.emit(missing()[0])">{{ t('Go to General settings','الانتقال إلى الإعدادات العامة') }}</button>
    </div>
  } @else {
    <p class="text-xs" data-testid="forms-context"><span class="font-medium">{{ t('Department','القسم') }}:</span> {{ departmentName() }} · <span class="font-medium">{{ t('Field Activity Type','نوع النشاط الميداني') }}:</span> {{ fieldActivityName() }}</p>
    @if(loading()){
      <p role="status" class="text-sm">{{ t('Finding related forms…','جارٍ البحث عن النماذج المرتبطة…') }}</p>
    } @else if(forbidden()){
      <p role="alert" class="rounded-lg px-3 py-3 text-sm" style="background:var(--acc-warn-bg);color:var(--acc-warn-fg)">{{ t('You do not have permission to view forms. Viewing an activity’s forms needs the Manage definitions or View forms permission. Ask an administrator for access.','ليست لديك صلاحية عرض النماذج. يتطلب عرض نماذج النشاط صلاحية إدارة التعريفات أو عرض النماذج. اطلب الصلاحية من المسؤول.') }}</p>
    } @else if(error()){
      <div role="alert" class="space-y-2 rounded-lg px-3 py-3 text-sm" style="background:var(--acc-danger-bg);color:var(--acc-danger-fg)"><p>{{ error() }}</p>
        <button type="button" id="wf-activity-forms-retry" class="underline" (click)="load()">{{ t('Try again','حاول مرة أخرى') }}</button></div>
    } @else if(total() === 0){
      <p role="status" class="rounded-lg border px-3 py-3 text-sm">{{ t('No Form Engine forms are filed under this Department and Field Activity Type yet.','لا توجد نماذج في محرك النماذج مصنفة تحت هذا القسم ونوع النشاط حتى الآن.') }}</p>
    } @else {
      <p role="status" class="text-xs wf-muted">{{ summary() }}</p>
      @if(usable().length){
        <ul class="space-y-2" [attr.aria-label]="t('Usable forms','النماذج القابلة للاستخدام')">
          @for(f of usable();track f.id){<ng-container *ngTemplateOutlet="card; context:{$implicit:f}" />}
        </ul>
      } @else {
        <p class="rounded-lg px-3 py-2 text-sm" style="background:var(--acc-warn-bg);color:var(--acc-warn-fg)">{{ t('None of the related forms can be used: none has a published version in service.','لا يمكن استخدام أي من النماذج المرتبطة: لا يوجد لأي منها إصدار منشور في الخدمة.') }}</p>
      }
      @if(unusable().length){
        <div class="space-y-2">
          <h4 class="text-xs font-semibold">{{ t('Not available for use','غير متاحة للاستخدام') }} ({{ unusableTotal() }})</h4>
          <p class="wf-muted text-xs">{{ t('Drafts that were never published, deprecated and archived forms accept no new submissions. Their published versions stay viewable for history.','المسودات غير المنشورة والنماذج المتوقفة والمؤرشفة لا تقبل إرساليات جديدة. تبقى إصداراتها المنشورة متاحة للعرض كسجل.') }}</p>
          <ul class="space-y-2" [attr.aria-label]="t('Forms not available for use','النماذج غير المتاحة للاستخدام')">
            @for(f of unusable();track f.id){<ng-container *ngTemplateOutlet="card; context:{$implicit:f}" />}
          </ul>
        </div>
      }
      @if(items().length < total()){
        <button type="button" class="wf-btn-secondary w-full" [disabled]="loadingMore()" (click)="loadMore()">{{ loadingMore() ? t('Loading…','جارٍ التحميل…') : t('Show more forms','عرض المزيد من النماذج') }}</button>
        @if(moreError()){<p role="alert" class="text-xs" style="color:var(--acc-danger-fg)">{{ moreError() }}</p>}
      }
    }
  }
</section>
<ng-template #card let-f>
  <li class="rounded-lg border p-3 text-sm" [attr.data-form-code]="f.code">
    <div class="flex flex-wrap items-start justify-between gap-2">
      <div class="min-w-0">
        <p class="font-semibold break-words">{{ name(f) }}</p>
        <p class="wf-muted text-xs"><span dir="ltr" class="font-mono">{{ f.code }}</span></p>
      </div>
      <span [class]="'app-badge ' + badge(f)">{{ status(f) }}</span>
    </div>
    <p class="mt-1 text-xs">{{ versionText(f) }}</p>
    @if(f.versionNos.length){
      <button type="button" class="wf-btn-secondary mt-2" [id]="'wf-form-preview-' + f.id" [attr.aria-label]="t('Preview','معاينة') + ' ' + name(f)" (click)="open(f)">{{ t('Preview','معاينة') }}</button>
    }
  </li>
</ng-template>`, imports:[NgTemplateOutlet]})
export class WorkflowActivityFormsComponent implements OnChanges {
  @Input() configuration='{}';
  /** No workspace settings: activities have no Department / FA Type to look forms up by. */
  @Input() legacy=false;
  /** Take the user to the General field that is still missing. */
  @Output() editContext=new EventEmitter<ContextField>();
  /** Open the read-only preview of one of the listed forms. */
  @Output() preview=new EventEmitter<ActivityFormPreviewRequest>();
  private api=inject(WorkflowActivityFormsService); private refs=inject(WorkflowWorkspaceService); private locale=inject(LocaleService);
  /** Bumped by every new context and every reload; a response for an older one is dropped. */
  private request=0; private nextPage=2; private key=''; private department=''; private fieldActivity='';
  readonly missing=signal<ContextField[]>([]); readonly legacyFields=signal(0); readonly items=signal<ActivityForm[]>([]); readonly total=signal(0); readonly usableTotal=signal(0);
  readonly loading=signal(false); readonly loadingMore=signal(false); readonly error=signal(''); readonly forbidden=signal(false); readonly moreError=signal('');
  readonly usable=computed(()=>this.items().filter(f=>f.isUsable)); readonly unusable=computed(()=>this.items().filter(f=>!f.isUsable));
  readonly unusableTotal=computed(()=>this.total()-this.usableTotal());
  private readonly departments=signal<ReferenceItem[]>([]); private readonly fieldTypes=signal<ReferenceItem[]>([]); private departmentsLoaded=false; private fieldTypesFor='';
  t(en:string,ar:string){return this.locale.locale()==='ar'?ar:en;}
  name(f:ActivityForm){return (this.locale.locale()==='ar'?f.nameAr:f.nameEn)||f.nameEn||f.code;}
  status(f:ActivityForm){const l=STATUS_LABELS[f.status?.toUpperCase()];return l?this.t(l[0],l[1]):f.status;}
  badge(f:ActivityForm){return f.isUsable?'app-badge--success':f.status?.toUpperCase()==='DRAFT'?'app-badge--neutral':'app-badge--warn';}
  versionText(f:ActivityForm){
    if(!f.currentVersionNo)return this.t('Never published — not usable until it is published in the Form Engine.','لم يُنشر بعد — لا يمكن استخدامه حتى يُنشر في محرك النماذج.');
    const older=f.versionNos.length>1?this.t(` · ${f.versionNos.length} published versions`,` · ${f.versionNos.length} إصدارات منشورة`):'';
    if(!f.isUsable)return this.t(`Last published version v${f.currentVersionNo}`,`آخر إصدار منشور v${f.currentVersionNo}`)+older;
    const draft=f.status?.toUpperCase()==='DRAFT'?this.t(' · newer draft in progress',' · توجد مسودة أحدث قيد التحرير'):'';
    return this.t(`Available version v${f.currentVersionNo}`,`الإصدار المتاح v${f.currentVersionNo}`)+older+draft;
  }
  summary(){const n=this.items().length,total=this.total();return n<total?this.t(`Showing ${n} of ${total} related forms.`,`عرض ${n} من ${total} نموذجاً مرتبطاً.`):this.t(`${total} related form${total===1?'':'s'}, ${this.usableTotal()} usable.`,`${total} نموذج مرتبط، ${this.usableTotal()} قابل للاستخدام.`);}
  departmentName(){return this.label(this.departments(),this.department);}
  fieldActivityName(){return this.label(this.fieldTypes(),this.fieldActivity);}
  private label(list:ReferenceItem[],code:string){const item=list.find(i=>i.code===code);return item?`${this.locale.locale()==='ar'?item.nameAr:item.nameEn} (${code})`:code;}
  ngOnChanges(){
    let c:Record<string,unknown>={};try{c=JSON.parse(this.configuration||'{}')??{};}catch{c={};}
    const department=typeof c['departmentCode']==='string'?c['departmentCode'].trim():'',fieldActivity=typeof c['fieldActivityCode']==='string'?c['fieldActivityCode'].trim():'';
    this.legacyFields.set(Array.isArray(c['formFields'])?c['formFields'].length:0);
    const missing:ContextField[]=[];if(!department)missing.push('departmentCode');if(!fieldActivity)missing.push('fieldActivityCode');this.missing.set(missing);
    const key=missing.length||this.legacy?'':`${department}\u0000${fieldActivity}`;
    // Unrelated edits to the activity keep the loaded list; a different combination starts over.
    if(key===this.key)return;
    this.key=key;this.department=department;this.fieldActivity=fieldActivity;this.loadNames();this.load();
  }
  load(){
    const request=++this.request;this.items.set([]);this.total.set(0);this.usableTotal.set(0);this.error.set('');this.forbidden.set(false);this.moreError.set('');this.loading.set(false);this.loadingMore.set(false);
    if(!this.key)return;
    this.loading.set(true);
    this.api.list(this.department,this.fieldActivity,1).subscribe({
      next:page=>{if(request!==this.request)return;this.nextPage=2;this.items.set(page.items);this.total.set(page.totalCount);this.usableTotal.set(page.usableCount);this.loading.set(false);},
      error:e=>{if(request!==this.request)return;this.loading.set(false);this.fail(e,false);}});
  }
  loadMore(){
    if(!this.key||this.loadingMore())return;
    const request=this.request,next=this.nextPage;
    this.loadingMore.set(true);this.moreError.set('');
    this.api.list(this.department,this.fieldActivity,next).subscribe({
      next:page=>{if(request!==this.request)return;const seen=new Set(this.items().map(f=>f.id));this.nextPage=next+1;this.items.update(list=>[...list,...page.items.filter(f=>!seen.has(f.id))]);this.total.set(page.totalCount);this.usableTotal.set(page.usableCount);this.loadingMore.set(false);},
      error:e=>{if(request!==this.request)return;this.loadingMore.set(false);this.fail(e,true);}});
  }
  open(f:ActivityForm){if(this.key&&f.versionNos.length)this.preview.emit({form:f,departmentCode:this.department,fieldActivityCode:this.fieldActivity,versionNo:f.currentVersionNo??f.versionNos[0]});}
  private fail(e:unknown,more:boolean){
    const status=e instanceof HttpErrorResponse?e.status:0;
    if(status===401||status===403){this.forbidden.set(true);return;}
    const message=status===0?this.t('The server could not be reached. Check the connection and try again.','تعذر الوصول إلى الخادم. تحقق من الاتصال وحاول مرة أخرى.'):this.t('The related forms could not be loaded.','تعذر تحميل النماذج المرتبطة.');
    (more?this.moreError:this.error).set(message);
  }
  private loadNames(){
    if(!this.departmentsLoaded&&this.key){this.departmentsLoaded=true;this.refs.references('departments').subscribe({next:r=>this.departments.set(r),error:()=>this.departmentsLoaded=false});}
    if(this.department&&this.fieldTypesFor!==this.department){const department=this.department;this.fieldTypesFor=department;this.fieldTypes.set([]);this.refs.references('field-activity-types',department).subscribe({next:r=>{if(this.department===department)this.fieldTypes.set(r);},error:()=>{if(this.fieldTypesFor===department)this.fieldTypesFor='';}});}
  }
}
