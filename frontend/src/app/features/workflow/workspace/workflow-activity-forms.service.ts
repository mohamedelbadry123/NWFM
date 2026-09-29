import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

/** A Form Engine form filed under an activity's Department + FA Type. Unusable ones (never published, deprecated, archived) are listed too, flagged. */
export interface ActivityForm {
  id: string; code: string; nameEn: string; nameAr: string; category: string; status: string;
  departmentCode: string; fieldActivityCode: string;
  currentVersionNo?: number | null;
  /** Every published version, newest first. */
  versionNos: number[];
  isUsable: boolean;
  updatedAt: string;
}
export interface ActivityFormPage { items: ActivityForm[]; totalCount: number; usableCount: number; pageNumber: number; pageSize: number }
export interface ActivityFormPreview { formId: string; code: string; nameEn: string; nameAr: string; status: string; versionNo: number; isUsable: boolean; schemaJson: string }

/** The designer's Form tab. Read-only: the server filters by both codes and re-checks them for a preview. */
@Injectable({ providedIn: 'root' })
export class WorkflowActivityFormsService {
  static readonly PAGE_SIZE = 20;
  private readonly http = inject(HttpClient);
  private readonly base = '/api/workflow/workspace/activity-forms';
  list(departmentCode: string, fieldActivityCode: string, pageNumber = 1, pageSize = WorkflowActivityFormsService.PAGE_SIZE) {
    return this.http.get<ActivityFormPage>(this.base, { params: { departmentCode, fieldActivityCode, pageNumber, pageSize } });
  }
  preview(formId: string, versionNo: number, departmentCode: string, fieldActivityCode: string) {
    return this.http.get<ActivityFormPreview>(`${this.base}/${formId}/versions/${versionNo}`, { params: { departmentCode, fieldActivityCode } });
  }
}
