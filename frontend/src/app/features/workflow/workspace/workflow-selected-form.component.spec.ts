import { beforeEach, describe, expect, it, vi, type Mock } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowSelectedFormComponent } from './workflow-selected-form.component';
import { TranslateModule } from '@ngx-translate/core';
import { provideDynamicForms } from '@shared/components/dynamic-form/provide-dynamic-forms';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

describe('Selected workflow form',()=>{
  beforeEach(()=>TestBed.configureTestingModule({imports:[WorkflowSelectedFormComponent,TranslateModule.forRoot()],providers:[provideDynamicForms(),provideNoopAnimations(),{provide:LocaleService,useValue:{locale:()=> 'en',isRtl:()=>false}}]}));
  it('starts fresh answers when returning to the same form with a new visit',()=>{
    const c=TestBed.runInInjectionContext(()=>new WorkflowSelectedFormComponent());c.workItemId='work';c.selectedForm={formId:'f',versionNo:1,visitId:'first',schemaJson:'{"elements":[]}'};c.answers={answer:'previous'};c.ngOnChanges();
    const old=c.initialAnswers;c.selectedForm={...c.selectedForm,visitId:'second'};c.answers={};c.ngOnChanges();expect(c.initialAnswers).toEqual({});expect(c.initialAnswers).not.toBe(old);
  });
  it('renders a real required form, retains typed answers on polling and emits a submission payload',async()=>{
    const fixture=TestBed.createComponent(WorkflowSelectedFormComponent);
    const selected={formId:'form',versionNo:2,schemaJson:JSON.stringify({name_en:'Activity form',elements:[{type:'text',data_name:'notes',label_en:'Notes',required:true}]})};
    fixture.componentRef.setInput('workItemId','work');fixture.componentRef.setInput('selectedForm',selected);fixture.detectChanges();await fixture.whenStable();fixture.detectChanges();
    expect(fixture.componentInstance.validate()).toBe(false);
    const input=fixture.nativeElement.querySelector('input') as HTMLInputElement;
    expect(input).not.toBeNull();
    const changed=vi.fn();fixture.componentInstance.answersChange.subscribe(changed);
    input.value='Completed inspection';input.dispatchEvent(new Event('input',{bubbles:true}));fixture.detectChanges();await fixture.whenStable();
    expect(fixture.componentInstance.validate()).toBe(true);expect(fixture.componentInstance.payload()['notes']).toBe('Completed inspection');expect(changed).toHaveBeenCalled();
    fixture.componentRef.setInput('selectedForm',{...selected});fixture.componentRef.setInput('answers',{notes:'Completed inspection'});fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('input')).toBe(input);expect(input.value).toBe('Completed inspection');
  });
  it('keeps renderer inputs stable while task data polls and answers change',()=>{
    const c=TestBed.runInInjectionContext(()=>new WorkflowSelectedFormComponent());c.workItemId='work';c.selectedForm={formId:'f',versionNo:1,schemaJson:'{"elements":[]}'};c.answers={a:'original'};c.ngOnChanges();
    const schema=c.definition,answers=c.initialAnswers;c.answers={a:'typed'};c.selectedForm={...c.selectedForm};c.ngOnChanges();expect(c.definition).toBe(schema);expect(c.initialAnswers).toBe(answers);
    c.readonly=true;c.ngOnChanges();expect(c.initialAnswers).toEqual({a:'typed'});
  });
  it('does not allow completion when the pinned schema is missing or malformed',()=>{
    const c=TestBed.runInInjectionContext(()=>new WorkflowSelectedFormComponent());c.selectedForm={formId:'f',versionNo:1,schemaJson:'broken'};c.ngOnChanges();expect(c.definition).toBeNull();expect(c.validate()).toBe(false);
  });
});
