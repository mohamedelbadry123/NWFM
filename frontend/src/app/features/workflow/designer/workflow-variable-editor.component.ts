import { Component, EventEmitter, Input, OnChanges, Output, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocaleService } from '@core/i18n/locale.service';

@Component({selector:'app-workflow-variable-editor',standalone:true,imports:[FormsModule],
  styleUrl:'../integrations/workflow-integrations.css',
  template:`<fieldset [disabled]="readonly" class="integration-grid">
    @for(row of rows; track $index) {
      <label>{{ t('Variable','المتغير') }}<input list="assignment-variables" [(ngModel)]="row.key" [ngModelOptions]="standalone" /></label>
      <label>{{ t('Value type','نوع القيمة') }}<select [(ngModel)]="row.type" [ngModelOptions]="standalone"><option>String</option><option>Number</option><option>Boolean</option><option>Json</option><option>Null</option></select></label>
      <label class="full">{{ t('Value','القيمة') }}
        @if(row.type==='Boolean') { <select [(ngModel)]="row.value" [ngModelOptions]="standalone"><option>true</option><option>false</option></select> }
        @else if(row.type!=='Null') { <input [(ngModel)]="row.value" [ngModelOptions]="standalone" /> }
      </label>
      <button type="button" (click)="rows.splice($index,1)">{{ t('Remove assignment','حذف القيمة') }}</button>
    }
    <datalist id="assignment-variables">@for(variable of variables;track variable.variableKey){<option [value]="variable.variableKey"></option>}</datalist>
    <button type="button" (click)="rows.push({key:'',type:'String',value:''})">{{ t('Add assignment','إضافة قيمة') }}</button>
    <button type="button" (click)="apply()">{{ t('Apply values','تطبيق القيم') }}</button>
  </fieldset>@if(error()){<p role="alert">{{error()}}</p>}
  <p class="integration-help">{{ t('Use declared variable names. Values are typed constants; no code is executed.','استخدم أسماء المتغيرات المعرّفة. القيم ثابتة ومحددة النوع ولا يتم تنفيذ تعليمات برمجية.') }}</p>`})
export class WorkflowVariableEditorComponent implements OnChanges {
  @Input() value='{}';@Input() readonly=false;@Input() variables:{variableKey:string}[]=[];
  @Output() valueChange=new EventEmitter<string>();
  readonly locale=inject(LocaleService);readonly error=signal('');readonly standalone={standalone:true};
  rows:{key:string;type:string;value:string}[]=[];
  t(en:string,ar:string){return this.locale.isRtl()?ar:en;}
  ngOnChanges(){try{const parsed=JSON.parse(this.value||'{}');this.rows=Object.entries(parsed).map(([key,value])=>({key,type:value===null?'Null':typeof value==='boolean'?'Boolean':typeof value==='number'?'Number':typeof value==='string'?'String':'Json',value:typeof value==='string'?value:JSON.stringify(value)}));}catch{this.rows=[];}}
  apply(){if(this.readonly)return;try{
    const values:Record<string,unknown>={};
    for(const row of this.rows){
      if(!/^[A-Za-z_][A-Za-z0-9_]*$/.test(row.key)||Object.hasOwn(values,row.key))throw new Error(this.t('Use unique, valid variable names.','استخدم أسماء متغيرات صحيحة وغير مكررة.'));
      const value=row.type==='Null'?null:row.type==='String'?row.value:JSON.parse(row.value);
      if(row.type==='Number'&&typeof value!=='number'||row.type==='Boolean'&&typeof value!=='boolean')throw new Error(this.t('The value does not match its selected type.','القيمة لا تطابق النوع المحدد.'));
      values[row.key]=value;
    }
    this.error.set('');this.valueChange.emit(JSON.stringify(values));
  }catch(e){this.error.set((e as Error).message);}}
}
