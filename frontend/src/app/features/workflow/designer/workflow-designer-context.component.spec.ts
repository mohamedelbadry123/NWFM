import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { of, throwError } from 'rxjs';
import { page } from 'vitest/browser';
import { LocaleService } from '@core/i18n/locale.service';
import { LookupsService } from '@core/lookups/lookups.service';
import { WorkflowDefinitionsService } from '../workflow-definitions.service';
import { WorkflowWorkspaceService } from '../workspace/workflow-workspace.service';
import { WorkflowDesignerContextComponent } from './workflow-designer-context.component';

describe('Designer context', () => {
  const locale = signal('en');
  const listAll = vi.fn();
  const main = {
    kind: 'Main' as const,
    schemaVersion: 2,
    designerVersion: 3,
    fieldActivityTypeId: 'unrc',
    fieldActivityCode: 'UNRC',
    organizationScopes: [{ level: 'Cluster' as const, code: 'E', clusterCode: 'E' }],
  };
  beforeEach(() => {
    locale.set('en');
    listAll
      .mockReset()
      .mockImplementation(() =>
        of([
          { id: 'E', code: 'E', nameEn: 'East Cluster', nameAr: 'القطاع الشرقي', isActive: true },
        ]),
      );
    TestBed.configureTestingModule({
      imports: [WorkflowDesignerContextComponent],
      providers: [
        { provide: LocaleService, useValue: { locale } },
        { provide: LookupsService, useValue: { listAll } },
        {
          provide: WorkflowDefinitionsService,
          useValue: { getById: () => of({ name: 'UNRC parent' }) },
        },
        {
          provide: WorkflowWorkspaceService,
          useValue: {
            references: () =>
              of([
                {
                  id: 'unrc',
                  code: 'UNRC',
                  nameEn: 'Unregistered Connection',
                  nameAr: 'توصيلة غير مسجلة',
                },
                {
                  id: 'meter',
                  code: 'METER',
                  nameEn: 'Meter Installation',
                  nameAr: 'تركيب العداد',
                },
              ]),
          },
        },
      ],
    });
  });
  function render(settings = main) {
    const fixture = TestBed.createComponent(WorkflowDesignerContextComponent);
    fixture.componentRef.setInput('settings', settings);
    fixture.componentRef.setInput('name', 'UNRC — MVP Practice');
    fixture.componentRef.setInput('hierarchical', true);
    fixture.detectChanges();
    return fixture;
  }
  it('keeps the chosen activity, named cluster, editing level and next action visible', () => {
    const fixture = render();
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Main workflow designer');
    expect(text).toContain('Unregistered Connection');
    expect(text).toContain('East Cluster');
    expect(text).toContain('Version 1 · Draft');
    const add = vi.fn();
    fixture.componentInstance.addRequested.subscribe(add);
    fixture.nativeElement.querySelector('.add-link').click();
    expect(add).toHaveBeenCalledWith('MainActivity');
    fixture.componentRef.setInput('selectedName', 'Meter installation');
    fixture.componentRef.setInput('selectedType', 'MainActivity');
    fixture.componentRef.setInput('workCount', 1);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.editing-context').textContent).toContain(
      'Task Type: Meter installation',
    );
    expect(fixture.nativeElement.querySelector('.context-guidance')).toBeNull();
  });
  it('shows the parent and child task type, and returns to the parent on request', () => {
    const fixture = render();
    fixture.componentRef.setInput('settings', {
      ...main,
      kind: 'Child',
      taskTypeId: 'meter',
      fieldActivityTypeId: undefined,
      fieldActivityCode: undefined,
      parentDefinitionId: 'parent',
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Child workflow designer');
    expect(fixture.nativeElement.textContent).toContain('UNRC parent');
    expect(fixture.nativeElement.textContent).toContain('Meter Installation');
    expect(fixture.nativeElement.textContent).toContain('add a Step');
    const back = vi.fn();
    fixture.componentInstance.parentRequested.subscribe(back);
    fixture.nativeElement.querySelectorAll('.context-path button')[1].click();
    expect(back).toHaveBeenCalledOnce();
  });
  it('keeps published versions read only and reflects changed setup immediately', () => {
    const fixture = render();
    fixture.componentRef.setInput('status', 'Published');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.add-link')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('read only');
    fixture.componentRef.setInput('settings', {
      ...main,
      organizationScopes: [{ level: 'Cluster', code: 'N', clusterCode: 'N' }],
    });
    fixture.detectChanges();
    expect(fixture.componentInstance.scopeLabels()).toEqual(['N']);
  });
  it('shows saved entries as authored and keeps older scopes readable, with two chips and +N more', () => {
    const scope = (code: string, parent?: string) =>
      parent
        ? { level: 'Cbu' as const, code, clusterCode: parent, cbuCode: code }
        : { level: 'Cluster' as const, code, clusterCode: code };
    const fixture = render();
    fixture.componentRef.setInput('settings', {
      ...main,
      organizationScopes: [
        { level: 'Branch', code: 'EB', clusterCode: 'E', cbuCode: 'E1' },
        { level: 'OperationArea', code: 'EA', clusterCode: 'E', cbuCode: 'E1' },
        scope('N'),
        scope('S'),
      ],
      scopeRows: [
        { id: 'r1', clusterCode: 'E', cbuCode: 'E1', branchCode: 'EB', operationAreaCode: 'EA' },
        { id: 'r2', clusterCode: 'N' },
        { id: 'r3', clusterCode: 'S' },
      ],
    });
    fixture.detectChanges();
    // Branch and Operation Area saved in one entry read as one line.
    expect(fixture.componentInstance.scopeLabels()).toEqual([
      'East Cluster / E1 / Branch: EB / Operation area: EA',
      'N',
      'S',
    ]);
    const chips = () => fixture.nativeElement.querySelectorAll('app-workflow-scope-summary li');
    expect(chips()).toHaveLength(2);
    const more = fixture.nativeElement.querySelector('app-workflow-scope-summary button');
    expect(more.textContent.trim()).toBe('+1 more');
    more.click();
    fixture.detectChanges();
    expect(chips()).toHaveLength(3);

    // Entries that no longer describe the saved scopes are ignored; the scopes win.
    fixture.componentRef.setInput('settings', {
      ...main,
      organizationScopes: [scope('N')],
      scopeRows: [{ id: 'r1', clusterCode: 'E' }],
    });
    fixture.detectChanges();
    expect(fixture.componentInstance.scopeLabels()).toEqual(['N']);
  });
  it('shows saved codes with a retry when names are unavailable', () => {
    listAll.mockReturnValue(throwError(() => new Error('offline')));
    const fixture = render();
    expect(fixture.nativeElement.textContent).toContain('Saved codes are shown');
    expect(fixture.componentInstance.scopeLabels()).toEqual(['E']);
    expect(fixture.componentInstance.classification()).toBe('UNRC');
  });
  it('fits a mobile Arabic context without hiding the selected scope', async () => {
    const fixture = render();
    locale.set('ar');
    fixture.nativeElement.dir = 'rtl';
    await page.viewport(390, 844);
    fixture.detectChanges();
    try {
      expect(fixture.nativeElement.textContent).toContain('القطاع الشرقي');
      expect(fixture.nativeElement.scrollWidth).toBeLessThanOrEqual(
        fixture.nativeElement.clientWidth,
      );
      await page.screenshot({ path: '__screenshots__/designer-context-ar.png' });
      await page.viewport(1280, 900);
      locale.set('en');
      fixture.nativeElement.dir = 'ltr';
      fixture.detectChanges();
      await page.screenshot({ path: '__screenshots__/designer-context-desktop.png' });
    } finally {
      await page.viewport(1280, 900);
    }
  });
});
