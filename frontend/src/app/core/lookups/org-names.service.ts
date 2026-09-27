import { Injectable, inject, signal } from '@angular/core';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { LocaleService } from '../i18n/locale.service';
import { LookupItem, LookupType, LookupsService } from './lookups.service';

type NameTables = Partial<Record<LookupType, ReadonlyMap<string, LookupItem>>>;

const DISPLAYED: readonly LookupType[] = ['Department', 'Cluster', 'Cbu', 'Branch', 'OperationArea'];

/**
 * Turns org codes into names for display. Work carries codes — a task never stores a branch's name —
 * so the grids look them up here. Loaded once per session and held in a signal, so a table that
 * rendered codes before the names arrived redraws with them.
 */
@Injectable({ providedIn: 'root' })
export class OrgNamesService {
  private readonly lookups = inject(LookupsService);
  private readonly locale = inject(LocaleService);

  private readonly tables = signal<NameTables>({});
  private requested = false;

  /** Starts the one load. Safe to call from every page that shows org codes. */
  ensureLoaded(): void {
    if (this.requested) {
      return;
    }

    this.requested = true;

    forkJoin(
      DISPLAYED.map((type) => this.lookups.listAll(type).pipe(catchError(() => of([] as LookupItem[])))),
    ).subscribe((lists) => {
      const tables: NameTables = {};
      DISPLAYED.forEach((type, index) => {
        tables[type] = new Map(lists[index].map((item) => [item.code.toUpperCase(), item]));
      });
      this.tables.set(tables);
    });
  }

  /** The unit's name in the reader's language, or null when the code is unknown or not loaded yet. */
  name(type: LookupType, code: string | null | undefined): string | null {
    if (!code) {
      return null;
    }

    const item = this.tables()[type]?.get(code.toUpperCase());
    if (!item) {
      return null;
    }

    return this.locale.locale() === 'ar' ? item.nameAr : item.nameEn;
  }

  /** `code — name` when the name is known, the bare code otherwise, a dash when there is no code. */
  label(type: LookupType, code: string | null | undefined): string {
    if (!code) {
      return '—';
    }

    const name = this.name(type, code);
    return name ? `${code} — ${name}` : code;
  }
}
