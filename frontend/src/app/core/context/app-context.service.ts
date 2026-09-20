import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, of } from 'rxjs';

export interface TenantOption { id: string; name: string; }
export interface ParticipantOption { id: string; actorId: string; displayName: string; displayNameAr?: string; }
interface AppContext {
  tenant: TenantOption;
  defaultParticipantId: string;
  participants: ParticipantOption[];
}

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
      const raw = await firstValueFrom(this.http.get<{
        tenantId?: string;
        tenant?: TenantOption;
        participants?: ParticipantOption[];
        defaultParticipantId?: string | null;
      }>('/api/app-context'));
      const tenant = raw.tenant ?? { id: raw.tenantId ?? '', name: 'NWFM' };
      const participants = raw.participants ?? [];
      const defaultParticipantId = raw.defaultParticipantId ?? '';
      this.data.set({ tenant, participants, defaultParticipantId });
      if (participants.length && !participants.some(p => p.id === this.selectedParticipantId())) {
        this.selectedParticipantId.set(defaultParticipantId);
      }
      this.error.set('');
    } catch {
      this.data.set({
        tenant: { id: '', name: 'NWFM' },
        participants: [],
        defaultParticipantId: '',
      });
      this.error.set('');
    }
  }

  selectParticipant(id: string): void {
    if (!this.participants().some(p => p.id === id)) return;
    sessionStorage.setItem('nwfm.participant', id);
    window.location.reload();
  }

  list() { return of(this.tenant() ? [this.tenant()!] : []); }
  getOrganizations(_params?: unknown) { return of({ items: this.tenant() ? [this.tenant()!] : [] }); }
}
