import { Component, EventEmitter, Input, OnChanges, Output, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthStore } from '@core/auth/auth.store';
import { FormsModule } from '@angular/forms';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowWorkspaceService, ReferenceItem, WorkspaceWorkflow } from './workflow-workspace.service';

interface BusinessEvent { id: string; name: string; trigger: string; kind: 'Http'|'Soap'|'Sms'|'Email'; required: boolean; configuration: Record<string,unknown> }
interface BusinessConfiguration { [key:string]:unknown; departmentCode?:string; fieldActivityCode?:string; definitionKey?:string; versionId?:string; rejectTargetNodeKey?:string; events?:BusinessEvent[] }

@Component({selector:'app-workflow-business-activity',standalone:true,imports:[FormsModule,RouterLink],template:`
<fieldset [disabled]="readonly" class="space-y-4 border rounded-lg p-3 my-3">
  <legend class="px-1 font-medium">{{ t('Business activity','النشاط') }}</legend>
  <label class="block text-sm">{{ t('Department','القسم') }}<select class="wf-input" [ngModel]="config.departmentCode || ''" [ngModelOptions]="standalone" (ngModelChange)="department($event)"><option value="">{{ t('Select department','اختر القسم') }}</option>@for(item of departments();track item.code){<option [value]="item.code">{{ label(item) }}</option>}</select></label>
  <label class="block text-sm">{{ t('Field Activity Type','نوع النشاط الميداني') }}<select class="wf-input" [disabled]="!config.departmentCode" [(ngModel)]="config.fieldActivityCode" [ngModelOptions]="standalone" (ngModelChange)="apply()"><option value="">{{ t('Select FA Type','اختر نوع النشاط') }}</option>@for(item of fieldTypes();track item.code){<option [value]="item.code">{{ label(item) }}</option>}</select></label>
  @if(config.departmentCode && !fieldTypes().length){<p role="status">{{ t('No active Field Activity Types exist for this department.','لا توجد أنواع أنشطة ميدانية نشطة لهذا القسم.') }}</p>@if(canLookups()){<a routerLink="/lookups" class="underline">{{ t('Manage lookups','إدارة البيانات المرجعية') }}</a>}}
  @if(error()){<p role="alert">{{ error() }}</p>}
  @if(main){
    <label class="block text-sm">{{ t('Child workflow','سير العمل الفرعي') }}<select class="wf-input" [ngModel]="config.versionId || ''" [ngModelOptions]="standalone" (ngModelChange)="child($event)"><option value="">{{ t('Select published child','اختر سير عمل فرعي منشور') }}</option>@for(item of children();track item.versionId){<option [value]="item.versionId">{{ item.name }} · v{{ item.versionNumber }}</option>}</select></label>
    <p class="text-xs opacity-70">{{ t('The child inherits geography. Its completion enables this activity’s approval. The SLA includes child time.','يرث سير العمل الفرعي الموقع. بعد اكتماله يتاح اعتماد النشاط الرئيسي. تشمل مدة الخدمة وقت التنفيذ الفرعي.') }}</p>
  }
</fieldset>
`})
export class WorkflowBusinessActivityComponent implements OnChanges {
  @Input() configuration='{}'; @Input() main=false; @Input() readonly=false; @Input() nodes:{nodeKey:string;name:string;type:string}[]=[]; @Input() variables:{variableKey:string}[]=[];
  @Output() configurationChange=new EventEmitter<string>();
  private readonly api=inject(WorkflowWorkspaceService);private readonly locale=inject(LocaleService);
  readonly departments=signal<ReferenceItem[]>([]);readonly fieldTypes=signal<ReferenceItem[]>([]);readonly children=signal<WorkspaceWorkflow[]>([]);readonly error=signal('');readonly standalone={standalone:true};
  private auth=inject(AuthStore); canLookups(){return this.auth.roles().includes('Administrator')||this.auth.hasAnyPermission('ManageLookups');}
  config:BusinessConfiguration={};private loaded=false;private loadedDepartment='';
  t(en:string,ar:string){return this.locale.locale()==='ar'?ar:en;}
  label(item:ReferenceItem){return this.locale.locale()==='ar'?item.nameAr:item.nameEn;}
  businessNodes(){return this.nodes.filter(n=>n.type==='UserTask'||n.type==='MainActivity');}
  ngOnChanges(){try{this.config=JSON.parse(this.configuration||'{}');}catch{this.config={};}if(!this.loaded){this.loaded=true;this.api.references('departments').subscribe({next:r=>this.departments.set(r),error:()=>this.error.set('Could not load departments.')});this.api.children().subscribe({next:r=>this.children.set(r),error:()=>this.error.set('Could not load child workflows.')});}if(this.loadedDepartment!==this.config.departmentCode){this.loadedDepartment=this.config.departmentCode||'';this.loadFields();}}
  loadFields(){const department=this.config.departmentCode;this.fieldTypes.set([]);if(department)this.api.references('field-activity-types',department).subscribe({next:r=>{if(this.config.departmentCode===department)this.fieldTypes.set(r);},error:()=>{if(this.config.departmentCode===department)this.error.set('Could not load Field Activity Types.');}});}
  department(code:string){this.config.departmentCode=code;this.config.fieldActivityCode='';this.loadFields();this.apply();}
  child(versionId:string){const child=this.children().find(c=>c.versionId===versionId);this.config.definitionKey=child?.definitionKey;this.config.versionId=child?.versionId;this.config['waitForCompletion']=true;this.apply();}
  apply(){if(!this.readonly)this.configurationChange.emit(JSON.stringify(this.config));}

}
