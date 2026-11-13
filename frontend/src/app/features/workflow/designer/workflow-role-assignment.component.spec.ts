import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowWorkspaceService } from '../workspace/workflow-workspace.service';
import { WorkflowRoleAssignmentComponent } from './workflow-role-assignment.component';

describe('Account role assignment', () => {
  const references = vi.fn();
  beforeEach(() => {
    references.mockReset().mockReturnValue(
      of([
        { id: 'reviewer', code: 'Reviewer', nameEn: 'Reviewer', nameAr: 'Reviewer' },
        { id: 'operator', code: 'Operator', nameEn: 'Operator', nameAr: 'Operator' },
      ]),
    );
    TestBed.configureTestingModule({
      imports: [WorkflowRoleAssignmentComponent],
      providers: [
        { provide: WorkflowWorkspaceService, useValue: { references } },
        { provide: LocaleService, useValue: { locale: () => 'en' } },
      ],
    });
  });
  it('loads existing account roles and emits multiple distinct role identities', () => {
    const fixture = TestBed.createComponent(WorkflowRoleAssignmentComponent);
    fixture.detectChanges();
    expect(references).toHaveBeenCalledWith('roles');
    const changed = vi.fn();
    fixture.componentInstance.roleIdsChange.subscribe(changed);
    fixture.componentInstance.choose(['reviewer', 'operator', 'reviewer']);
    expect(changed).toHaveBeenCalledWith(['reviewer', 'operator']);
    expect(fixture.nativeElement.textContent).toContain('any selected role');
    fixture.componentInstance.choose(null);
    expect(changed).toHaveBeenLastCalledWith([]);
  });
  it('preserves a legacy group until the user chooses replacement roles', () => {
    const fixture = TestBed.createComponent(WorkflowRoleAssignmentComponent);
    fixture.componentRef.setInput('legacyGroup', 'Existing reviewers');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Existing reviewers');
    const changed = vi.fn();
    fixture.componentInstance.roleIdsChange.subscribe(changed);
    expect(changed).not.toHaveBeenCalled();
    fixture.componentRef.setInput('readonly', true);
    fixture.detectChanges();
    fixture.componentInstance.choose(['reviewer']);
    expect(changed).not.toHaveBeenCalled();
  });
  it('flags unavailable saved roles and retries lookup failures', () => {
    references.mockReturnValueOnce(throwError(() => new Error('offline')));
    const fixture = TestBed.createComponent(WorkflowRoleAssignmentComponent);
    fixture.componentRef.setInput('roleIds', ['removed']);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Could not load');
    fixture.nativeElement.querySelector('button').click();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Some saved roles are no longer available');
    expect(fixture.componentInstance.roleIds).toEqual(['removed']);
  });
});
