import { TestBed } from '@angular/core/testing';
import { of, Subject } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowWorkspaceService, WorkspaceSettings, WorkspaceWorkflow } from './workflow-workspace.service';
import { WorkflowBusinessActivityComponent } from './workflow-business-activity.component';
import { matchingChild, sameWorkflowScope } from './workflow-child-match';
import { WorkflowTaskPaletteComponent } from '../designer/workflow-task-palette.component';

const parent: WorkspaceSettings = { kind:'Main',schemaVersion:2,organizationScopes:[{level:'Cbu',code:'RCBU',clusterCode:'CC',cbuCode:'RCBU'}] };
const child = (id:string, settings:Partial<WorkspaceSettings>={}):WorkspaceWorkflow => ({id,name:id,versionId:id,versionNumber:1,definitionKey:id,
  workspaceJson:JSON.stringify({...parent,kind:'Child',taskTypeId:'T1',...settings})});

describe('Exact workflow child matching',()=>{
  it('excludes ancestor, descendant, additional scope and wrong Task Type',()=>{
    expect(matchingChild(parent,child('exact').workspaceJson,'T1')).toBeTrue();
    for(const settings of [
      {organizationScopes:[{level:'Cluster',code:'CC',clusterCode:'CC'}]},
      {organizationScopes:[{level:'Branch',code:'B1',clusterCode:'CC',cbuCode:'RCBU'}]},
      {organizationScopes:[...parent.organizationScopes!,{level:'Cluster',code:'EC',clusterCode:'EC'}]},
      {taskTypeId:'T2'}, {kind:'Main'}, {organizationScopes:[]},
    ] as Partial<WorkspaceSettings>[]) expect(matchingChild(parent,child('other',settings).workspaceJson,'T1')).toBeFalse();
    expect(matchingChild(parent,'broken','T1')).toBeFalse();
  });
  it('compares complete sets without ordering or duplicate sensitivity',()=>{
    const other:WorkspaceSettings={...parent,organizationScopes:[...parent.organizationScopes!,{level:'Cluster',code:'EC',clusterCode:'EC'}]};
    expect(sameWorkflowScope(other,{...other,organizationScopes:[...other.organizationScopes!].reverse()})).toBeTrue();
    expect(sameWorkflowScope(parent,{...parent,organizationScopes:[...parent.organizationScopes!,...parent.organizationScopes!]})).toBeTrue();
    expect(sameWorkflowScope(parent,other)).toBeFalse();
  });
  beforeEach(()=>TestBed.configureTestingModule({providers:[{provide:LocaleService,useValue:{locale:()=> 'en'}},
    {provide:WorkflowWorkspaceService,useValue:{references:()=>of([]),children:()=>of([])}}]}));
  it('refreshes with unsaved parent scope and retains an invalid selection for review',()=>{
    const c=TestBed.runInInjectionContext(()=>new WorkflowBusinessActivityComponent());
    c.parentSettings=parent;c.config={taskTypeId:'T1',versionId:'exact',events:[]};c.children.set([child('exact'),child('broad',{organizationScopes:[{level:'Cluster',code:'CC',clusterCode:'CC'}]})]);
    expect(c.eligibleChildren().map(x=>x.id)).toEqual(['exact']);
    c.parentSettings={...parent,organizationScopes:[{level:'Cluster',code:'CC',clusterCode:'CC'}]};
    expect(c.eligibleChildren().map(x=>x.id)).toEqual(['broad']);expect(c.selectedChildMatches()).toBeFalse();
    expect(c.config.versionId).toBe('exact');c.taskType('T2');expect(c.eligibleChildren()).toEqual([]);expect(c.config.events).toEqual([]);
    c.child('broad');expect(c.config.versionId).toBe('exact');
  });
  it('ignores an obsolete child lookup response after selecting another node',()=>{
    const first=new Subject<WorkspaceWorkflow[]>(),second=new Subject<WorkspaceWorkflow[]>();
    TestBed.overrideProvider(WorkflowWorkspaceService,{useValue:{children:(id:string)=>id==='one'?first:second}});
    const c=TestBed.runInInjectionContext(()=>new WorkflowBusinessActivityComponent());
    c.config={versionId:'one'};c.loadChildren();c.config={versionId:'two'};c.loadChildren();
    second.next([child('two')]);first.next([child('one')]);expect(c.children().map(c=>c.id)).toEqual(['two']);
  });
  it('preserves published child selection when viewing a legacy version',()=>{
    const c=TestBed.runInInjectionContext(()=>new WorkflowBusinessActivityComponent());
    c.parentSettings=parent;c.readonly=true;c.config={versionId:'old'};c.children.set([child('old',{organizationScopes:undefined})]);
    expect(c.selectedChildMatches()).toBeTrue();
  });
});

describe('Task Type palette',()=>{
  it('searches both languages and writes the task reference into the drag payload',()=>{
    TestBed.configureTestingModule({providers:[{provide:LocaleService,useValue:{locale:()=> 'ar'}},{provide:WorkflowWorkspaceService,useValue:{references:()=>of([])}}]});
    const c=TestBed.runInInjectionContext(()=>new WorkflowTaskPaletteComponent());
    const task={id:'T1',code:'SURVEY',nameEn:'Survey',nameAr:'مسح'};c.items.set([task]);
    for(const query of ['survey','مسح']) {c.query=query;expect(c.filtered()).toEqual([task]);}
    const transfer=new DataTransfer();c.drag(new DragEvent('dragstart',{dataTransfer:transfer}),task);
    expect(transfer.getData('nodeType')).toBe('MainActivity');expect(JSON.parse(transfer.getData('workflowTaskType')).id).toBe('T1');
    c.readonly=true;const blocked=new DataTransfer();c.drag(new DragEvent('dragstart',{dataTransfer:blocked}),task);expect(blocked.getData('workflowTaskType')).toBe('');
  });
});
