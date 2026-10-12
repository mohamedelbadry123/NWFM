import { beforeEach, describe, expect, it, vi, type Mock } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowChildPreviewComponent } from './workflow-child-preview.component';
import { WorkflowVersionsService } from '../workflow-versions.service';
import { WorkflowWorkspaceService } from '../workspace/workflow-workspace.service';
import { layoutWorkflowGraph } from './workflow-canvas-layout';

describe('Independent child workflow preview',()=>{
  const child={id:'child',name:'Independent child',versionId:'v1',versionNumber:1,workspaceJson:'{}'};
  const graph={activities:[{id:'a',nodeKey:'start',name:'Start',activityType:'Start'},{id:'b',nodeKey:'review',name:'Review',activityType:'UserTask',configurationJson:'{"taskTypeId":"child-type","formDefinitionId":"child-form"}'}],transitions:[{id:'edge',fromActivityDefinitionId:'a',toActivityDefinitionId:'b'}]};
  let versions:Record<string, Mock>;
  beforeEach(()=>{
    versions={'getById': vi.fn()};versions.getById.mockReturnValue(of(graph as any));
    TestBed.configureTestingModule({imports:[WorkflowChildPreviewComponent],providers:[provideRouter([]),{provide:LocaleService,useValue:{isRtl:()=>false}}, {provide:WorkflowVersionsService,useValue:versions},{provide:WorkflowWorkspaceService,useValue:{children:()=>of([child,{...child,versionId:'v2',versionNumber:2}])}}]});
  });
  it('renders the exact linked version without changing child data and opens its own designer',()=>{
    const before=JSON.stringify(graph);const fixture=TestBed.createComponent(WorkflowChildPreviewComponent);fixture.componentRef.setInput('versionId','v1');fixture.detectChanges();
    expect(versions.getById).toHaveBeenCalledOnceWith('child','v1');expect(fixture.componentInstance.nodes().length).toBe(2);expect(fixture.componentInstance.edges().length).toBe(1);
    const el=fixture.nativeElement as HTMLElement;expect(el.textContent).toContain('Review');expect(el.querySelector('input,select,textarea')).toBeNull();expect(el.querySelector('a')?.getAttribute('href')).toContain('/child/versions/v1/designer');expect(el.querySelector('a')?.target).toBe('_blank');expect(JSON.stringify(graph)).toBe(before);
  });
  it('ignores a late version response after the link changes',()=>{
    const slow=new Subject<any>();versions.getById.mockReturnValueOnce(slow).mockReturnValueOnce(of({...graph,activities:[{id:'new',name:'New',activityType:'End'}]} as any));
    const c=TestBed.runInInjectionContext(()=>new WorkflowChildPreviewComponent());c.versionId='v1';c.ngOnChanges();c.versionId='v2';c.ngOnChanges();slow.next(graph);expect(c.nodes().map(n=>n.name)).toEqual(['New']);expect(c.child()?.versionId).toBe('v2');
  });
  it('reports loading failures and supports retry',()=>{
    versions.getById.mockReturnValue(throwError(()=>new Error('offline')));const c=TestBed.runInInjectionContext(()=>new WorkflowChildPreviewComponent());c.versionId='v1';c.ngOnChanges();expect(c.error()).toBeTruthy();versions.getById.mockReturnValue(of(graph as any));c.load();expect(c.error()).toBe('');expect(c.nodes().length).toBe(2);
  });
  it('reserves space for an expanded main activity in the parent layout',()=>{
    const positions=layoutWorkflowGraph([{id:'main',type:'MainActivity',width:560,height:440},{id:'next',type:'UserTask'}],[{fromNodeId:'main',toNodeId:'next'}]);expect(positions['next'].y).toBeGreaterThan(positions['main'].y+440);
  });
});
