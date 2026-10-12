import { Component, EventEmitter, Input, OnChanges, Output, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowActivityFormsService } from './workflow-activity-forms.service';
import { ActivityFormStep, FormRoutingField, activityFormSteps, routingFields } from './workflow-form-routing.types';
import { WorkflowRoutingGroupComponent } from './workflow-routing-group.component';
import { FormRouteNode, WorkflowFormRouteComponent } from './workflow-form-route.component';

@Component({selector:'app-workflow-form-actions',standalone:true,imports:[FormsModule,WorkflowRoutingGroupComponent,WorkflowFormRouteComponent],template:`
<section class="space-y-4 p-4" aria-labelledby="wf-form-actions-title">
  <h3 id="wf-form-actions-title" tabindex="-1" class="font-semibold">{{t('Form actions and routing','إجراءات النموذج والتوجيه')}}</h3>
  <p class="text-xs wf-muted">{{t('Submit a form, then follow its first matching rule. Otherwise, follow its default route.','بعد إرسال النموذج اتبع أول قاعدة مطابقة، وإلا اتبع المسار الافتراضي.')}}</p>
  <p class="text-xs wf-muted">{{t('Configure both matching rules and Otherwise. Either can go to any form in this activity or an earlier or later activity in this workflow. Form list order does not restrict explicit destinations.','اضبط القواعد المطابقة ومسار وإلا. يمكن لكليهما الانتقال إلى أي نموذج في هذا النشاط أو إلى نشاط سابق أو لاحق في سير العمل. ترتيب قائمة النماذج لا يقيد الوجهات المحددة.')}}</p>
  @if(!steps.length){<p role="status">{{t('Add forms in the Form tab first.','أضف النماذج في تبويب النموذج أولاً.')}}</p>}
  @else {
    <label class="block">{{t('Configure actions for','إعداد إجراءات')}}<select class="wf-input w-full" [ngModel]="selectedId" (ngModelChange)="selectStep($event)">@for(s of steps;track s.id;let i=$index){<option [value]="s.id">{{i+1}}. {{s.name||s.formId}} · v{{s.versionNo}}</option>}</select></label>
    @if(loading()){<p role="status">{{t('Loading published fields…','تحميل حقول الإصدار المنشور…')}}</p>}
    @if(error()){<p role="alert">{{error()}}</p><button type="button" class="underline" (click)="load()">{{t('Retry','إعادة المحاولة')}}</button>}
    @if(selectedStep();as step){
      @if(step.reviewRequired){<p role="alert">{{t('Review these rules against the new form version, then confirm.','راجع القواعد وفق إصدار النموذج الجديد ثم أكد.')}}</p><button type="button" class="wf-btn-secondary" [disabled]="readonly||loading()||!!error()" (click)="step.reviewRequired=false;save()">{{t('Confirm rules reviewed','تأكيد مراجعة القواعد')}}</button>}
      @for(rule of step.rules;track rule.id;let i=$index){
        <article class="rounded-lg border p-3 space-y-3" [id]="'wf-routing-rule-'+rule.id">
          <label class="block text-xs">{{i+1}}. {{t('Rule name','اسم القاعدة')}}<input class="wf-input w-full" [disabled]="readonly" [(ngModel)]="rule.name" (ngModelChange)="save()"></label>
          <app-workflow-routing-group [group]="rule.when" [fields]="fields()" [readonly]="readonly||loading()||!!error()" (changed)="save()" />
          <app-workflow-form-route [destination]="rule.destination" [steps]="steps" [nodes]="nodes" [currentNodeKey]="nodeKey" [currentStepId]="step.id" [readonly]="readonly" (changed)="save()" />
          <div class="flex flex-wrap gap-3">
            <button type="button" class="wf-btn-secondary" [disabled]="readonly||i===0" (click)="move(i,-1)" [attr.aria-label]="t('Move rule earlier','تقديم القاعدة')">↑</button>
            <button type="button" class="wf-btn-secondary" [disabled]="readonly||i===step.rules.length-1" (click)="move(i,1)" [attr.aria-label]="t('Move rule later','تأخير القاعدة')">↓</button>
            <button type="button" class="underline text-xs" [disabled]="readonly" (click)="duplicate(i)">{{t('Duplicate','نسخ')}}</button>
            <button type="button" class="underline text-xs" [disabled]="readonly" (click)="step.rules.splice(i,1);save()">{{t('Delete rule','حذف القاعدة')}}</button>
          </div>
        </article>
      }
      <button type="button" class="wf-btn-primary" [disabled]="readonly||loading()||!!error()||step.rules.length>=100" (click)="addRule()">{{t('Add routing rule','إضافة قاعدة توجيه')}}</button>
      <div class="border rounded-lg p-3 space-y-2"><h4 class="font-semibold">{{t('Otherwise go to','وإلا انتقل إلى')}}</h4><app-workflow-form-route [destination]="step.otherwise" [steps]="steps" [nodes]="nodes" [currentNodeKey]="nodeKey" [currentStepId]="step.id" [readonly]="readonly" (changed)="save()" /></div>
    }
  }
</section>`})
export class WorkflowFormActionsComponent implements OnChanges {
  @Input() configuration='{}'; @Input() readonly=false; @Input() nodeKey=''; @Input() nodes:FormRouteNode[]=[];
  @Output() configurationChange=new EventEmitter<string>(); private locale=inject(LocaleService); private api=inject(WorkflowActivityFormsService);
  private config:Record<string,unknown>={}; private request=0; private loadedKey=''; steps:ActivityFormStep[]=[]; selectedId='';
  readonly fields=signal<FormRoutingField[]>([]);readonly loading=signal(false);readonly error=signal('');
  t(en:string,ar:string){return this.locale.isRtl()?ar:en;}
  ngOnChanges(){try{this.config=JSON.parse(this.configuration);}catch{this.config={};}this.steps=activityFormSteps(this.config);if(!this.steps.some(s=>s.id===this.selectedId))this.selectedId=this.steps[0]?.id??'';const step=this.selectedStep();const key=[this.nodeKey,this.config['taskTypeId'],step?.id,step?.formId,step?.versionNo].join(':');if(key!==this.loadedKey){this.loadedKey=key;this.load();}}
  selectedStep(){return this.steps.find(s=>s.id===this.selectedId);}
  selectStep(id:string){this.selectedId=id;this.load();}
  load(){const request=++this.request;const step=this.selectedStep();this.fields.set([]);this.error.set('');this.loading.set(false);if(!step)return;if(!this.config['taskTypeId']){this.error.set(this.t('Select a Task Type first.','اختر نوع المهمة أولاً.'));return;}this.loading.set(true);this.api.previewForTaskType(String(this.config['taskTypeId']),step.formId,step.versionNo).subscribe({next:preview=>{if(request!==this.request)return;try{this.fields.set(routingFields(JSON.parse(preview.schemaJson)));}catch{this.error.set(this.t('Invalid published form schema.','مخطط النموذج المنشور غير صالح.'));}this.loading.set(false);},error:()=>{if(request!==this.request)return;this.error.set(this.t('Could not load this form version. Review its selection or retry.','تعذر تحميل إصدار النموذج. راجع الاختيار أو أعد المحاولة.'));this.loading.set(false);}});}
  save(){if(this.readonly)return;const config:Record<string,unknown>={...this.config,formSteps:this.steps};delete config['formDefinitionId'];delete config['formVersionNo'];this.configurationChange.emit(JSON.stringify(config));}
  addRule(){const step=this.selectedStep();if(this.readonly||!step)return;step.rules.push({id:crypto.randomUUID(),name:this.t('New rule','قاعدة جديدة'),when:{match:'All',conditions:[],groups:[]},destination:{kind:'Next'}});this.save();}
  move(index:number,offset:number){const step=this.selectedStep();if(this.readonly||!step?.rules[index+offset])return;[step.rules[index],step.rules[index+offset]]=[step.rules[index+offset],step.rules[index]];this.save();}
  duplicate(index:number){const step=this.selectedStep();if(this.readonly||!step||step.rules.length>=100)return;const rule=structuredClone(step.rules[index]);rule.id=crypto.randomUUID();rule.name+=' '+this.t('(copy)','(نسخة)');step.rules.splice(index+1,0,rule);this.save();}
}
