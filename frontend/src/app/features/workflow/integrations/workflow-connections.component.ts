import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { RouterLink } from '@angular/router';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowConnection, WorkflowIntegrationsService, IntegrationOperation, EventReceipt, EventWait } from './workflow-integrations.service';
import { DatePipe } from '@angular/common';

@Component({ selector: 'app-workflow-connections', standalone: true, imports: [FormsModule, RouterLink, DatePipe],
  templateUrl: './workflow-connections.component.html', styleUrl: './workflow-integrations.css' })
export class WorkflowConnectionsComponent implements OnInit {
  @Input() embedded = false;
  readonly api = inject(WorkflowIntegrationsService);
  readonly locale = inject(LocaleService);
  readonly connections = signal<WorkflowConnection[]>([]);
  readonly operations = signal<IntegrationOperation[]>([]);
  readonly events = signal<EventReceipt[]>([]); readonly waits = signal<EventWait[]>([]);
  replayTargets: Record<string,string> = {};
  readonly error = signal(''); readonly busy = signal(false); readonly editing = signal(false);
  readonly standalone = { standalone: true };
  id: string | null = null;
  form = this.empty(); credentials: Record<string, string> = {}; replaceCredentials = true;
  t(en: string, ar: string) { return this.locale.locale() === 'ar' ? ar : en; }
  empty() { return { name: '', kind: 'Http', address: '', authentication: 'None', allowPrivateNetwork: false, port: 587, useTls: true }; }
  ngOnInit() { this.refresh(); }
  refresh() {
    this.api.connections().subscribe({ next: rows => this.connections.set(rows), error: e => this.error.set(e.error?.message ?? 'Could not load connections.') });
    if (!this.embedded) this.api.operations().subscribe({ next: rows => this.operations.set(rows), error: e => this.error.set(e.error?.message ?? 'Could not load operations.') });
    if (!this.embedded) {
      this.api.events().subscribe({ next: rows => this.events.set(rows), error: e => this.error.set(e.error?.message ?? 'Could not load events.') });
      this.api.waits().subscribe({ next: rows => this.waits.set(rows), error: e => this.error.set(e.error?.message ?? 'Could not load waits.') });
    }
  }
  edit(row?: WorkflowConnection) {
    this.id = row?.id ?? null; this.form = row ? { ...row } : this.empty(); this.credentials = {};
    this.replaceCredentials = !row; this.error.set(''); this.editing.set(true);
  }
  authOptions() { return this.form.kind === 'Http' ? ['None', 'ApiKey', 'Basic', 'Bearer', 'OAuth2'] : this.form.kind === 'Smtp' ? ['None', 'Basic'] : ['Hmac', 'ApiKey']; }
  kindChanged() { this.form.authentication = this.form.kind === 'Webhook' ? 'Hmac' : 'None'; this.credentials = {}; this.replaceCredentials = true; }
  credentialFields() {
    const fields: Record<string, string[]> = { None: [], Basic: ['username', 'password'], Bearer: ['token'], ApiKey: ['apiKey'], OAuth2: ['tokenUrl', 'clientId', 'clientSecret', 'scope'], Hmac: ['secret'] };
    return [...(fields[this.form.authentication] ?? []), ...(this.form.kind === 'Http' && this.form.authentication === 'ApiKey' ? ['header', 'location'] : []), ...(this.form.kind === 'Smtp' ? ['fromAddress'] : [])];
  }
  label(key: string) {
    const labels: Record<string, [string,string]> = { username: ['Username','اسم المستخدم'], password: ['Password','كلمة المرور'], token: ['Bearer token','رمز الدخول'], apiKey: ['API key','مفتاح الواجهة'], tokenUrl: ['Token endpoint URL','رابط إصدار الرمز'], clientId: ['Client ID','معرف العميل'], clientSecret: ['Client secret','سر العميل'], scope: ['Scope (optional)','النطاق (اختياري)'], secret: ['Signing secret (32+ characters)','سر التوقيع (٣٢ حرفاً على الأقل)'], header: ['API key name (default X-Api-Key)','اسم مفتاح الواجهة'], location: ['API key location: header or query','موضع المفتاح: header أو query'], fromAddress: ['Sender email','بريد المرسل'] };
    const values = labels[key] ?? [key,key]; return this.t(...values);
  }
  isSecret(key: string) { return ['password','token','apiKey','clientSecret','secret'].includes(key); }
  save() {
    this.busy.set(true); this.error.set('');
    this.api.saveConnection(this.id, { ...this.form, credentials: this.replaceCredentials ? this.credentials : null }).subscribe({
      next: () => { this.busy.set(false); this.editing.set(false); this.credentials = {}; this.refresh(); },
      error: e => { this.busy.set(false); this.error.set(e.error?.message ?? 'Could not save connection.'); }
    });
  }
  remove(row: WorkflowConnection) {
    this.api.deleteConnection(row.id).subscribe({ next: () => this.refresh(), error: e => this.error.set(e.error?.message ?? 'Could not remove connection.') });
  }
  replay(row: IntegrationOperation) {
    this.api.replay(row.id).subscribe({ next: () => this.refresh(), error: e => this.error.set(e.error?.message ?? 'Could not replay operation.') });
  }
  replayEvent(row: EventReceipt) {
    this.api.replayEvent(row.id, this.replayTargets[row.id] || null).subscribe({ next: () => this.refresh(), error: e => this.error.set(e.error?.message ?? 'Could not replay event.') });
  }
}
