import { Component, EventEmitter, Input, OnChanges, OnInit, Output, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowConnectionsComponent } from './workflow-connections.component';
import { WorkflowConnection, WorkflowIntegrationsService } from './workflow-integrations.service';

type Pair = { key: string; value: string };
interface IntegrationForm {
  protocol: string; soapVersion: string; soapAction: string; smsTo: string; smsMessage: string;
  connectionId: string; method: string; path: string; contentType: string; body: string;
  timeoutSeconds: number; maxAttempts: number; retryDelaySeconds: number; idempotencyHeader: string;
  errorOutcome: string; timeoutOutcome: string; eventKey: string; correlationVariable: string;
  channels: string; templateKey: string; to: string; cc: string; bcc: string; subject: string; isHtml: boolean; failurePolicy: string;
}
@Component({ selector: 'app-workflow-integration-editor', standalone: true, imports: [FormsModule, WorkflowConnectionsComponent],
  templateUrl: './workflow-integration-editor.component.html', styleUrl: './workflow-integrations.css' })
export class WorkflowIntegrationEditorComponent implements OnInit, OnChanges {
  @Input() kind: 'ServiceTask' | 'WaitEvent' | 'NotificationTask' = 'ServiceTask';
  @Input() configuration = ''; @Input() readonly = false; @Input() variables: { variableKey: string }[] = [];
  @Input() protocol?: string; @Input() activityEvent = false;
  @Output() configurationChange = new EventEmitter<string>();
  readonly api = inject(WorkflowIntegrationsService); readonly locale = inject(LocaleService);
  readonly connections = signal<WorkflowConnection[]>([]); readonly error = signal(''); readonly result = signal(''); readonly busy = signal(false);
  readonly managing = signal(false); readonly standalone = { standalone: true };
  form: IntegrationForm = this.defaults(); original: Record<string, unknown> = {};
  rows: Record<'headers' | 'query' | 'outputMappings' | 'xmlNamespaces' | 'xmlOutputMappings' | 'testVariables', Pair[]> = { headers: [], query: [], outputMappings: [], xmlNamespaces: [], xmlOutputMappings: [], testVariables: [] };
  statusCodes = ''; sampleResponse = '{}'; recipientIds = '';
  t(en: string, ar: string) { return this.locale.locale() === 'ar' ? ar : en; }
  defaults(): IntegrationForm { return { protocol: 'Rest', soapVersion: '1.1', soapAction: '', smsTo: '', smsMessage: '', connectionId: '', method: 'GET', path: '/', contentType: 'application/json', body: '', timeoutSeconds: this.kind === 'WaitEvent' ? 86400 : 30, maxAttempts: 1, retryDelaySeconds: 10, idempotencyHeader: 'Idempotency-Key', errorOutcome: 'error', timeoutOutcome: 'timeout', eventKey: '', correlationVariable: 'CorrelationId', channels: 'InApp', templateKey: 'workflow.default', to: '', cc: '', bcc: '', subject: 'Workflow notification', isHtml: false, failurePolicy: 'FailWorkflow' }; }
  ngOnInit() { this.refresh(); }
  ngOnChanges() {
    try { this.original = JSON.parse(this.configuration || '{}') as Record<string, unknown>; } catch { this.original = {}; }
    this.form = { ...this.defaults(), ...this.original } as IntegrationForm;
    if (this.protocol) this.form.protocol = this.protocol;
    if (!this.form.eventKey && typeof this.original['signalKey'] === 'string') this.form.eventKey = this.original['signalKey'];
    for (const key of ['headers','query','outputMappings','xmlNamespaces','xmlOutputMappings'] as const) this.rows[key] = Object.entries((this.original[key] ?? {}) as Record<string,string>).map(([key,value]) => ({key,value}));
    this.statusCodes = Array.isArray(this.original['successStatusCodes']) ? (this.original['successStatusCodes'] as number[]).join(', ') : '';
    this.recipientIds = Array.isArray(this.original['recipientUserIds']) ? (this.original['recipientUserIds'] as string[]).join(', ') : '';
  }
  refresh() { this.api.connections().subscribe({ next: connections => this.connections.set(connections), error: e => this.error.set(e.error?.message ?? 'Could not load connections.') }); }
  connectionKind() { return this.kind === 'ServiceTask' ? 'Http' : this.kind === 'WaitEvent' ? 'Webhook' : 'Smtp'; }
  availableConnections() { return this.connections().filter(c => c.kind === this.connectionKind()); }
  variableNames() { return [...new Set(['BusinessEntityId','CorrelationId','OrganizationId', ...this.variables.map(v => v.variableKey)])]; }
  groups(): { key: 'headers' | 'query' | 'outputMappings' | 'xmlNamespaces' | 'xmlOutputMappings'; label: string }[] { return [
    ...(this.kind === 'ServiceTask' ? [{key: 'headers' as const, label: this.t('Headers','الترويسات')}, {key: 'query' as const, label: this.t('Query parameters','معاملات الاستعلام')}] : []),
    ...(this.kind === 'ServiceTask' && this.form.protocol === 'Soap' ? [{key: 'xmlNamespaces' as const,label: this.t('XML namespaces (prefix → URI)','مساحات أسماء XML')},{key:'xmlOutputMappings' as const,label:this.t('Response mappings (variable → XPath)','ربط الاستجابة (متغير ← XPath)')}] : this.kind !== 'NotificationTask' ? [{key: 'outputMappings' as const, label: this.t('Response → workflow variables','الاستجابة ← متغيرات سير العمل')}] : []) ]; }
  config() {
    const result: Record<string, unknown> = { ...this.original, ...this.form };
    for (const key of ['headers','query','outputMappings','xmlNamespaces','xmlOutputMappings'] as const) {
      const rows = this.rows[key].filter(r => r.key.trim());
      if (new Set(rows.map(r => r.key.trim())).size !== rows.length) throw new Error(this.t('Duplicate field names are not allowed.','لا يمكن تكرار أسماء الحقول.'));
      result[key] = Object.fromEntries(rows.map(r => [r.key.trim(),r.value]));
    }
    const codes = this.statusCodes.split(',').map(v => v.trim()).filter(Boolean).map(Number);
    if (codes.some(c => !Number.isInteger(c) || c < 200 || c > 599)) throw new Error(this.t('Success codes must be numbers from 200 to 599.','رموز النجاح يجب أن تكون بين 200 و599.'));
    result['successStatusCodes'] = codes;
    if (this.kind === 'ServiceTask' && this.form.protocol === 'Soap') {
      result['method'] = 'POST'; result['contentType'] = this.form.soapVersion === '1.2' ? 'application/soap+xml' : 'text/xml'; result['outputMappings'] = {};
    }
    result['recipientUserIds'] = this.recipientIds.split(',').map(v => v.trim()).filter(Boolean);
    if (!this.form.connectionId && (this.kind !== 'NotificationTask' || this.form.channels.includes('Email'))) throw new Error(this.t('Select a connection.','اختر اتصالاً.'));
    if (!this.form.connectionId) delete result['connectionId'];
    if (this.kind === 'ServiceTask' && !this.form.body) result['body'] = null;
    delete result['signalKey'];
    return result;
  }
  apply() { try { this.error.set(''); this.configurationChange.emit(JSON.stringify(this.config())); this.result.set(this.t('Settings applied. Save the workflow to persist them.','تم تطبيق الإعدادات. احفظ سير العمل لحفظها.')); } catch (e) { this.error.set((e as Error).message); } }
  testVariables() { return Object.fromEntries(this.rows.testVariables.filter(r => r.key.trim()).map(r => { let value: unknown = r.value; try { value = JSON.parse(r.value); } catch { /* plain text */ } return [r.key.trim(),value]; })); }
  test() {
    try {
      const config = this.config(); this.busy.set(true); this.error.set('');
      this.api.test(config, this.testVariables()).subscribe({ next: result => { this.busy.set(false); this.result.set(JSON.stringify(result,null,2)); this.sampleResponse = this.form.protocol === 'Soap' ? result.body : JSON.stringify({status: result.statusCode, headers: result.headers, body: this.parseBody(result.body)},null,2); }, error: e => { this.busy.set(false); this.error.set(e.error?.message ?? 'Request failed.'); } });
    } catch(e) { this.error.set((e as Error).message); }
  }
  parseBody(body: string): unknown { try { return JSON.parse(body); } catch { return body; } }
  preview() {
    try {
      if (this.kind === 'ServiceTask' && this.form.protocol === 'Soap') {
        const xml = new DOMParser().parseFromString(this.sampleResponse, 'application/xml');
        if (xml.querySelector('parsererror') || /<!DOCTYPE/i.test(this.sampleResponse)) throw new Error('Enter valid XML without a DTD.');
        const namespaces = Object.fromEntries(this.rows.xmlNamespaces.map(r => [r.key,r.value]));
        namespaces['soap'] ??= this.form.soapVersion === '1.2' ? 'http://www.w3.org/2003/05/soap-envelope' : 'http://schemas.xmlsoap.org/soap/envelope/';
        const outputs: Record<string,string> = {};
        for (const row of this.rows.xmlOutputMappings.filter(r=>r.key.trim())) outputs[row.key] = xml.evaluate(`string(${row.value})`,xml,prefix=>prefix ? namespaces[prefix] ?? null : null,XPathResult.STRING_TYPE).stringValue;
        this.error.set(''); this.result.set(JSON.stringify(outputs,null,2)); return;
      }
      const root = JSON.parse(this.sampleResponse) as unknown; const outputs: Record<string, unknown> = {};
      for (const row of this.rows.outputMappings.filter(r => r.key.trim())) {
        let value = root; const path = row.value.replace(/^\$\./,'');
        for (const part of path === '$' || !path ? [] : path.split('.')) {
          if (value === null || typeof value !== 'object' || !(part in value)) throw new Error(`Missing response path: ${row.value}`);
          value = (value as Record<string,unknown>)[part];
        }
        outputs[row.key] = value;
      }
      this.error.set(''); this.result.set(JSON.stringify(outputs,null,2));
    } catch(e) { this.error.set((e as Error).message); }
  }
  eventExample() { return JSON.stringify({eventId: 'unique-message-id', eventKey: this.form.eventKey, correlationId: 'value-of-' + this.form.correlationVariable, payload: {status: 'approved'}},null,2); }
}
