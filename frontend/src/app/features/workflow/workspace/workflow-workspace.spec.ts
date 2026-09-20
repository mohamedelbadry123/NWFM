import { TestBed } from '@angular/core/testing';
import { Subject, of } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowGeographyComponent } from './workflow-geography.component';
import { WorkflowBusinessActivityComponent } from './workflow-business-activity.component';
import { ReferenceItem, WorkflowWorkspaceService } from './workflow-workspace.service';
import { buildNWFMXml } from '../designer/workflow-designer.component';

describe('Workflow workspace contracts', () => {
  it('clears a selected FA and ignores the old department response when department changes', () => {
    const previous = new Subject<ReferenceItem[]>(); const next = new Subject<ReferenceItem[]>();
    TestBed.configureTestingModule({providers:[{provide:LocaleService,useValue:{locale:()=> 'en'}},{provide:WorkflowWorkspaceService,useValue:{references:(_kind:string,parent?:string)=>parent==='old'?previous:next}}]});
    const component = TestBed.runInInjectionContext(()=>new WorkflowBusinessActivityComponent());
    component.config={departmentCode:'old',fieldActivityCode:'OLD-FA'}; component.loadFields(); component.department('new');
    expect(component.config.fieldActivityCode).toBe('');
    next.next([{id:'2',code:'NEW-FA',nameEn:'New',nameAr:'',parentCode:'new'}]);
    previous.next([{id:'1',code:'OLD-FA',nameEn:'Old',nameAr:'',parentCode:'old'}]);
    expect(component.fieldTypes().map(x=>x.code)).toEqual(['NEW-FA']);
  });
  it('preserves workspace metadata when the full designer saves XML', () => {
    const workspace = { kind: 'Main' as const, clusterCode:'CC',regionCode:'R',cityCode:'C' };
    const xml = new DOMParser().parseFromString(buildNWFMXml({nodes:[],edges:[],variables:[],workspace}), 'application/xml');
    expect(JSON.parse(xml.documentElement.getAttribute('workspaceJson')!)).toEqual(workspace);
  });
  it('resets descendants and ignores a stale response after changing cluster', () => {
    const previous = new Subject<ReferenceItem[]>(); const next = new Subject<ReferenceItem[]>();
    TestBed.configureTestingModule({providers:[{provide:LocaleService,useValue:{locale:()=> 'en'}},{provide:WorkflowWorkspaceService,useValue:{references:(kind:string,parent?:string)=>kind==='regions'?(parent==='old'?previous:next):of([])}}]});
    const component = TestBed.runInInjectionContext(()=>new WorkflowGeographyComponent());
    component.settings={kind:'Main',clusterCode:'old',regionCode:'old-region',cityCode:'old-city'};
    component.ngOnChanges(); component.chooseCluster('new');
    expect(component.settings.regionCode).toBe('');expect(component.settings.cityCode).toBe('');
    next.next([{id:'2',code:'new-region',nameEn:'New',nameAr:'',parentCode:'new'}]);
    previous.next([{id:'1',code:'old-region',nameEn:'Old',nameAr:'',parentCode:'old'}]);
    expect(component.regions().map(x=>x.code)).toEqual(['new-region']);
  });
});
