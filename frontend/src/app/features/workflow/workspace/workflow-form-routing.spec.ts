import { beforeEach, describe, expect, it, vi, type Mock } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { of, Subject } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowFormActionsComponent } from './workflow-form-actions.component';
import { WorkflowRoutingGroupComponent } from './workflow-routing-group.component';
import { WorkflowActivityFormsComponent } from './workflow-activity-forms.component';
import { WorkflowActivityFormsService, ActivityFormPreview } from './workflow-activity-forms.service';
import { ActivityFormStep, activityFormSteps, routingFields, routingOperators } from './workflow-form-routing.types';

describe('Per-form routing designer',()=>{
  const steps:ActivityFormStep[]=[{id:'one',formId:'A',versionNo:1,name:'First',rules:[],otherwise:{kind:'Next'}},{id:'two',formId:'B',versionNo:2,name:'Second',rules:[],otherwise:{kind:'Next'}}];
  let api:Record<string, Mock>;
  beforeEach(()=>{
    api={'previewForTaskType': vi.fn(),'listForTaskType': vi.fn()};
    api.previewForTaskType.mockReturnValue(of({schemaJson:JSON.stringify({elements:[{type:'section',elements:[{type:'date',data_name:'day',label_en:'Day'}]},{type:'single_choice',data_name:'result',choices:[{value:'A',label_en:'Approve'}]}]})} as ActivityFormPreview));
    api.listForTaskType.mockReturnValue(of({items:[],totalCount:0,usableCount:0,pageNumber:1,pageSize:20}));
    TestBed.configureTestingModule({imports:[WorkflowFormActionsComponent,WorkflowActivityFormsComponent],providers:[{provide:WorkflowActivityFormsService,useValue:api},{provide:LocaleService,useValue:{isRtl:()=>false,locale:()=> 'en'}}]});
  });
  const create=()=>{const c=TestBed.runInInjectionContext(()=>new WorkflowFormActionsComponent());c.configuration=JSON.stringify({taskTypeId:'T',formSteps:steps});c.ngOnChanges();return c;};

  it('loads only the selected pinned form and keeps rules scoped to that form',()=>{
    const c=create();const saved=vi.fn();c.configurationChange.subscribe(saved);
    expect(api.previewForTaskType).toHaveBeenCalledWith('T','A',1);expect(c.fields().map(f=>f.dataName)).toEqual(['day','result']);
    c.addRule();c.selectedStep()!.rules[0].when.conditions.push({field:'day',operator:'between',value:'2026-10-01',to:'2026-10-31'});c.save();
    c.selectStep('two');c.addRule();c.selectedStep()!.rules[0].destination={kind:'Form',target:'one'};c.save();
    const config=JSON.parse(saved.mock.lastCall![0]);expect(config.formSteps[0].rules[0].when.conditions[0].operator).toBe('between');expect(config.formSteps[1].rules[0].destination.target).toBe('one');
    expect(config.formSteps[0].rules[0].id).not.toBe(config.formSteps[1].rules[0].id);
  });
  it('reorders and duplicates rules with stable condition groups and new rule IDs',()=>{
    const c=create();c.addRule();const first=c.selectedStep()!.rules[0];first.when.groups.push({match:'Any',conditions:[{field:'result',operator:'equal',value:'A'}],groups:[]});c.duplicate(0);
    const copy=c.selectedStep()!.rules[1];expect(copy.id).not.toBe(first.id);expect(copy.when).toEqual(first.when);c.move(1,-1);expect(c.selectedStep()!.rules[0].id).toBe(copy.id);
  });
  it('ignores a late schema response for another form',()=>{
    const slow=new Subject<ActivityFormPreview>();api.previewForTaskType.mockReturnValueOnce(slow).mockReturnValueOnce(of({schemaJson:'{"elements":[]}'} as ActivityFormPreview));
    const c=create();c.selectStep('two');slow.next({schemaJson:'{"elements":[{"data_name":"old","type":"text"}]}'} as ActivityFormPreview);expect(c.fields()).toEqual([]);
  });
  it('renders the nested editor and both forms without changing the draft',()=>{
    const fixture=TestBed.createComponent(WorkflowFormActionsComponent);fixture.componentRef.setInput('configuration',JSON.stringify({taskTypeId:'T',formSteps:steps}));fixture.detectChanges();fixture.componentInstance.addRule();fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-workflow-routing-group')).toBeTruthy();expect(fixture.nativeElement.textContent).toContain('Otherwise go to');fixture.destroy();
  });
  it('offers earlier, later and current destinations for both rules and Otherwise',()=>{
    const fixture=TestBed.createComponent(WorkflowFormActionsComponent);
    fixture.componentRef.setInput('configuration',JSON.stringify({taskTypeId:'T',formSteps:steps}));
    fixture.componentRef.setInput('nodeKey','current');
    fixture.componentRef.setInput('nodes',[
      {nodeKey:'earlier',name:'Earlier activity',type:'UserTask'},
      {nodeKey:'current',name:'Current activity',type:'UserTask'},
      {nodeKey:'later',name:'Later activity',type:'MainActivity'}
    ]);
    fixture.detectChanges();
    const c=fixture.componentInstance;c.selectStep('two');c.addRule();
    c.selectedStep()!.rules[0].destination={kind:'Form',target:'one'};
    c.selectedStep()!.otherwise={kind:'Activity',target:'earlier'};
    fixture.detectChanges();
    const editors=fixture.nativeElement.querySelectorAll('app-workflow-form-route');
    expect(editors[0].textContent).toContain('First');
    expect(editors[0].textContent).toContain('Second');
    expect(editors[1].textContent).toContain('Earlier activity');
    expect(editors[1].textContent).toContain('Later activity');
    expect(editors[1].textContent).toContain('(Restart activity)');
    const saved=vi.fn();c.configurationChange.subscribe(saved);c.save();
    const config=JSON.parse(saved.mock.lastCall![0]);
    expect(config.formSteps[1].rules[0].destination).toEqual({kind:'Form',target:'one'});
    expect(config.formSteps[1].otherwise).toEqual({kind:'Activity',target:'earlier'});
    fixture.destroy();
  });
  it('keeps explicit form references when order changes or a target is removed',()=>{
    const c=TestBed.runInInjectionContext(()=>new WorkflowActivityFormsComponent());const configured=structuredClone(steps);configured[0].otherwise={kind:'Form',target:'two'};c.configuration=JSON.stringify({taskTypeId:'T',formSteps:configured});c.ngOnChanges();
    c.move(1,-1);expect(c.steps[1].otherwise.target).toBe('two');c.remove('two');expect(c.steps[0].otherwise.target).toBe('two');
  });
  it('preserves rules for review after changing the published version',()=>{
    const c=TestBed.runInInjectionContext(()=>new WorkflowActivityFormsComponent());c.configuration=JSON.stringify({taskTypeId:'T',formSteps:steps});c.ngOnChanges();c.version(c.steps[0],3);expect(c.steps[0].reviewRequired).toBe(true);
  });
  it('renders and edits scalar choices separately from multiple-choice comparisons',async()=>{
    const fixture=TestBed.createComponent(WorkflowRoutingGroupComponent);
    const group={match:'All',conditions:[{field:'result',operator:'equal',value:'A' as unknown}],groups:[]};
    fixture.componentRef.setInput('group',group);
    fixture.componentRef.setInput('fields',[{dataName:'result',type:'single_choice',labelEn:'Result',labelAr:'Result',choices:[{value:'A',labelEn:'Approve',labelAr:'A'},{value:'B',labelEn:'Return',labelAr:'B'}]}]);
    fixture.detectChanges();await fixture.whenStable();
    let select:HTMLSelectElement=fixture.nativeElement.querySelectorAll('select')[3];
    expect(select.selectedOptions[0].textContent).toBe('Approve');
    select.selectedIndex=2;select.dispatchEvent(new Event('change'));fixture.detectChanges();
    expect(group.conditions[0].value).toBe('B');
    fixture.componentInstance.changeOperator(group.conditions[0],'in');fixture.detectChanges();await fixture.whenStable();
    select=fixture.nativeElement.querySelectorAll('select')[3];
    expect(select.multiple).toBe(true);select.options[0].selected=true;select.options[1].selected=true;
    select.dispatchEvent(new Event('change'));fixture.detectChanges();
    expect(group.conditions[0].value).toEqual(['A','B']);
    fixture.componentInstance.changeOperator(group.conditions[0],'equal');fixture.detectChanges();await fixture.whenStable();
    select=fixture.nativeElement.querySelectorAll('select')[3];expect(select.multiple).toBe(false);
    select.selectedIndex=1;select.dispatchEvent(new Event('change'));fixture.detectChanges();
    expect(group.conditions[0].value).toBe('A');fixture.destroy();
  });
});

describe('Routing schema metadata',()=>{
  it('adapts legacy single-form bindings as one step',()=>expect(activityFormSteps({formDefinitionId:'old',formVersionNo:2})[0]).toEqual(expect.objectContaining({id:'legacy-form',formId:'old',versionNo:2})));
  it('exposes published options and nested fields but excludes sections and computed fields',()=>{
    const fields=routingFields({elements:[{type:'section',data_name:'section',elements:[{type:'single_choice',data_name:'answer',choices:[{value:'X',label_en:'Choice'}]}]}],computed_columns:[{key:'computed'}]});
    expect(fields.length).toBe(1);expect(fields[0].choices[0].value).toBe('X');
  });
  it('offers matching operators for date, choices and media',()=>{expect(routingOperators('date')).toContain('between');expect(routingOperators('multiple_choice')).toContain('contains_all');expect(routingOperators('photo')).toEqual(['is_empty','is_not_empty']);});
});
