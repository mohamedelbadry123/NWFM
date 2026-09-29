import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslateService } from '@ngx-translate/core';
import { Observable, Subject, of } from 'rxjs';

import { LocaleService } from '../../../core/i18n/locale.service';
import { LookupItem, LookupType, LookupsService } from '../../../core/lookups/lookups.service';
import { OrgScopeSelectorComponent } from './org-scope-selector.component';
import { OrgLocation } from './org-scope.model';

/**
 * The cascade every module places work with: Cluster → CBU → (Branch | Operation Area). A parent
 * change must clear what no longer fits beneath it, each list must come from the canonical lookups
 * filtered by its parent, and branch and operation area are siblings — picking one keeps the other.
 */
describe('OrgScopeSelectorComponent', () => {
  let fixture: ComponentFixture<OrgScopeSelectorComponent>;
  let picker: OrgScopeSelectorComponent;
  let emitted: OrgLocation[];
  let pending: Map<string, Subject<LookupItem[]>>;
  let requests: string[];

  const item = (code: string, parentCode?: string): LookupItem => ({ id: code, code, nameEn: code, nameAr: code, isActive: true, parentCode });
  const key = (type: LookupType, parentCode?: string) => `${type}:${parentCode ?? ''}`;
  const reply = (type: LookupType, parentCode: string, items: LookupItem[]) => pending.get(key(type, parentCode))!.next(items);

  beforeEach(() => {
    pending = new Map();
    requests = [];
    const lookups = {
      listAll: (type: LookupType, options?: { parentCode?: string }): Observable<LookupItem[]> => {
        if (!options?.parentCode) return of(type === 'Cluster' ? [item('C1'), item('C2')] : []);
        requests.push(key(type, options.parentCode));
        const subject = new Subject<LookupItem[]>();
        pending.set(key(type, options.parentCode), subject);
        return subject;
      },
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: LookupsService, useValue: lookups },
        { provide: LocaleService, useValue: { locale: () => 'en' } },
        { provide: TranslateService, useValue: { instant: (k: string) => k } },
      ],
    });
    TestBed.overrideComponent(OrgScopeSelectorComponent, { set: { template: '', imports: [] } });

    fixture = TestBed.createComponent(OrgScopeSelectorComponent);
    picker = fixture.componentInstance;
    emitted = [];
    picker.locationChange.subscribe(location => emitted.push(location));
    fixture.detectChanges();
  });

  function pick(): void {
    picker['onClusterChange']('C1');
    reply('Cbu', 'C1', [item('CB1', 'C1'), item('CB2', 'C1')]);
    picker['onCbuChange']('CB1');
    reply('Branch', 'CB1', [item('BR1', 'CB1')]);
    reply('OperationArea', 'CB1', [item('OA1', 'CB1')]);
  }

  it('loads each level from the canonical lookups filtered by its parent', () => {
    pick();

    expect(requests).toEqual(['Cbu:C1', 'Branch:CB1', 'OperationArea:CB1']);
    expect(picker['cbus']().map(o => o.value)).toEqual(['CB1', 'CB2']);
    expect(picker['branches']().map(o => o.value)).toEqual(['BR1']);
    expect(picker['operationAreas']().map(o => o.value)).toEqual(['OA1']);
  });

  it('keeps branch and operation area side by side — they are siblings, not a chain', () => {
    pick();
    picker['onBranchChange']('BR1');
    picker['onOperationAreaChange']('OA1');

    expect(emitted.at(-1)).toEqual({ clusterCode: 'C1', cbuCode: 'CB1', branchCode: 'BR1', operationAreaCode: 'OA1' });
  });

  it('clears the CBU, branch and operation area when the cluster changes', () => {
    pick();
    picker['onBranchChange']('BR1');
    picker['onOperationAreaChange']('OA1');

    picker['onClusterChange']('C2');

    expect(emitted.at(-1)).toEqual({ clusterCode: 'C2', cbuCode: null, branchCode: null, operationAreaCode: null });
    expect(picker['branches']()).toEqual([]);
    expect(picker['operationAreas']()).toEqual([]);
  });

  it('clears branch and operation area, and reloads both, when the CBU changes', () => {
    pick();
    picker['onBranchChange']('BR1');
    picker['onOperationAreaChange']('OA1');

    picker['onCbuChange']('CB2');

    expect(emitted.at(-1)).toEqual({ clusterCode: 'C1', cbuCode: 'CB2', branchCode: null, operationAreaCode: null });
    expect(requests).toContain('Branch:CB2');
    expect(requests).toContain('OperationArea:CB2');
  });

  it('ignores a late reply for a parent that is no longer selected', () => {
    picker['onClusterChange']('C1');
    picker['onClusterChange']('C2');
    reply('Cbu', 'C2', [item('CB3', 'C2')]);
    reply('Cbu', 'C1', [item('CB1', 'C1')]);

    expect(picker['cbus']().map(o => o.value)).toEqual(['CB3']);

    picker['onCbuChange']('CB3');
    picker['onCbuChange'](null);
    reply('Branch', 'CB3', [item('BR4', 'CB3')]);

    expect(picker['branches']()).toEqual([]);
  });

  it('rebuilds the cascade from a saved location', async () => {
    fixture.componentRef.setInput('initialLocation', { clusterCode: 'C1', cbuCode: 'CB1', branchCode: 'BR1', operationAreaCode: null });
    fixture.detectChanges();
    await fixture.whenStable();

    expect(picker['clusterCode']()).toBe('C1');
    expect(picker['cbuCode']()).toBe('CB1');
    expect(picker['branchCode']()).toBe('BR1');
    expect(requests).toEqual(['Cbu:C1', 'Branch:CB1', 'OperationArea:CB1']);
  });
});
