import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { SimpleChange, signal } from '@angular/core';
import { of } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowIntegrationsService } from './workflow-integrations.service';
import { WorkflowIntegrationEditorComponent } from './workflow-integration-editor.component';
import { WORKFLOW_MAPPING_HOST } from '../mapping/mapping-host';

describe('Workflow integration configuration', () => {
  let editor: WorkflowIntegrationEditorComponent;
  const api = {
    connections: () => of([]),
    test: vi
      .fn()
      .mockReturnValue(
        of({ success: true, statusCode: 201, body: '{"id":42}', headers: { 'x-result': 'ok' } }),
      ),
  };
  beforeEach(() => {
    api.test.mockClear();
    TestBed.configureTestingModule({
      providers: [
        { provide: WorkflowIntegrationsService, useValue: api },
        { provide: LocaleService, useValue: { locale: signal('en') } },
      ],
    });
    editor = TestBed.runInInjectionContext(() => new WorkflowIntegrationEditorComponent());
    editor.configuration = '{}';
    editor.ngOnChanges();
  });
  it('preserves extension fields when editing an existing connector', () => {
    editor.configuration = JSON.stringify({
      connectionId: 'connection',
      method: 'POST',
      custom: { keep: true },
      headers: { Accept: 'application/json' },
    });
    editor.ngOnChanges();
    editor.form.path = '/orders';
    const saved = editor.config();
    expect(saved['custom']).toEqual({ keep: true });
    expect(saved['headers']).toEqual({ Accept: 'application/json' });
    expect(saved['method']).toBe('POST');
  });
  it('preserves unsaved request fields when canvas source lists refresh', () => {
    editor.form.path = '/edited';
    editor.form.body = '{"name":"draft"}';
    editor.rows.outputMappings.push({ key: 'externalId', value: 'body.id' });
    editor.ngOnChanges({
      nodes: new SimpleChange([], [{ nodeKey: 'review', name: 'Review' }], false),
    });
    expect(editor.form.path).toBe('/edited');
    expect(editor.form.body).toBe('{"name":"draft"}');
    expect(editor.rows.outputMappings).toEqual([{ key: 'externalId', value: 'body.id' }]);
  });
  it('does not contact the service while applying or previewing configuration', () => {
    editor.form.connectionId = 'connection';
    editor.apply();
    editor.preview();
    expect(api.test).not.toHaveBeenCalled();
  });
  it('rejects a missing connection and duplicate mappings', () => {
    editor.apply();
    expect(editor.error()).toContain('connection');
    editor.form.connectionId = 'connection';
    editor.rows.outputMappings = [
      { key: 'id', value: 'body.id' },
      { key: 'id', value: 'body.code' },
    ];
    editor.apply();
    expect(editor.error()).toContain('Duplicate');
  });
  it('keeps callback correlation and payload mappings through save and reload', () => {
    editor.kind = 'WaitEvent';
    editor.configuration = JSON.stringify({
      connectionId: 'webhook',
      eventKey: 'ready',
      correlationVariable: 'externalId',
      outputMappings: { approved: 'approved' },
    });
    editor.ngOnChanges();
    const saved = editor.config();
    expect(saved['eventKey']).toBe('ready');
    expect(saved['correlationVariable']).toBe('externalId');
    expect(saved['outputMappings']).toEqual({ approved: 'approved' });
  });
  it('reports missing response paths before a real request', () => {
    editor.sampleResponse = '{"body":{"id":42}}';
    editor.rows.outputMappings = [{ key: 'id', value: 'body.missing' }];
    editor.preview();
    expect(editor.error()).toContain('Missing response path');
    editor.rows.outputMappings[0].value = 'body.id';
    editor.preview();
    expect(JSON.parse(editor.result())).toEqual({ id: 42 });
  });
  it('requires an explicit test action and sends typed test variables', () => {
    editor.form.connectionId = 'connection';
    editor.rows.testVariables = [
      { key: 'amount', value: '12.5' },
      { key: 'name', value: 'Example' },
    ];
    editor.test();
    expect(api.test).toHaveBeenCalledTimes(1);
    expect(api.test).toHaveBeenCalledWith(expect.any(Object), { amount: 12.5, name: 'Example' });
    expect(editor.busy()).toBe(false);
  });
  it('stores recipients and retry settings for email', () => {
    editor.kind = 'NotificationTask';
    editor.ngOnChanges();
    editor.form.connectionId = 'smtp';
    editor.form.channels = 'Email, InApp';
    editor.form.to = '{{email}}';
    editor.form.failurePolicy = 'Retry';
    editor.form.maxAttempts = 3;
    const saved = editor.config();
    expect(saved['to']).toBe('{{email}}');
    expect(saved['failurePolicy']).toBe('Retry');
    expect(saved['maxAttempts']).toBe(3);
  });
  it('allows an in-app notification without an SMTP connection', () => {
    editor.kind = 'NotificationTask';
    editor.ngOnChanges();
    expect(editor.config()['connectionId']).toBeUndefined();
  });
  it('round trips a trigger binding and clears it when switched to sequence flow', () => {
    editor.configuration = JSON.stringify({
      connectionId: 'http',
      triggerBinding: { sourceNodeKey: 'review', trigger: 'OnComment' },
      required: true,
    });
    editor.ngOnChanges();
    expect(editor.config()['triggerBinding']).toEqual({
      sourceNodeKey: 'review',
      trigger: 'OnComment',
    });
    editor.invocation = 'flow';
    expect(editor.config()['triggerBinding']).toBeUndefined();
  });
  it('forces SLA and failure alerts to background delivery', () => {
    editor.form.connectionId = 'http';
    editor.invocation = 'trigger';
    editor.sourceNodeKey = 'review';
    editor.required = true;
    for (const trigger of ['OnSlaReminder', 'OnSlaBreach', 'OnFailure']) {
      editor.trigger = trigger;
      expect(editor.config()['required']).toBe(false);
    }
    editor.trigger = 'OnComment';
    expect(editor.config()['required']).toBe(true);
  });
  it('defaults REST to required and SMS to background without changing an explicit choice', () => {
    editor.configuration = JSON.stringify({ connectionId: 'http', protocol: 'Sms' });
    editor.ngOnChanges();
    expect(editor.required).toBe(false);
    editor.configuration = JSON.stringify({ connectionId: 'http', protocol: 'Rest' });
    editor.ngOnChanges();
    expect(editor.required).toBe(true);
    editor.configuration = JSON.stringify({
      connectionId: 'http',
      protocol: 'Sms',
      required: true,
    });
    editor.ngOnChanges();
    expect(editor.required).toBe(true);
  });

  it('keeps Sync gated and lets Async explicitly choose whether success is required', () => {
    editor.hierarchical = true;
    editor.form.connectionId = 'http';
    editor.executionMode = 'Sync';
    editor.required = false;
    expect(editor.config()['required']).toBe(true);
    editor.changeExecutionMode('Async');
    expect(editor.config()['required']).toBe(false);
    editor.required = true;
    expect(editor.config()['required']).toBe(true);
    expect(editor.config()['executionMode']).toBe('Async');
  });

  it('offers lifecycle triggers for Task Types and user actions for Steps', () => {
    editor.hierarchical = true;
    editor.nodes = [
      { nodeKey: 'meter', name: 'Meter', type: 'MainActivity' },
      { nodeKey: 'registration', name: 'Registration', type: 'UserTask' },
    ];
    editor.sourceNodeKey = 'meter';
    editor.trigger = 'OnApprove';
    editor.sourceChanged();
    expect(editor.trigger).toBe('OnComplete');
    expect(editor.availableTriggers()).not.toContain('OnApprove');
    editor.sourceNodeKey = 'registration';
    expect(editor.availableTriggers()).toContain('OnApprove');
  });
});

