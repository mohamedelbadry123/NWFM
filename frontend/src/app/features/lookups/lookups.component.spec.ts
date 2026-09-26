import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';

import { LookupsComponent } from './lookups.component';
import { AuthStore } from '../../core/auth/auth.store';
import { ADMINISTRATOR_ROLE, PERMISSIONS } from '../../core/auth/permissions';
import { LookupsService } from '../../core/lookups/lookups.service';

/**
 * Lookups holds the org lookups and, since the menu was trimmed, the task types, C2M action mappings
 * and field catalog. Each tab shows only to someone who may use it, and the old addresses of the
 * moved screens land on their tab through `?tab=`.
 */
describe('LookupsComponent tabs', () => {
  function create(options: { permissions?: string[]; roles?: string[]; tab?: string | null }) {
    const permissions = options.permissions ?? [];

    TestBed.overrideComponent(LookupsComponent, { set: { template: '', imports: [] } });
    TestBed.configureTestingModule({
      imports: [LookupsComponent, TranslateModule.forRoot()],
      providers: [
        {
          provide: AuthStore,
          useValue: {
            roles: signal(options.roles ?? []),
            hasAnyPermission: (...wanted: string[]) => wanted.some((p) => permissions.includes(p)),
          },
        },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(options.tab ? { tab: options.tab } : {}) } } },
        { provide: Router, useValue: { navigate: jasmine.createSpy('navigate').and.resolveTo(true) } },
        { provide: LookupsService, useValue: {} },
      ],
    });

    const fixture = TestBed.createComponent(LookupsComponent);
    fixture.componentInstance.ngOnInit();
    return fixture.componentInstance as unknown as {
      visibleTabs: () => { key: string }[];
      activeKey: () => string;
      onTabChange: (value: string) => void;
      currentTab: () => { type: string; parentType?: string };
    };
  }

  it('shows a task-type manager only the task tabs', () => {
    const page = create({ permissions: [PERMISSIONS.manageTaskTypes] });

    expect(page.visibleTabs().map((t) => t.key)).toEqual(['task-types', 'c2m-actions']);
    expect(page.activeKey()).toBe('task-types');
  });

  it('shows a form viewer only the field catalog', () => {
    const page = create({ permissions: [PERMISSIONS.viewForms] });

    expect(page.visibleTabs().map((t) => t.key)).toEqual(['field-catalog']);
  });

  it('shows an administrator every tab, org lookups first', () => {
    const page = create({ roles: [ADMINISTRATOR_ROLE] });

    const keys = page.visibleTabs().map((t) => t.key);
    expect(keys[0]).toBe('departments');
    expect(keys).toContain('task-types');
    expect(keys).toContain('c2m-actions');
    expect(keys).toContain('field-catalog');
  });

  it('opens the tab an old address asked for', () => {
    expect(create({ roles: [ADMINISTRATOR_ROLE], tab: 'c2m-actions' }).activeKey()).toBe('c2m-actions');
  });

  it('keeps workflow field activity types available with their department parent', () => {
    const page = create({ permissions: [PERMISSIONS.manageLookups], tab: 'field-activity-types' });

    expect(page.activeKey()).toBe('field-activity-types');
    expect(page.currentTab()).toEqual(jasmine.objectContaining({
      type: 'FieldActivityType', parentType: 'Department',
    }));
    expect(page.visibleTabs().map((tab) => tab.key)).not.toContain('task-types');
  });

  it('falls back to the first tab it may show when the asked-for one is not allowed', () => {
    expect(create({ permissions: [PERMISSIONS.viewForms], tab: 'task-types' }).activeKey()).toBe('field-catalog');
  });

  it('keeps the chosen tab in the address', () => {
    const page = create({ roles: [ADMINISTRATOR_ROLE] });
    page.onTabChange('field-catalog');

    const router = TestBed.inject(Router) as unknown as { navigate: jasmine.Spy };
    expect(page.activeKey()).toBe('field-catalog');
    expect(router.navigate.calls.mostRecent().args[1]).toEqual(
      jasmine.objectContaining({ queryParams: { tab: 'field-catalog' }, replaceUrl: true }),
    );
  });
});
