import { TestBed } from '@angular/core/testing';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowVariableEditorComponent } from './workflow-variable-editor.component';
describe('Typed workflow assignments',()=>{
  let editor:WorkflowVariableEditorComponent;
  beforeEach(()=>{TestBed.configureTestingModule({providers:[{provide:LocaleService,useValue:{isRtl:()=>false}}]});editor=TestBed.runInInjectionContext(()=>new WorkflowVariableEditorComponent());});
  it('keeps numeric-looking strings separate from numbers and booleans',()=>{
    editor.value='{"name":"150","amount":150,"approved":false,"data":{"id":42}}';editor.ngOnChanges();
    let saved='';editor.valueChange.subscribe(value=>saved=value);editor.apply();expect(JSON.parse(saved)).toEqual(JSON.parse(editor.value));
  });
  it('rejects duplicate names and incorrectly typed values',()=>{
    editor.rows=[{key:'amount',type:'Number',value:'"150"'}];editor.apply();expect(editor.error()).toContain('type');
    editor.rows=[{key:'amount',type:'Number',value:'150'},{key:'amount',type:'Number',value:'200'}];editor.apply();expect(editor.error()).toContain('unique');
  });
  it('cannot change a published configuration',()=>{editor.readonly=true;const emit=spyOn(editor.valueChange,'emit');editor.apply();expect(emit).not.toHaveBeenCalled();});
});
