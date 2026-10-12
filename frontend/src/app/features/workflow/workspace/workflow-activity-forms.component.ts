import { Component, EventEmitter, Input, OnChanges, Output, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivityFormStep, activityFormSteps } from './workflow-form-routing.types';
import { LocaleService } from '@core/i18n/locale.service';
import { ActivityForm, WorkflowActivityFormsService } from './workflow-activity-forms.service';

export interface ActivityFormPreviewRequest { form: ActivityForm; taskTypeId?: string; departmentCode: string; fieldActivityCode: string; versionNo: number }

@Component({selector:'app-workflow-activity-forms',standalone:true,imports:[FormsModule],template:`
<section class="space-y-4 p-4" aria-labelledby="wf-activity-forms-title" [attr.aria-busy]="loading()">
  <h3 id="wf-activity-forms-title" tabindex="-1" class="text-sm font-semibold">{{ t('Configure activity forms','إعداد نماذج النشاط') }}</h3>
  <p class="text-xs wf-muted">{{ t('The first form opens when this activity starts. Configure every form’s destinations in Actions. List order is used only when you choose Next form; explicit destinations can skip, return, or leave the activity.','يفتح النموذج الأول عند بدء النشاط. اضبط وجهات كل نموذج في الإجراءات. يُستخدم ترتيب القائمة فقط عند اختيار النموذج التالي؛ ويمكن للوجهات المحددة تخطي النماذج أو الرجوع أو مغادرة النشاط.') }}</p>
  @if(reviewRequired){<p role="alert">{{t('Review the forms after changing Task Type.','راجع النماذج بعد تغيير نوع المهمة.')}}</p><button type="button" class="wf-btn-secondary" [disabled]="readonly||loading()||!selectedAvailable()" (click)="confirmReview()">{{t('Confirm forms reviewed','تأكيد مراجعة النماذج')}}</button>}
  @if(steps.length){
    <ol class="space-y-2" [attr.aria-label]="t('Form sequence','تسلسل النماذج')">
      @for(step of steps;track step.id;let i=$index){
        <li class="border rounded-lg p-3 space-y-2">
          <strong>{{ i+1 }}. {{ step.name || step.formId }} - v{{ step.versionNo }}</strong>
          @if(!loading()&&!stepAvailable(step)){<p role="alert">{{t('Review this form version','راجع إصدار النموذج')}}</p>}
          <div class="flex flex-wrap gap-2">
            <button type="button" class="wf-btn-secondary" [disabled]="readonly || i===0" (click)="move(i,-1)" [attr.aria-label]="t('Move form earlier','تقديم النموذج')">&#8593;</button>
            <button type="button" class="wf-btn-secondary" [disabled]="readonly || i===steps.length-1" (click)="move(i,1)" [attr.aria-label]="t('Move form later','تأخير النموذج')">&#8595;</button>
            <label>{{t('Published version','الإصدار المنشور')}} <select class="wf-input" [disabled]="readonly" [ngModel]="step.versionNo" (ngModelChange)="version(step,$event)">@for(v of versions(step.formId);track v){<option [ngValue]="v">v{{v}}</option>}</select></label>
            <button type="button" class="wf-btn-secondary" [disabled]="readonly" (click)="remove(step.id)">{{t('Remove','إزالة')}}</button>
            <button type="button" class="wf-btn-secondary" [disabled]="!stepAvailable(step)" (click)="previewStep(step)">{{t('Preview','معاينة')}}</button>
          </div>
        </li>
      }
    </ol>
  }
  @if(!taskTypeId){
    <p role="status">{{ t('Select a Task Type in General first.','اختر نوع المهمة في الإعدادات العامة أولاً.') }}</p>
    <button type="button" class="underline" (click)="editContext.emit('taskTypeId')">{{ t('Go to General','الانتقال إلى الإعدادات العامة') }}</button>
  } @else if(loading()) {
    <p role="status">{{ t('Loading related forms…','جارٍ تحميل النماذج…') }}</p>
  } @else if(error()) {
    <p role="alert">{{ error() }}</p><button type="button" class="underline" (click)="load()">{{ t('Try again','حاول مرة أخرى') }}</button>
  } @else {
    @if(steps.length && !selectedAvailable()) {<p role="alert">{{ t('The saved form is no longer available for this Task Type. Select a usable form before publishing.','النموذج المحفوظ غير متاح لنوع المهمة. اختر نموذجاً صالحاً قبل النشر.') }}</p>}
    @if(!steps.length){<p role="status">{{ t('A form selection is required before publishing.','يجب اختيار نموذج قبل النشر.') }}</p>}
    <div class="space-y-3" role="group" [attr.aria-label]="t('Related forms','النماذج المرتبطة')">
      @for(f of items();track f.id){
        <article class="rounded-lg border p-3 space-y-2" [class.border-primary]="isSelected(f.id)" [attr.data-form-code]="f.code">
          <h4 class="font-semibold">{{ name(f) }}</h4>
          <p class="text-xs"><span dir="ltr">{{ f.code }}</span> · {{ status(f) }} · {{ f.currentVersionNo ? 'v' + f.currentVersionNo : t('Never published','غير منشور') }}</p>
          @if(!f.isUsable){<p class="text-xs">{{ t('Not available for use','غير متاح للاستخدام') }}</p>}
          @if(isSelected(f.id)){<p class="text-sm font-semibold" role="status">{{ t('Added to sequence','مضاف للتسلسل') }}</p>}
          <div class="flex flex-wrap gap-2">
            @if(f.versionNos.length){<button type="button" class="wf-btn-secondary" [id]="'wf-form-preview-' + f.id" (click)="open(f)">{{ t('Preview','معاينة') }}</button>}
            <button type="button" class="wf-btn-primary" [disabled]="readonly || !f.isUsable || !f.currentVersionNo || isSelected(f.id)" [attr.aria-pressed]="isSelected(f.id)" (click)="select(f)">{{ isSelected(f.id) ? t('Added','مضاف') : t('Add form','إضافة نموذج') }}</button>
          </div>
        </article>
      } @empty {<p role="status">{{ t('No forms are associated with this Task Type. Add forms in Task Types first.','لا توجد نماذج مرتبطة بنوع المهمة. أضف النماذج في أنواع المهام أولاً.') }}</p>}
    </div>
  }
</section>`})
export class WorkflowActivityFormsComponent implements OnChanges {
  @Input() configuration='{}'; @Input() readonly=false; @Input() legacy=false;
  @Output() configurationChange=new EventEmitter<string>();
  @Output() editContext=new EventEmitter<'taskTypeId'>();
  @Output() preview=new EventEmitter<ActivityFormPreviewRequest>();
  private api=inject(WorkflowActivityFormsService); private locale=inject(LocaleService);
  private request=0; private loadedType=''; private config:Record<string,unknown>={};
  taskTypeId=''; selectedId=''; selectedVersion=0; steps:ActivityFormStep[]=[]; reviewRequired=false;
  readonly items=signal<ActivityForm[]>([]); readonly loading=signal(false); readonly error=signal('');
  t(en:string,ar:string){return this.locale.locale()==='ar'?ar:en;}
  name(f:ActivityForm){return (this.locale.locale()==='ar'?f.nameAr:f.nameEn)||f.nameEn||f.code;}
  status(f:ActivityForm){const names:Record<string,[string,string]>={PUBLISHED:['Published','منشور'],DRAFT:['Draft','مسودة'],DEPRECATED:['Deprecated','متوقف'],ARCHIVED:['Archived','مؤرشف']};const name=names[f.status.toUpperCase()];return name?this.t(...name):f.status;}
  ngOnChanges(){
    try{this.config=JSON.parse(this.configuration||'{}')??{};}catch{this.config={};}
    this.taskTypeId=String(this.config['taskTypeId']||'');this.selectedId=String(this.config['formDefinitionId']||'');this.selectedVersion=Number(this.config['formVersionNo']||0);
    this.steps=activityFormSteps(this.config);
    this.reviewRequired=!!this.config['formStepsReviewRequired'];
    if(this.loadedType!==this.taskTypeId){this.loadedType=this.taskTypeId;this.load();}
  }
  selectedAvailable(){return this.steps.every(s=>this.stepAvailable(s));}
  load(){
    const request=++this.request;this.items.set([]);this.loading.set(false);this.error.set('');
    if(!this.taskTypeId)return;
    this.loading.set(true);this.api.listForTaskType(this.taskTypeId).subscribe({
      next:page=>{if(request!==this.request)return;this.items.set(page.items);this.loading.set(false);},
      error:e=>{if(request!==this.request)return;this.loading.set(false);this.error.set(e.status===403?this.t('You do not have permission to view these forms.','ليس لديك صلاحية عرض النماذج.'):this.t('Could not load related forms.','تعذر تحميل النماذج المرتبطة.'));}});
  }
  isSelected(id:string){return this.steps.some(s=>s.formId===id);}
  versions(id:string){return this.items().find(f=>f.id===id)?.versionNos??[];}
  stepAvailable(step:ActivityFormStep){return this.items().some(f=>f.id===step.formId&&f.isUsable&&f.versionNos.includes(step.versionNo));}
  previewStep(step:ActivityFormStep){const f=this.items().find(f=>f.id===step.formId);if(f&&this.taskTypeId)this.preview.emit({form:f,taskTypeId:this.taskTypeId,departmentCode:f.departmentCode,fieldActivityCode:f.fieldActivityCode,versionNo:step.versionNo});}
  move(index:number,offset:number){if(this.readonly||!this.steps[index+offset])return;[this.steps[index],this.steps[index+offset]]=[this.steps[index+offset],this.steps[index]];this.save();}
  remove(id:string){if(this.readonly)return;this.steps=this.steps.filter(s=>s.id!==id);this.save();}
  version(step:ActivityFormStep,value:number){if(this.readonly||step.versionNo===value)return;step.versionNo=value;step.reviewRequired=true;this.save();}
  confirmReview(){if(this.readonly||!this.selectedAvailable())return;this.config['formStepsReviewRequired']=false;this.reviewRequired=false;this.save();}
  save(){
    this.config={...this.config,formSteps:this.steps};delete this.config['formDefinitionId'];delete this.config['formVersionNo'];
    this.configurationChange.emit(JSON.stringify(this.config));
  }
  select(f:ActivityForm){
    if(this.readonly||!f.isUsable||!f.currentVersionNo||!this.items().some(x=>x.id===f.id)||this.isSelected(f.id))return;
    this.steps.push({id:crypto.randomUUID(),formId:f.id,versionNo:f.currentVersionNo,name:this.name(f),rules:[],otherwise:{kind:'Next'}});
    if(!this.config['departmentCode']&&!this.config['fieldActivityCode']){this.config['departmentCode']=f.departmentCode;this.config['fieldActivityCode']=f.fieldActivityCode;}
    this.save();
  }
  open(f:ActivityForm){if(this.taskTypeId&&f.versionNos.length)this.preview.emit({form:f,taskTypeId:this.taskTypeId,departmentCode:f.departmentCode,fieldActivityCode:f.fieldActivityCode,versionNo:this.selectedId===f.id&&f.versionNos.includes(this.selectedVersion)?this.selectedVersion:f.currentVersionNo??f.versionNos[0]});}
}
