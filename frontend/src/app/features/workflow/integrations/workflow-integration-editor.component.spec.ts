import { TestBed } from '@angular/core/testing';
import { SimpleChange, signal } from '@angular/core';
import { of } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowIntegrationsService } from './workflow-integrations.service';
import { WorkflowIntegrationEditorComponent } from './workflow-integration-editor.component';

describe('Workflow integration configuration', () => {
  let editor: WorkflowIntegrationEditorComponent;
  const api = { connections: () => of([]), test: jasmine.createSpy('test').and.returnValue(of({success:true,statusCode:201,body:'{"id":42}',headers:{'x-result':'ok'}})) };
  beforeEach(() => {
    api.test.calls.reset();
    TestBed.configureTestingModule({ providers: [{provide:WorkflowIntegrationsService,useValue:api},{provide:LocaleService,useValue:{locale:signal('en')}}] });
    editor=TestBed.runInInjectionContext(()=>new WorkflowIntegrationEditorComponent());
    editor.configuration='{}'; editor.ngOnChanges();
  });
  it('preserves extension fields when editing an existing connector', () => {
    editor.configuration=JSON.stringify({connectionId:'connection',method:'POST',custom:{keep:true},headers:{Accept:'application/json'}});editor.ngOnChanges();
    editor.form.path='/orders';const saved=editor.config();expect(saved['custom']).toEqual({keep:true});expect(saved['headers']).toEqual({Accept:'application/json'});expect(saved['method']).toBe('POST');
  });
  it('preserves unsaved request fields when canvas source lists refresh', () => {
    editor.form.path='/edited';editor.form.body='{"name":"draft"}';
    editor.rows.outputMappings.push({key:'externalId',value:'body.id'});
    editor.ngOnChanges({nodes:new SimpleChange([], [{nodeKey:'review',name:'Review'}],false)});
    expect(editor.form.path).toBe('/edited');expect(editor.form.body).toBe('{"name":"draft"}');
    expect(editor.rows.outputMappings).toEqual([{key:'externalId',value:'body.id'}]);
  });
  it('does not contact the service while applying or previewing configuration', () => {
    editor.form.connectionId='connection';editor.apply();editor.preview();expect(api.test).not.toHaveBeenCalled();
  });
  it('rejects a missing connection and duplicate mappings', () => {
    editor.apply();expect(editor.error()).toContain('connection');editor.form.connectionId='connection';editor.rows.outputMappings=[{key:'id',value:'body.id'},{key:'id',value:'body.code'}];editor.apply();expect(editor.error()).toContain('Duplicate');
  });
  it('keeps callback correlation and payload mappings through save and reload', () => {
    editor.kind='WaitEvent';editor.configuration=JSON.stringify({connectionId:'webhook',eventKey:'ready',correlationVariable:'externalId',outputMappings:{approved:'approved'}});editor.ngOnChanges();
    const saved=editor.config();expect(saved['eventKey']).toBe('ready');expect(saved['correlationVariable']).toBe('externalId');expect(saved['outputMappings']).toEqual({approved:'approved'});
  });
  it('reports missing response paths before a real request', () => {
    editor.sampleResponse='{"body":{"id":42}}';editor.rows.outputMappings=[{key:'id',value:'body.missing'}];editor.preview();expect(editor.error()).toContain('Missing response path');
    editor.rows.outputMappings[0].value='body.id';editor.preview();expect(JSON.parse(editor.result())).toEqual({id:42});
  });
  it('requires an explicit test action and sends typed test variables', () => {
    editor.form.connectionId='connection';editor.rows.testVariables=[{key:'amount',value:'12.5'},{key:'name',value:'Example'}];editor.test();
    expect(api.test).toHaveBeenCalledOnceWith(jasmine.any(Object),{amount:12.5,name:'Example'});expect(editor.busy()).toBeFalse();
  });
  it('stores recipients and retry settings for email', () => {
    editor.kind='NotificationTask';editor.ngOnChanges();editor.form.connectionId='smtp';editor.form.channels='Email, InApp';editor.form.to='{{email}}';editor.form.failurePolicy='Retry';editor.form.maxAttempts=3;
    const saved=editor.config();expect(saved['to']).toBe('{{email}}');expect(saved['failurePolicy']).toBe('Retry');expect(saved['maxAttempts']).toBe(3);
  });
  it('allows an in-app notification without an SMTP connection', () => {
    editor.kind='NotificationTask';editor.ngOnChanges();expect(editor.config()['connectionId']).toBeUndefined();
  });
  it('round trips a trigger binding and clears it when switched to sequence flow', () => {
    editor.configuration=JSON.stringify({connectionId:'http',triggerBinding:{sourceNodeKey:'review',trigger:'OnComment'},required:true});editor.ngOnChanges();
    expect(editor.config()['triggerBinding']).toEqual({sourceNodeKey:'review',trigger:'OnComment'});
    editor.invocation='flow';expect(editor.config()['triggerBinding']).toBeUndefined();
  });
  it('forces SLA and failure alerts to background delivery', () => {
    editor.form.connectionId='http';editor.invocation='trigger';editor.sourceNodeKey='review';editor.required=true;
    for(const trigger of ['OnSlaReminder','OnSlaBreach','OnFailure']){editor.trigger=trigger;expect(editor.config()['required']).toBeFalse();}
    editor.trigger='OnComment';expect(editor.config()['required']).toBeTrue();
  });
  it('defaults REST to required and SMS to background without changing an explicit choice', () => {
    editor.configuration=JSON.stringify({connectionId:'http',protocol:'Sms'});editor.ngOnChanges();expect(editor.required).toBeFalse();
    editor.configuration=JSON.stringify({connectionId:'http',protocol:'Rest'});editor.ngOnChanges();expect(editor.required).toBeTrue();
    editor.configuration=JSON.stringify({connectionId:'http',protocol:'Sms',required:true});editor.ngOnChanges();expect(editor.required).toBeTrue();
  });
});
