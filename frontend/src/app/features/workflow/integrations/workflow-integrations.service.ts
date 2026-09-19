import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ApiConfiguration } from '@shared/models/api-configuration';

export interface WorkflowConnection {
  id: string; name: string; kind: string; address: string; authentication: string;
  hasCredentials: boolean; allowPrivateNetwork: boolean; port: number; useTls: boolean;
}
export interface IntegrationOperation {
  id: string; workflowInstanceId: string; activityInstanceId: string; kind: string; status: string;
  attempts: number; nextAttemptAt: string; error?: string; statusCode?: number; createdAt: string;
}
export interface RequestResult { success: boolean; statusCode?: number; body: string; headers?: Record<string,string>; error?: string; timedOut: boolean; }
export interface EventReceipt { id: string; eventId: string; eventKey: string; correlationId: string; status: string; activityInstanceId?: string; error?: string; createdAt: string; }
export interface EventWait { id: string; workflowInstanceId: string; activityInstanceId: string; eventKey: string; correlationId: string; status: string; expiresAt: string; }
@Injectable({ providedIn: 'root' })
export class WorkflowIntegrationsService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(ApiConfiguration).rootUrl + '/api/workflow/integrations';
  connections() { return this.http.get<WorkflowConnection[]>(this.base + '/connections'); }
  saveConnection(id: string | null, body: unknown) { return id
    ? this.http.put<WorkflowConnection>(`${this.base}/connections/${id}`, body)
    : this.http.post<WorkflowConnection>(this.base + '/connections', body); }
  deleteConnection(id: string) { return this.http.delete(`${this.base}/connections/${id}`); }
  test(configuration: unknown, variables: unknown) { return this.http.post<RequestResult>(this.base + '/http/test', { configuration, variables }); }
  operations() { return this.http.get<IntegrationOperation[]>(this.base + '/operations'); }
  replay(id: string) { return this.http.post(`${this.base}/operations/${id}/replay`, {}); }
  events() { return this.http.get<EventReceipt[]>(this.base + '/events'); }
  waits() { return this.http.get<EventWait[]>(this.base + '/waits'); }
  replayEvent(id: string, activityInstanceId: string | null) { return this.http.post(`${this.base}/events/${id}/replay`, { activityInstanceId }); }
  webhookUrl(id: string) { return new URL(`${this.base}/webhooks/${id}`, window.location.origin).href; }
}
