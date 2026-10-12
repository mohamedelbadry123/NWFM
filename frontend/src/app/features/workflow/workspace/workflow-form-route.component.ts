import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocaleService } from '@core/i18n/locale.service';
import { ActivityFormStep, FormRouteDestination } from './workflow-form-routing.types';

export interface FormRouteNode { nodeKey: string; name: string; type: string }
@Component({selector:'app-workflow-form-route',standalone:true,imports:[FormsModule],template:`
<div class="space-y-2">
  <label class="block text-xs">{{t('Go to','انتقل إلى')}}<select class="wf-input w-full" [disabled]="readonly" [ngModel]="destination.kind" (ngModelChange)="destination.kind=$event;destination.target=null;changed.emit()"><option value="Next">{{t('Next form; after last form, follow forward route','النموذج التالي؛ وبعد الأخير اتبع المسار الأمامي')}}</option><option value="Form">{{t('Specific form in this activity','نموذج محدد في هذا النشاط')}}</option><option value="Activity">{{t('Activity or End in this workflow','نشاط أو نهاية في سير العمل')}}</option></select></label>
  @if(destination.kind==='Form'){
    <p class="text-xs wf-muted">{{t('Choose any form: continue, skip, return, or repeat. This destination stays fixed if the form list is reordered.','اختر أي نموذج: متابعة أو تخطٍ أو رجوع أو تكرار. تبقى هذه الوجهة ثابتة عند إعادة ترتيب القائمة.')}}</p>
    <label class="block text-xs">{{t('Destination form','النموذج الوجهة')}}<select class="wf-input w-full" [disabled]="readonly" [ngModel]="destination.target" (ngModelChange)="destination.target=$event;changed.emit()"><option [ngValue]="null">{{t('Select form','اختر النموذج')}}</option>@if(destination.target&&!hasForm()){<option [value]="destination.target">{{t('Removed form — select again','نموذج محذوف — أعد الاختيار')}}</option>}@for(s of steps;track s.id;let i=$index){<option [value]="s.id">{{i+1}}. {{s.name||s.formId}} {{s.id===currentStepId?t('(Repeat form)','(تكرار النموذج)'):''}}</option>}</select></label>
  }
  @if(destination.kind==='Activity'){
    <label class="block text-xs">{{t('Destination activity','النشاط الوجهة')}}<select class="wf-input w-full" [disabled]="readonly" [ngModel]="destination.target" (ngModelChange)="destination.target=$event;changed.emit()"><option [ngValue]="null">{{t('Select activity','اختر النشاط')}}</option>@if(destination.target&&!hasNode()){<option [value]="destination.target">{{t('Removed activity — select again','نشاط محذوف — أعد الاختيار')}}</option>}@for(n of nodes;track n.nodeKey){<option [value]="n.nodeKey">{{n.name||n.nodeKey}} {{n.nodeKey===currentNodeKey?t('(Restart activity)','(إعادة النشاط)'):''}}</option>}</select></label>
    <p class="text-xs wf-muted">{{t('Move forward or return to an earlier activity. This finishes the current activity; the destination starts a fresh execution at its first form. Routes stay within the same workflow and execution branch.','انتقل للأمام أو ارجع إلى نشاط سابق. ينتهي النشاط الحالي ويبدأ النشاط الوجهة تنفيذاً جديداً من نموذجه الأول. تبقى المسارات ضمن سير العمل وفرع التنفيذ نفسيهما.')}}</p>
  }
</div>`})
export class WorkflowFormRouteComponent {
  @Input({required:true}) destination!:FormRouteDestination; @Input() steps:ActivityFormStep[]=[]; @Input() nodes:FormRouteNode[]=[];
  @Input() currentNodeKey=''; @Input() currentStepId=''; @Input() readonly=false; @Output() changed=new EventEmitter<void>();
  private locale=inject(LocaleService);t(en:string,ar:string){return this.locale.isRtl()?ar:en;}
  hasForm(){return this.steps.some(s=>s.id===this.destination.target);}
  hasNode(){return this.nodes.some(n=>n.nodeKey===this.destination.target);}
}
