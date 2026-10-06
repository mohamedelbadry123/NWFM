import { Component, EventEmitter, Input, OnChanges, Output, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthStore } from '@core/auth/auth.store';
import { FormsModule } from '@angular/forms';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowWorkspaceService, ReferenceItem, WorkspaceWorkflow, WorkspaceSettings } from './workflow-workspace.service';
import { matchingChild } from './workflow-child-match';

interface BusinessEvent { id: string; name: string; trigger: string; kind: 'Http'|'Soap'|'Sms'|'Email'; required: boolean; configuration: Record<string,unknown> }
interface BusinessConfiguration { [key:string]:unknown; taskTypeId?:string; departmentCode?:string; fieldActivityCode?:string; definitionKey?:string; versionId?:string; rejectTargetNodeKey?:string; events?:BusinessEvent[] }

@Component({selector:'app-workflow-business-activity',standalone:true,imports:[FormsModule,RouterLink],template:`
<fieldset [disabled]="readonly" class="space-y-4 border rounded-lg p-3 my-3">
  <legend class="px-1 font-medium">{{ t('Business activity','النشاط') }}</legend>
  <label class="block text-sm">{{ t('Department','القسم') }}<select id="wf-activity-department" class="wf-input" [ngModel]="config.departmentCode || ''" [ngModelOptions]="standalone" (ngModelChange)="department($event)"><option value="">{{ t('Select department','اختر القسم') }}</option>@for(item of departments();track item.code){<option [value]="item.code">{{ label(item) }}</option>}</select></label>
  <label class="block text-sm">{{ t('Field Activity Type','نوع النشاط الميداني') }}<select id="wf-activity-field-type" class="wf-input" [disabled]="!config.departmentCode" [(ngModel)]="config.fieldActivityCode" [ngModelOptions]="standalone" (ngModelChange)="apply()"><option value="">{{ t('Select FA Type','اختر نوع النشاط') }}</option>@for(item of fieldTypes();track item.code){<option [value]="item.code">{{ label(item) }}</option>}</select></label>
  @if(config.departmentCode && !fieldTypes().length){<p role="status">{{ t('No active Field Activity Types exist for this department.','لا توجد أنواع أنشطة ميدانية نشطة لهذا القسم.') }}</p>@if(canLookups()){<a routerLink="/lookups" class="underline">{{ t('Manage lookups','إدارة البيانات المرجعية') }}</a>}}
  @if(error()){<p role="alert">{{ error() }}</p>}
  @if(main){
    @if(parentSettings?.kind === 'Main') {
      <label class="block text-sm">{{ t('Task Type','نوع المهمة') }}<select id="wf-activity-task-type" class="wf-input" [ngModel]="config.taskTypeId || ''" [ngModelOptions]="standalone" (ngModelChange)="taskType($event)">
        <option value="">{{ t('Select Task Type','اختر نوع المهمة') }}</option>
        @if(config.taskTypeId && !taskTypeAvailable()) { <option [value]="config.taskTypeId" disabled>{{ t('Saved Task Type is unavailable','نوع المهمة المحفوظ غير متاح') }}</option> }
        @for(item of taskTypes();track item.id){<option [value]="item.id">{{ label(item) }}</option>}
      </select></label>
      @if(typeError()) { <p role="alert">{{ t('Could not load Task Types.','تعذر تحميل أنواع المهام.') }}</p><button type="button" (click)="loadTaskTypes()">{{ t('Retry','إعادة المحاولة') }}</button> }
    }
    <label class="block text-sm">{{ t('Child workflow','سير العمل الفرعي') }}<select id="wf-activity-child" class="wf-input" [ngModel]="config.versionId || ''" [ngModelOptions]="standalone" (ngModelChange)="child($event)">
      <option value="">{{ t('Select published child','اختر سير عمل فرعي منشور') }}</option>
      @if(config.versionId && !selectedChildMatches()) { <option [value]="config.versionId" disabled>{{ t('Saved child requires review','سير العمل الفرعي المحفوظ يحتاج مراجعة') }}</option> }
      @for(item of eligibleChildren();track item.versionId){<option [value]="item.versionId">{{ localeName(item) }} · v{{ item.versionNumber }}</option>}
    </select></label>
    @if(childrenLoading()) { <p role="status">{{ t('Loading child workflows…','جارٍ تحميل سير العمل الفرعي…') }}</p> }
    @else if(childrenError()) { <p role="alert">{{ t('Could not load child workflows.','تعذر تحميل سير العمل الفرعي.') }}</p><button type="button" (click)="loadChildren()">{{ t('Retry','إعادة المحاولة') }}</button> }
    @else if(config.versionId && !selectedChildMatches()) { <p role="alert">{{ t('The selected child no longer matches this Task Type and organization scope. Select a matching child before publishing.','سير العمل الفرعي المحدد لا يطابق نوع المهمة والنطاق التنظيمي. اختر سير عمل مطابقاً قبل النشر.') }}</p> }
    @else if(!eligibleChildren().length) { <p role="status">{{ t('No published child workflow matches this Task Type and organization scope.','لا يوجد سير عمل فرعي منشور يطابق نوع المهمة والنطاق التنظيمي.') }}</p> }
    <p class="text-xs opacity-70">{{ t('The child inherits the organization location. Its completion enables this activity’s approval. The SLA includes child time.','يرث سير العمل الفرعي الموقع التنظيمي. بعد اكتماله يتاح اعتماد النشاط الرئيسي. تشمل مدة الخدمة وقت التنفيذ الفرعي.') }}</p>
    <p class="text-xs opacity-70" data-testid="main-activity-forms-note">{{ t('A main activity has no forms of its own. Forms are shown on the activities inside its child workflow.','لا توجد نماذج خاصة بالنشاط الرئيسي. تظهر النماذج في أنشطة سير العمل الفرعي التابع له.') }}</p>
    @if(selectedChild(); as child){<a class="text-sm underline" target="_blank" rel="noopener" [routerLink]="['/admin/workflow/definitions', child.id, 'versions', child.versionId, 'designer']">{{ t('Open child workflow in a new tab','فتح سير العمل الفرعي في علامة تبويب جديدة') }}</a>}
  }
</fieldset>
`})
export class WorkflowBusinessActivityComponent implements OnChanges {
  @Input() parentSettings: WorkspaceSettings | null = null;
  @Input() configuration='{}'; @Input() main=false; @Input() readonly=false; @Input() nodes:{nodeKey:string;name:string;type:string}[]=[]; @Input() variables:{variableKey:string}[]=[];
  @Output() configurationChange=new EventEmitter<string>();
  private readonly api=inject(WorkflowWorkspaceService);private readonly locale=inject(LocaleService);
  readonly departments=signal<ReferenceItem[]>([]);readonly fieldTypes=signal<ReferenceItem[]>([]);readonly children=signal<WorkspaceWorkflow[]>([]);readonly error=signal('');readonly standalone={standalone:true};
  private auth=inject(AuthStore); canLookups(){return this.auth.roles().includes('Administrator')||this.auth.hasAnyPermission('ManageLookups');}
  readonly taskTypes=signal<ReferenceItem[]>([]);readonly typeError=signal(false);
  readonly childrenLoading=signal(false);readonly childrenError=signal(false);
  private childRequest=0;private loadedChild: string | undefined;private childrenLoaded=false;
  config:BusinessConfiguration={};private loaded=false;private loadedDepartment='';
  t(en:string,ar:string){return this.locale.locale()==='ar'?ar:en;}
  label(item:ReferenceItem){return this.locale.locale()==='ar'?(item.nameAr || item.nameEn):item.nameEn;}
  localeName(item:WorkspaceWorkflow){return this.t(item.name,item.nameAr || item.name);}
  taskTypeAvailable(){return this.taskTypes().some(t=>t.id===this.config.taskTypeId);}
  eligibleChildren(){return this.parentSettings?.kind !== 'Main' || this.readonly ? this.children()
    : this.children().filter(child=>matchingChild(this.parentSettings,child.workspaceJson,this.config.taskTypeId));}
  selectedChildMatches(){return this.eligibleChildren().some(c=>c.versionId===this.config.versionId);}
  taskType(id:string){if(this.readonly)return;this.config.taskTypeId=id;this.apply();}
  loadTaskTypes(){this.typeError.set(false);this.api.references('task-types').subscribe({next:r=>this.taskTypes.set(r),error:()=>this.typeError.set(true)});}
  loadChildren(){const request=++this.childRequest;this.childrenLoading.set(true);this.childrenError.set(false);this.loadedChild=this.config.versionId;this.childrenLoaded=true;
    this.api.children(this.config.versionId).subscribe({next:r=>{if(request!==this.childRequest)return;this.children.set(r);this.childrenLoading.set(false);},error:()=>{if(request!==this.childRequest)return;this.childrenLoading.set(false);this.childrenError.set(true);}});}
  selectedChild(){return this.children().find(c=>c.versionId===this.config.versionId);}
  businessNodes(){return this.nodes.filter(n=>n.type==='UserTask'||n.type==='MainActivity');}
  ngOnChanges(){try{this.config=JSON.parse(this.configuration||'{}');}catch{this.config={};}if(!this.loaded){this.loaded=true;this.api.references('departments').subscribe({next:r=>this.departments.set(r),error:()=>this.error.set('Could not load departments.')});this.loadTaskTypes();}if(!this.childrenLoaded || this.loadedChild!==this.config.versionId)this.loadChildren();if(this.loadedDepartment!==this.config.departmentCode){this.loadedDepartment=this.config.departmentCode||'';this.loadFields();}}
  loadFields(){const department=this.config.departmentCode;this.fieldTypes.set([]);if(department)this.api.references('field-activity-types',department).subscribe({next:r=>{if(this.config.departmentCode===department)this.fieldTypes.set(r);},error:()=>{if(this.config.departmentCode===department)this.error.set('Could not load Field Activity Types.');}});}
  department(code:string){this.config.departmentCode=code;this.config.fieldActivityCode='';this.loadFields();this.apply();}
  child(versionId:string){if(this.readonly)return;const child=this.eligibleChildren().find(c=>c.versionId===versionId);if(versionId && !child)return;this.config.definitionKey=child?.definitionKey;this.config.versionId=child?.versionId;this.config['waitForCompletion']=true;this.apply();}
  apply(){if(!this.readonly)this.configurationChange.emit(JSON.stringify(this.config));}

}
