import { Component, EventEmitter, Input, Output, forwardRef, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocaleService } from '@core/i18n/locale.service';
import { FormRoutingField, RoutingCondition, RoutingGroup, routingOperators } from './workflow-form-routing.types';

@Component({selector:'app-workflow-routing-group',standalone:true,imports:[FormsModule,forwardRef(()=>WorkflowRoutingGroupComponent)],template:`
<fieldset class="border rounded-lg p-3 space-y-3" [disabled]="readonly">
  <legend class="px-1"><select class="wf-input" [ngModel]="group.match" (ngModelChange)="group.match=$event;changed.emit()" [attr.aria-label]="t('Group matching','مطابقة المجموعة')"><option value="All">{{t('All conditions (AND)','كل الشروط (و)')}}</option><option value="Any">{{t('Any condition (OR)','أي شرط (أو)')}}</option></select></legend>
  @for(c of group.conditions;track $index;let index=$index){
    <div class="space-y-2 border-b pb-3">
      <label class="block text-xs">{{t('Field','الحقل')}}<select class="wf-input w-full" [ngModel]="c.field" (ngModelChange)="changeField(c,$event)"><option value="">{{t('Select field','اختر الحقل')}}</option>@if(c.field&&!field(c)){<option [value]="c.field">{{c.field}} — {{t('Unavailable','غير متاح')}}</option>}@for(f of fields;track f.dataName){<option [value]="f.dataName">{{label(f)}} ({{f.dataName}})</option>}</select></label>
      <label class="block text-xs">{{t('Operator','المقارنة')}}<select class="wf-input w-full" [ngModel]="c.operator" (ngModelChange)="changeOperator(c,$event)">@for(op of operators(c);track op){<option [value]="op">{{operatorLabel(op)}}</option>}</select></label>
      @if(!empty(c)){
        <label class="block text-xs">{{c.operator==='between'?t('From (inclusive)','من (شامل)'):t('Value','القيمة')}}
          @if(field(c)?.type==='yes_no'){<select class="wf-input w-full" [ngModel]="c.value" (ngModelChange)="c.value=$event;changed.emit()"><option [ngValue]="null">{{t('Select','اختر')}}</option><option [ngValue]="true">{{t('Yes','نعم')}}</option><option [ngValue]="false">{{t('No','لا')}}</option></select>}
          @else if(field(c)?.type==='single_choice'||field(c)?.type==='multiple_choice'){
            @if(multiple(c)){
              <select class="wf-input w-full" multiple [ngModel]="c.value" (ngModelChange)="c.value=$event;changed.emit()">@for(choice of field(c)?.choices??[];track choice.value){<option [ngValue]="choice.value">{{locale.isRtl()?choice.labelAr:choice.labelEn}}</option>}</select>
            } @else {
              <select class="wf-input w-full" [ngModel]="c.value" (ngModelChange)="c.value=$event;changed.emit()"><option [ngValue]="null">{{t('Select option','اختر القيمة')}}</option>@for(choice of field(c)?.choices??[];track choice.value){<option [ngValue]="choice.value">{{locale.isRtl()?choice.labelAr:choice.labelEn}}</option>}</select>
            }
          } @else {<input class="wf-input w-full" [type]="inputType(c)" [step]="field(c)?.type==='numeric'?'any':null" [ngModel]="inputValue(c.value,c)" (ngModelChange)="c.value=storedValue($event,c);changed.emit()">}
        </label>
        @if(c.operator==='between'){<label class="block text-xs">{{t('To (inclusive)','إلى (شامل)')}}<input class="wf-input w-full" [type]="inputType(c)" [step]="field(c)?.type==='numeric'?'any':null" [ngModel]="inputValue(c.to,c)" (ngModelChange)="c.to=storedValue($event,c);changed.emit()"></label>}
      }
      <button type="button" class="underline text-xs" (click)="group.conditions.splice(index,1);changed.emit()">{{t('Remove condition','إزالة الشرط')}}</button>
    </div>
  }
  @for(child of group.groups;track $index;let index=$index){
    <app-workflow-routing-group [group]="child" [fields]="fields" [readonly]="readonly" [depth]="depth+1" (changed)="changed.emit()" />
    <button type="button" class="underline text-xs" (click)="group.groups.splice(index,1);changed.emit()">{{t('Remove group','إزالة المجموعة')}}</button>
  }
  <div class="flex flex-wrap gap-3">
    <button type="button" class="wf-btn-secondary" (click)="addCondition()">{{t('Add condition','إضافة شرط')}}</button>
    <button type="button" class="wf-btn-secondary" [disabled]="depth>=8" (click)="group.groups.push({match:'All',conditions:[],groups:[]});changed.emit()">{{t('Add group','إضافة مجموعة')}}</button>
  </div>
  @if(!group.conditions.length&&!group.groups.length){<p class="text-xs" role="status">{{t('Add at least one condition.','أضف شرطاً واحداً على الأقل.')}}</p>}
</fieldset>`})
export class WorkflowRoutingGroupComponent {
  @Input({required:true}) group!:RoutingGroup; @Input() fields:FormRoutingField[]=[]; @Input() readonly=false; @Input() depth=0;
  @Output() changed=new EventEmitter<void>(); readonly locale=inject(LocaleService);
  t(en:string,ar:string){return this.locale.isRtl()?ar:en;}
  label(f:FormRoutingField){return this.locale.isRtl()?f.labelAr:f.labelEn;}
  field(c:RoutingCondition){return this.fields.find(f=>f.dataName===c.field);}
  operators(c:RoutingCondition){const available=routingOperators(this.field(c)?.type??'');return available.includes(c.operator)?available:[c.operator,...available];}
  changeField(c:RoutingCondition,name:string){c.field=name;c.operator=routingOperators(this.field(c)?.type??'')[0];c.value=null;c.to=null;this.changed.emit();}
  changeOperator(c:RoutingCondition,operator:string){c.operator=operator;c.value=this.multiple(c)?[]:null;c.to=null;this.changed.emit();}
  addCondition(){this.group.conditions.push({field:'',operator:'equal',value:null,to:null});this.changed.emit();}
  empty(c:RoutingCondition){return ['is_empty','is_not_empty'].includes(c.operator);}
  multiple(c:RoutingCondition){return ['in','not_in','contains_any','contains_all','contains_none'].includes(c.operator);}
  inputType(c:RoutingCondition){return ({numeric:'number',date:'date',date_time:'datetime-local',time:'time'} as Record<string,string>)[this.field(c)?.type??'']??'text';}
  inputValue(value:unknown,c:RoutingCondition){return this.field(c)?.type==='date_time'&&typeof value==='string'?value.replace(' ','T'):value;}
  storedValue(value:unknown,c:RoutingCondition){return this.field(c)?.type==='date_time'&&typeof value==='string'?value.replace('T',' '):value;}
  operatorLabel(op:string){const labels:Record<string,[string,string]>={equal:['Equals','يساوي'],not_equal:['Does not equal','لا يساوي'],greater_than:['Greater than / After','أكبر من / بعد'],greater_or_equal:['At least / On or after','أكبر أو يساوي / في أو بعد'],less_than:['Less than / Before','أقل من / قبل'],less_or_equal:['At most / On or before','أقل أو يساوي / في أو قبل'],between:['Between (inclusive)','بين (شامل)'],in:['Is one of','إحدى القيم'],not_in:['Is not one of','ليس إحدى القيم'],contains_any:['Contains any','يحتوي أي قيمة'],contains_all:['Contains all','يحتوي كل القيم'],contains_none:['Contains none','لا يحتوي أياً منها'],contains:['Contains','يحتوي'],starts_with:['Starts with','يبدأ بـ'],ends_with:['Ends with','ينتهي بـ'],is_empty:['Is empty','فارغ'],is_not_empty:['Is not empty','غير فارغ']};return labels[op]?this.t(...labels[op]):op;}
}
