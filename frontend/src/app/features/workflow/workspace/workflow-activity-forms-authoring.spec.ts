import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowActivityFormsComponent } from './workflow-activity-forms.component';
import { ActivityForm, WorkflowActivityFormsService } from './workflow-activity-forms.service';

const form = (code: string, status = 'PUBLISHED'): ActivityForm => ({
  id: code,
  code,
  nameEn: code + ' EN',
  nameAr: code + ' AR',
  category: 'INSPECTION',
  status,
  departmentCode: '10',
  fieldActivityCode: 'LEAK',
  currentVersionNo: 1,
  versionNos: [1],
  isUsable: status === 'PUBLISHED',
  updatedAt: '2026-10-01T00:00:00Z',
});

describe('Step form picker: creating and editing forms', () => {
  let list: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    list = vi.fn().mockReturnValue(
      of({
        items: [form('A'), form('OLD', 'DEPRECATED')],
        totalCount: 2,
        usableCount: 1,
        pageNumber: 1,
        pageSize: 20,
      }),
    );
    TestBed.configureTestingModule({
      imports: [WorkflowActivityFormsComponent],
      providers: [
        { provide: LocaleService, useValue: { locale: () => 'en' } },
        { provide: WorkflowActivityFormsService, useValue: { listForTaskType: list } },
      ],
    });
  });

  function render(inputs: { canAuthor?: boolean; readonly?: boolean; refreshToken?: number } = {}) {
    const fixture = TestBed.createComponent(WorkflowActivityFormsComponent);
    fixture.componentRef.setInput('configuration', JSON.stringify({ taskTypeId: 'T' }));
    fixture.componentRef.setInput('singleForm', true);
    for (const [key, value] of Object.entries(inputs)) fixture.componentRef.setInput(key, value);
    fixture.detectChanges();
    const buttons = (text: string) =>
      Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('button')).filter(
        (b) => b.textContent?.trim() === text,
      );
    return { fixture, buttons };
  }

  it('offers Create form, Edit shared form and Duplicate for this Step to users who may manage forms and Task Types', () => {
    const { fixture, buttons } = render({ canAuthor: true });
    const requests: unknown[] = [];
    fixture.componentInstance.author.subscribe((request) => requests.push(request));
    buttons('Create form')[0].click();
    buttons('Edit shared form')[0].click();
    buttons('Duplicate for this Step')[1].click();
    expect(requests).toEqual([
      { mode: 'create' },
      { mode: 'edit', form: expect.objectContaining({ id: 'A' }) },
      { mode: 'duplicate', form: expect.objectContaining({ id: 'OLD' }) },
    ]);
  });

  it('cannot edit a deprecated form, only copy it', () => {
    const { buttons } = render({ canAuthor: true });
    expect(buttons('Edit shared form').map((b) => b.disabled)).toEqual([false, true]);
    expect(buttons('Duplicate for this Step').map((b) => b.disabled)).toEqual([false, false]);
  });

  it('shows none of it without both permissions, or on a published version', () => {
    for (const inputs of [{ canAuthor: false }, { canAuthor: true, readonly: true }]) {
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        imports: [WorkflowActivityFormsComponent],
        providers: [
          { provide: LocaleService, useValue: { locale: () => 'en' } },
          { provide: WorkflowActivityFormsService, useValue: { listForTaskType: list } },
        ],
      });
      const { buttons } = render(inputs);
      expect(buttons('Create form')).toHaveLength(0);
      expect(buttons('Edit shared form')).toHaveLength(0);
      expect(buttons('Duplicate for this Step')).toHaveLength(0);
    }
  });

  it('reads the Task Type forms again only when its refresh token changes', () => {
    const { fixture } = render({ canAuthor: true });
    expect(list).toHaveBeenCalledTimes(1);
    fixture.componentRef.setInput(
      'configuration',
      JSON.stringify({ taskTypeId: 'T', formSteps: [] }),
    );
    fixture.detectChanges();
    expect(list).toHaveBeenCalledTimes(1);
    fixture.componentRef.setInput('refreshToken', 1);
    fixture.detectChanges();
    expect(list).toHaveBeenCalledTimes(2);
  });
});