describe('Typed REST/JSON mapping opt-in', () => {
  const api = { connections: () => of([]), test: vi.fn() };
  const mappingHost = {
    owner: () => null,
    root: () => null,
    readonly: () => false,
    ensureSaved: async () => undefined,
    isChild: () => false,
  };

  function create(
    opts: {
      host?: boolean;
      kind?: 'ServiceTask' | 'NotificationTask';
      configuration?: Record<string, unknown>;
      readonly?: boolean;
    } = {},
  ) {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: WorkflowIntegrationsService, useValue: api },
        { provide: LocaleService, useValue: { locale: signal('en') } },
        ...(opts.host === false ? [] : [{ provide: WORKFLOW_MAPPING_HOST, useValue: mappingHost }]),
      ],
    });
    const editor = TestBed.runInInjectionContext(() => new WorkflowIntegrationEditorComponent());
    editor.kind = opts.kind ?? 'ServiceTask';
    editor.readonly = opts.readonly ?? false;
    editor.configuration = JSON.stringify(opts.configuration ?? {});
    editor.ngOnChanges();
    const emitted: Record<string, unknown>[] = [];
    editor.configurationChange.subscribe((c) => emitted.push(JSON.parse(c)));
    return { editor, emitted };
  }

  const legacy = {
    connectionId: 'c1',
    method: 'POST',
    path: '/orders?x=1',
    protocol: 'Rest',
    body: '{"a":1}',
    headers: { Accept: 'application/json' },
    query: { page: '1' },
    outputMappings: { externalId: 'body.id' },
    custom: { keep: true },
  };

  it('stays on the legacy editor by default and never adds typed members to an existing event', () => {
    const { editor } = create({ configuration: legacy });
    expect(editor.typed).toBeNull();
    const saved = editor.config();
    expect(saved).not.toHaveProperty('typedMapping');
    expect(saved['mappingMode']).toBeUndefined();
    expect(saved['headers']).toEqual({ Accept: 'application/json' });
    expect(saved['body']).toBe('{"a":1}');
  });

  it('does nothing without the designer host, for other kinds and when read-only', () => {
    const noHost = create({ host: false, configuration: legacy });
    expect(noHost.editor.mappingHost).toBeNull();
    const readonly = create({ configuration: legacy, readonly: true });
    readonly.editor.enableTyped();
    expect(readonly.editor.typed).toBeNull();
    const notification = create({ kind: 'NotificationTask' });
    notification.editor.enableTyped();
    expect(notification.editor.typed).toBeNull();
    expect(notification.editor.config(true)['mappingMode']).toBeUndefined();
  });

  it('opting in clears only the legacy request fields, keeps the connection and extension fields, and seeds the endpoint from them', () => {
    const { editor, emitted } = create({ configuration: legacy });
    editor.enableTyped();
    expect(editor.typed!.endpoint).toEqual(
      expect.objectContaining({ method: 'POST', path: '/orders' }),
    );
    const saved = emitted[emitted.length - 1];
    expect(saved).toEqual(
      expect.objectContaining({
        mappingMode: 'Typed',
        protocol: 'Rest',
        contentType: 'application/json',
        method: 'POST',
        path: '/orders',
        headers: {},
        query: {},
        outputMappings: {},
        body: null,
        connectionId: 'c1',
        custom: { keep: true },
      }),
    );
    expect(saved['typedMapping']).toEqual(expect.objectContaining({ contractVersion: 1 }));
    expect(editor.tabs).toEqual(['Authorization', 'Delivery']);
  });

  it('typed edits reach the designer even before a connection is chosen', () => {
    const { editor, emitted } = create({ configuration: { method: 'GET', path: '/x' } });
    editor.enableTyped();
    expect(emitted).toHaveLength(1);
    expect(editor.error()).toBe('');
    expect(() => editor.config(false)).toThrow('Select a connection.');
  });

  it('reloads a typed event as typed, and a legacy mapping mode only when the event says Typed', () => {
    const { editor } = create({
      configuration: {
        ...legacy,
        mappingMode: 'Typed',
        typedMapping: {
          contractVersion: 1,
          endpoint: { method: 'GET', path: '/orders', parameters: [] },
        },
      },
    });
    expect(editor.typed!.endpoint).toEqual(expect.objectContaining({ path: '/orders' }));
    const stale = create({ configuration: { ...legacy, typedMapping: { contractVersion: 1 } } });
    expect(stale.editor.typed).toBeNull();
    expect(stale.editor.config()).not.toHaveProperty('typedMapping');
  });

  it('going back to legacy drops the typed mapping and marks the event Legacy, never migrating it', () => {
    const { editor, emitted } = create({ configuration: legacy });
    editor.enableTyped();
    editor.disableTyped();
    expect(editor.typed).toBeNull();
    const saved = emitted[emitted.length - 1];
    expect(saved).not.toHaveProperty('typedMapping');
    // Opting in and straight back never wrote a mode, so the event is exactly as plain as it started: nothing invented.
    expect(saved['mappingMode']).toBeUndefined();
    expect(saved['body']).toBeNull();
    expect(saved['headers']).toEqual({});
  });

  it('an event that was saved as Typed goes back as Legacy, without its typed block', () => {
    const { editor, emitted } = create({
      configuration: {
        connectionId: 'c1',
        mappingMode: 'Typed',
        typedMapping: {
          contractVersion: 1,
          endpoint: { method: 'GET', path: '/orders', parameters: [] },
        },
      },
    });
    editor.disableTyped();
    const saved = emitted[emitted.length - 1];
    expect(saved).not.toHaveProperty('typedMapping');
    expect(saved['mappingMode']).toBe('Legacy');
    expect(saved['connectionId']).toBe('c1');
  });

  it('exposes the complete HTTP configuration to Send Test, and does not contact the service while editing', () => {
    api.test.mockClear();
    const { editor } = create({ configuration: legacy });
    editor.enableTyped();
    const http = editor.typedHttpConfiguration() as Record<string, unknown>;
    expect(http['connectionId']).toBe('c1');
    expect(http['typedMapping']).toBeDefined();
    expect(api.test).not.toHaveBeenCalled();
  });
});
