import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import { LocaleService } from '../../core/i18n/locale.service';
import { saveFileResponse } from '../../core/api/save-file-response';
import { TasksService } from '../../core/tasks/tasks.service';

/**
 * Downloads a task's PDF report in the reader's language. Shared by the worklist's row menu and the
 * details dialog so both name and save the file the same way.
 */
@Injectable({ providedIn: 'root' })
export class TaskExportService {
  private readonly tasksApi = inject(TasksService);
  private readonly locale = inject(LocaleService);

  exportPdf(task: { id: string; taskNumber: string }): Observable<void> {
    const language = this.locale.locale() === 'ar' ? 'ar' : 'en';

    return this.tasksApi
      .exportPdf(task.id, language)
      .pipe(map((response) => saveFileResponse(response, `Task_${task.taskNumber}.pdf`)));
  }
}
