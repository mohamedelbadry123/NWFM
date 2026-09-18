import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, of } from 'rxjs';

export interface TenantOption { id: string; name: string; }
export interface ParticipantOption { id: string; actorId: string; displayName: string; displayNameAr?: string; }
interface AppContext { tenant: TenantOption; defaultParticipantId: string; participants: ParticipantOption[]; }

@Injectable({ providedIn: 'root' })
export class AppContextService {
  private readonly http = inject(HttpClient);
  readonly data = signal<AppContext | null>(null);
  readonly selectedParticipantId = signal(sessionStorage.getItem('nwfm.participant') ?? '');
  readonly error = signal('');
  readonly tenant = computed(() => this.data()?.tenant);
  readonly participants = computed(() => this.data()?.participants ?? []);
  readonly participant = computed(() => this.participants().find(p => p.id === this.selectedParticipantId()));
  async load(): Promise<void> {
    try {
      const context = await firstValueFrom(this.http.get<AppContext>('/api/app-context'));
      this.data.set(context);
      if (!context.participants.some(p => p.id === this.selectedParticipantId())) this.selectedParticipantId.set(context.defaultParticipantId);
      this.error.set('');
    } catch { this.error.set('Unable to load NWFM. Start the backend and check its database connection.'); }
  }
  selectParticipant(id: string): void {
    if (!this.participants().some(p => p.id === id)) return;
    sessionStorage.setItem('nwfm.participant', id);
    window.location.reload();
  }
  list() { return of(this.tenant() ? [this.tenant()!] : []); }
  getOrganizations(_params?: unknown) { return of({ items: this.tenant() ? [this.tenant()!] : [] }); }
}
