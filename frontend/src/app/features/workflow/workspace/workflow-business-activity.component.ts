import { Component, EventEmitter, Input, OnChanges, Output, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowIntegrationEditorComponent } from '../integrations/workflow-integration-editor.component';
import { WorkflowWorkspaceService, ReferenceItem, WorkspaceWorkflow } from './workflow-workspace.service';

interface BusinessEvent { id: string; name: string; trigger: string; kind: 'Http'|'Soap'|'Sms'|'Email'; required: boolean; configuration: Record<string,unknown> }
interface BusinessConfiguration { [key:string]:unknown; departmentCode?:string; fieldActivityCode?:string; definitionKey?:string; versionId?:string; rejectTargetNodeKey?:string; events?:BusinessEvent[] }

@Component({selector:'app-workflow-business-activity',standalone:true,imports:[FormsModule,WorkflowIntegrationEditorComponent],template:`
<fieldset [disabled]="readonly" class="space-y-4 border rounded-lg p-3 my-3">
  <legend class="px-1 font-medium">{{ t('Business activity','النشاط') }}</legend>
  <label class="block text-sm">{{ t('Department','القسم') }}<select class="wf-input" [ngModel]="config.departmentCode || ''" [ngModelOptions]="standalone" (ngModelChange)="department($event)"><option value="">{{ t('Select department','اختر القسم') }}</option>@for(item of departments();track item.code){<option [value]="item.code">{{ label(item) }}</option>}</select></label>
  <label class="block text-sm">{{ t('Field Activity Type','نوع النشاط الميداني') }}<select class="wf-input" [disabled]="!config.departmentCode" [(ngModel)]="config.fieldActivityCode" [ngModelOptions]="standalone" (ngModelChange)="apply()"><option value="">{{ t('Select FA Type','اختر نوع النشاط') }}</option>@for(item of fieldTypes();track item.code){<option [value]="item.code">{{ label(item) }}</option>}</select></label>
  @if(main){
    <label class="block text-sm">{{ t('Child workflow','سير العمل الفرعي') }}<select class="wf-input" [ngModel]="config.versionId || ''" [ngModelOptions]="standalone" (ngModelChange)="child($event)"><option value="">{{ t('Select published child','اختر سير عمل فرعي منشور') }}</option>@for(item of children();track item.versionId){<option [value]="item.versionId">{{ item.name }} · v{{ item.versionNumber }}</option>}</select></label>
    <p class="text-xs opacity-70">{{ t('The child inherits geography. Its completion enables this activity’s approval. The SLA includes child time.','يرث سير العمل الفرعي الموقع. بعد اكتماله يتاح اعتماد النشاط الرئيسي. تشمل مدة الخدمة وقت التنفيذ الفرعي.') }}</p>
  }
  <label class="block text-sm">{{ t('Reject: return for rework','عند الرفض: العودة لإعادة العمل') }}<select class="wf-input" [(ngModel)]="config.rejectTargetNodeKey" [ngModelOptions]="standalone" (ngModelChange)="apply()"><option value="">{{ t('Select destination','اختر الوجهة') }}</option>@for(node of businessNodes();track node.nodeKey){<option [value]="node.nodeKey">{{ node.name }}</option>}</select></label>
</fieldset>
<section class="space-y-3 border rounded-lg p-3 my-3"><h3 class="font-medium">{{ t('Activity events','أحداث النشاط') }}</h3>
  @for(event of config.events || [];track event.id){
    <details class="border rounded p-2" open><summary class="font-medium cursor-pointer">{{ event.name || t('New event','حدث جديد') }}</summary><fieldset [disabled]="readonly" class="space-y-3 mt-3">
      <label class="block text-sm">{{ t('Event name','اسم الحدث') }}<input class="wf-input" [(ngModel)]="event.name" [ngModelOptions]="standalone" (ngModelChange)="apply()"></label>
      <label class="block text-sm">{{ t('When','عند') }}<select class="wf-input" [(ngModel)]="event.trigger" [ngModelOptions]="standalone" (ngModelChange)="apply()">@for(trigger of triggers;track trigger.value){<option [value]="trigger.value">{{ t(trigger.en,trigger.ar) }}</option>}</select></label>
      <label class="block text-sm">{{ t('Action','الإجراء') }}<select class="wf-input" [(ngModel)]="event.kind" [ngModelOptions]="standalone" (ngModelChange)="eventKind(event)"><option value="Http">REST API</option><option value="Soap">SOAP API</option><option value="Sms">SMS</option><option value="Email">{{ t('Email','بريد إلكتروني') }}</option></select></label>
      <label class="flex gap-2 text-sm"><input type="checkbox" [(ngModel)]="event.required" [ngModelOptions]="standalone" (ngModelChange)="apply()">{{ t('Must succeed before advancing','يجب نجاحه قبل الانتقال') }}</label>
      <app-workflow-integration-editor [kind]="event.kind === 'Email' ? 'NotificationTask' : 'ServiceTask'" [protocol]="event.kind === 'Soap' ? 'Soap' : event.kind === 'Sms' ? 'Sms' : 'Rest'" [activityEvent]="true" [configuration]="eventJson(event)" [readonly]="readonly" [variables]="variables" (configurationChange)="eventConfiguration(event,$event)" />
      <button type="button" class="text-red-600" (click)="removeEvent(event.id)">{{ t('Remove event','حذف الحدث') }}</button>
    </fieldset></details>
  }
  @if(!readonly){<button type="button" class="wf-btn-secondary" (click)="addEvent()">{{ t('Add event','إضافة حدث') }}</button>}
  @if(error()){<p role="alert" class="text-red-600">{{ error() }}</p>}
</section>`})
export class WorkflowBusinessActivityComponent implements OnChanges {
  @Input() configuration='{}'; @Input() main=false; @Input() readonly=false; @Input() nodes:{nodeKey:string;name:string;type:string}[]=[]; @Input() variables:{variableKey:string}[]=[];
  @Output() configurationChange=new EventEmitter<string>();
  private readonly api=inject(WorkflowWorkspaceService);private readonly locale=inject(LocaleService);
  readonly departments=signal<ReferenceItem[]>([]);readonly fieldTypes=signal<ReferenceItem[]>([]);readonly children=signal<WorkspaceWorkflow[]>([]);readonly error=signal('');readonly standalone={standalone:true};
  config:BusinessConfiguration={};private loaded=false;private loadedDepartment='';
  readonly triggers=[{value:'OnEnter',en:'Activity starts',ar:'بدء النشاط'},{value:'OnApprove',en:'User approves',ar:'اعتماد المستخدم'},{value:'OnReject',en:'User rejects',ar:'رفض المستخدم'},{value:'OnComment',en:'User adds a comment',ar:'إضافة تعليق'},{value:'OnComplete',en:'Activity completes',ar:'اكتمال النشاط'},{value:'OnFailure',en:'Activity fails',ar:'فشل النشاط'},{value:'OnSlaBreach',en:'SLA is overdue',ar:'تجاوز مدة الخدمة'}];
  t(en:string,ar:string){return this.locale.locale()==='ar'?ar:en;}
  label(item:ReferenceItem){return this.locale.locale()==='ar'?item.nameAr:item.nameEn;}
  businessNodes(){return this.nodes.filter(n=>n.type==='UserTask'||n.type==='MainActivity');}
  ngOnChanges(){try{this.config=JSON.parse(this.configuration||'{}');}catch{this.config={};}if(!this.loaded){this.loaded=true;this.api.references('departments').subscribe({next:r=>this.departments.set(r),error:()=>this.error.set('Could not load departments.')});this.api.children().subscribe({next:r=>this.children.set(r),error:()=>this.error.set('Could not load child workflows.')});}if(this.loadedDepartment!==this.config.departmentCode){this.loadedDepartment=this.config.departmentCode||'';this.loadFields();}}
  loadFields(){const department=this.config.departmentCode;this.fieldTypes.set([]);if(department)this.api.references('field-activity-types',department).subscribe({next:r=>{if(this.config.departmentCode===department)this.fieldTypes.set(r);},error:()=>{if(this.config.departmentCode===department)this.error.set('Could not load Field Activity Types.');}});}
  department(code:string){this.config.departmentCode=code;this.config.fieldActivityCode='';this.loadFields();this.apply();}
  child(versionId:string){const child=this.children().find(c=>c.versionId===versionId);this.config.definitionKey=child?.definitionKey;this.config.versionId=child?.versionId;this.config['waitForCompletion']=true;this.apply();}
  apply(){if(!this.readonly)this.configurationChange.emit(JSON.stringify(this.config));}
  addEvent(){(this.config.events??=[]).push({id:crypto.randomUUID(),name:'',trigger:'OnApprove',kind:'Http',required:true,configuration:{protocol:'Rest',method:'POST',path:'/',contentType:'application/json',body:'{}',maxAttempts:3,retryDelaySeconds:10,timeoutSeconds:30}});this.apply();}
  removeEvent(id:string){this.config.events=this.config.events?.filter(e=>e.id!==id);this.apply();}
  eventKind(event:BusinessEvent){event.required=event.kind==='Http'||event.kind==='Soap';event.configuration={...event.configuration,protocol:event.kind==='Soap'?'Soap':event.kind==='Sms'?'Sms':'Rest'};if(event.kind==='Email')event.configuration={channels:'Email',failurePolicy:'Retry',maxAttempts:3,retryDelaySeconds:30,subject:'Workflow notification',body:''};this.apply();}
  eventJson(event:BusinessEvent){return JSON.stringify(event.configuration);}
  eventConfiguration(event:BusinessEvent,json:string){event.configuration=JSON.parse(json);this.apply();}
}
