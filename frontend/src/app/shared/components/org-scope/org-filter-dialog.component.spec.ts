import { ComponentFixture, TestBed } from '@angular/core/testing';

import { OrgFilterDialogComponent } from './org-filter-dialog.component';
import { EMPTY_ORG_LOCATION, OrgLocation } from './org-scope.model';

/**
 * The filter is staged: the cascade reports each level as it is picked, and only Apply lets the
 * result out. The template is stubbed — this is about what leaves the dialog and when, not about
 * the picker, which has its own lookups behind it.
 */
describe('OrgFilterDialogComponent', () => {
  let fixture: ComponentFixture<OrgFilterDialogComponent>;
  let dialog: OrgFilterDialogComponent;
  let emitted: OrgLocation[];

  const applied: OrgLocation = { clusterCode: 'CENTRAL', cbuCode: 'RCBU', branchCode: null, operationAreaCode: null };
  const narrower: OrgLocation = { clusterCode: 'CENTRAL', cbuCode: 'RCBU', branchCode: 'R-16', operationAreaCode: null };

  beforeEach(() => {
    TestBed.overrideComponent(OrgFilterDialogComponent, { set: { template: '', imports: [] } });

    fixture = TestBed.createComponent(OrgFilterDialogComponent);
    dialog = fixture.componentInstance;
    fixture.componentRef.setInput('location', applied);

    emitted = [];
    dialog.applied.subscribe((location) => emitted.push(location));
  });

  function open(): void {
    dialog.visible.set(true);
    dialog['onShow']();
  }

  it('opens on the filter already in force', () => {
    open();

    expect(dialog['draft']()).toEqual(applied);
    expect(dialog['seed']()).toEqual(applied);
    expect(dialog['canApply']()).toBeTrue();
  });

  it('gives the picker a fresh seed on every open, so it rebuilds its cascade', () => {
    open();
    const first = dialog['seed']();
    open();

    expect(dialog['seed']()).not.toBe(first);
    expect(dialog['seed']()).toEqual(first);
  });

  it('holds each picked level until Apply, then hands over the finished location once', () => {
    open();
    dialog['onDraftChange']({ ...applied, branchCode: 'R-16' });
    dialog['onDraftChange'](narrower);

    expect(emitted).toEqual([]);

    dialog['apply']();

    expect(emitted).toEqual([narrower]);
    expect(dialog.visible()).toBeFalse();
  });

  it('drops the draft on Cancel, and reopens on what is applied', () => {
    open();
    dialog['onDraftChange'](narrower);
    dialog['cancel']();

    expect(emitted).toEqual([]);
    expect(dialog.visible()).toBeFalse();

    open();
    expect(dialog['draft']()).toEqual(applied);
  });

  it('clears the cascade on Reset but stays open for a new pick', () => {
    open();
    dialog['reset']();

    expect(dialog['draft']()).toEqual(EMPTY_ORG_LOCATION);
    expect(dialog['seed']()).toEqual(EMPTY_ORG_LOCATION);
    expect(dialog['canApply']()).toBeFalse();
    expect(dialog.visible()).toBeTrue();
    expect(emitted).toEqual([]);
  });

  it('applies an emptied filter, which clears it on the list', () => {
    open();
    dialog['reset']();
    dialog['apply']();

    expect(emitted).toEqual([EMPTY_ORG_LOCATION]);
  });
});
