import { Component, computed, effect, inject, input, model, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslateService } from '@ngx-translate/core';
import { catchError, finalize, forkJoin, of } from 'rxjs';

import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TabsModule } from 'primeng/tabs';
import { TagModule } from 'primeng/tag';
import { TimelineModule } from 'primeng/timeline';
import { TooltipModule } from 'primeng/tooltip';

import { TranslateContextDirective } from '../../../../core/i18n/translate-context.directive';
import { LocaleService } from '../../../../core/i18n/locale.service';
import { MediaObjectUrlService } from '../../../../core/form-engine/media-object-url.service';
import { OrgNamesService } from '../../../../core/lookups/org-names.service';
import { TasksService } from '../../../../core/tasks/tasks.service';
import { C2mDispatchLog, TaskAnswerView, TaskDetail, TaskFile, TaskFill, TaskHistoryEntry } from '../../../../core/tasks/tasks.models';
import { AuthStore } from '../../../../core/auth/auth.store';
import { ADMINISTRATOR_ROLE, PERMISSIONS } from '../../../../core/auth/permissions';
import { apiErrorMessage } from '../../../../core/api/api-error-message';
import { GeoMapComponent } from '../../../../shared/components/geo-map/geo-map.component';
import type { GeoPoint } from '../../../../shared/components/geo-map/google-maps.types';
import { MediaThumbnailComponent } from '../../../../shared/components/media-viewer/media-thumbnail.component';
import { MediaViewerDialogComponent } from '../../../../shared/components/media-viewer/media-viewer-dialog.component';
import { MediaItem } from '../../../../shared/components/media-viewer/media-kind';
import {
  c2mStatusSeverity,
  canRetryC2m,
  isOverdue,
  taskPrioritySeverity,
  taskReturnReasonSeverity,
  taskStatusSeverity,
  wasSubmittedAgain,
} from '../../task-status';
import { TaskExportService } from '../../task-export.service';
import { TaskAnswerMapDialogComponent } from './task-answer-map-dialog.component';
import { TaskPreviewDialogComponent } from './task-preview-dialog.component';

/** One rendered answer row: how it reads, and — for a geolocation answer — where it points. */
export interface AnswerEntry {
  readonly label: string;
  readonly value: string;
  readonly point: GeoPoint | null;
}

/** One tile in the Files gallery: what the viewer needs, plus the facts shown under it. */
interface FileCard {
  readonly item: MediaItem;
  readonly created: string;
  readonly dataName: string;
}

/**
 * Read-only task inspector, laid out like the reference survey details: an identity banner with
 * the report actions, the facts grouped by subject, why it came back (if it did), the latest fill as
 * label/value pairs, then tabs for the status trail, earlier fills, assignments, files and the map.
 */
@Component({
  selector: 'app-task-detail-dialog',
  standalone: true,
  imports: [
    CommonModule,
    TranslateContextDirective,
    ButtonModule,
    DialogModule,
    MessageModule,
    ProgressSpinnerModule,
    TabsModule,
    TagModule,
    TimelineModule,
    TooltipModule,
    GeoMapComponent,
    MediaThumbnailComponent,
    MediaViewerDialogComponent,
    TaskAnswerMapDialogComponent,
    TaskPreviewDialogComponent,
  ],
  templateUrl: './task-detail-dialog.component.html',
})
export class TaskDetailDialogComponent {
  readonly visible = model.required<boolean>();
  readonly taskId = input<string | null>(null);

  private readonly tasksApi = inject(TasksService);
  private readonly exporter = inject(TaskExportService);
  private readonly media = inject(MediaObjectUrlService);
  private readonly messageService = inject(MessageService);
  private readonly translate = inject(TranslateService);
  private readonly locale = inject(LocaleService);
  protected readonly orgNames = inject(OrgNamesService);
  private readonly authStore = inject(AuthStore);

  /** Drives the direction-sensitive bits the CSS cannot reach, such as the status-trail arrow. */
  protected readonly isRtl = this.locale.isRtl;

  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly detail = signal<TaskDetail | null>(null);
  protected readonly timeline = signal<TaskHistoryEntry[]>([]);
  protected readonly fills = signal<TaskFill[]>([]);
  protected readonly files = signal<TaskFile[]>([]);
  protected readonly c2mLogs = signal<C2mDispatchLog[]>([]);
  protected readonly retryingC2m = signal(false);

  /** The attempt whose request/response JSON is unfolded. */
  protected readonly openLogId = signal<string | null>(null);
  protected readonly exporting = signal(false);

  protected readonly statusSeverity = taskStatusSeverity;
  protected readonly prioritySeverity = taskPrioritySeverity;
  protected readonly returnReasonSeverity = taskReturnReasonSeverity;
  protected readonly wasSubmittedAgain = wasSubmittedAgain;
  protected readonly isOverdue = isOverdue;
  protected readonly c2mSeverity = c2mStatusSeverity;

  /** The C2M tab is shown for any task that names a field activity or has tried to close one. */
  protected readonly showC2m = computed(() => {
    const d = this.detail();
    return !!d && (!!d.task.faId || !!d.task.c2mStatus || this.c2mLogs().length > 0);
  });

  protected readonly canRetry = computed(() => {
    const task = this.detail()?.task;
    return !!task
      && canRetryC2m(task)
      && (this.authStore.hasAnyPermission(PERMISSIONS.reviewTasks) || this.authStore.roles().includes(ADMINISTRATOR_ROLE));
  });

  protected readonly viewerVisible = signal(false);
  protected readonly viewerIndex = signal(0);

  /** The read-only form; `previewSubmissionId` null means the newest fill. */
  protected readonly previewVisible = signal(false);
  protected readonly previewSubmissionId = signal<string | null>(null);

  protected readonly answerMapVisible = signal(false);
  protected readonly answerMapPoint = signal<GeoPoint | null>(null);
  protected readonly answerMapLabel = signal('');

  /** File ids whose download is in flight, so only that tile's button spins. */
  private readonly downloading = signal<ReadonlySet<string>>(new Set());

  /** The fills come newest first; the first is what the task currently holds. */
  protected readonly latestFill = computed<TaskFill | null>(() => this.fills()[0] ?? null);

  /** Older fills, kept on the Records tab — the latest sits above the tabs. */
  protected readonly earlierFills = computed<TaskFill[]>(() => this.fills().slice(1));

  protected readonly fileCards = computed<FileCard[]>(() =>
    this.files().map((file) => ({
      item: { fileId: file.fileId, name: file.fileName, contentType: file.contentType, sizeBytes: file.sizeBytes },
      created: file.createdAt,
      dataName: file.dataName,
    })),
  );

  /** The gallery order the viewer steps through — the same list the tiles render. */
  protected readonly viewerItems = computed<MediaItem[]>(() => this.fileCards().map((card) => card.item));

  protected readonly locationPoint = computed<GeoPoint | null>(() => {
    const task = this.detail()?.task;
    return task ? { lat: task.latitude, lng: task.longitude, address: task.address } : null;
  });

  protected readonly typeName = computed(() => {
    const task = this.detail()?.task;
    const name = this.localized(task?.taskTypeNameEn, task?.taskTypeNameAr);
    return task?.taskTypeCode ? `${task.taskTypeCode} — ${name}` : name || '—';
  });

  protected readonly formName = computed(() => {
    const d = this.detail();
    const name = this.localized(d?.formNameEn, d?.formNameAr);
    return d?.formCode ? `${d.formCode} — ${name}` : name || '—';
  });

  constructor() {
    effect(() => {
      const id = this.taskId();
      if (this.visible() && id) {
        this.load(id);
      }
    });
  }

  /** A fill's answers as `label: value` rows in the reader's language, straight from the server's rendering. */
  protected answerEntries(fill: TaskFill): AnswerEntry[] {
    return (fill.display ?? []).map((answer: TaskAnswerView) => ({
      label: this.localized(answer.labelEn, answer.labelAr) || answer.dataName,
      value: this.localized(answer.displayEn, answer.displayAr) || '—',
      point: answer.point
        ? { lat: answer.point.latitude, lng: answer.point.longitude, address: answer.point.address }
        : null,
    }));
  }

  protected openPreview(submissionId: string | null = null): void {
    this.previewSubmissionId.set(submissionId);
    this.previewVisible.set(true);
  }

  protected toggleLog(id: string): void {
    this.openLogId.update((current) => (current === id ? null : id));
  }

  /** Pretty JSON for a stored request or response; the raw text when it is not JSON. */
  protected prettyJson(json: string | null): string {
    if (!json) {
      return '—';
    }

    try {
      return JSON.stringify(JSON.parse(json), null, 2);
    } catch {
      return json;
    }
  }

  /** Sends a refused or failed closure again, then reloads the task so its status and the attempts show. */
  protected retryC2m(): void {
    const task = this.detail()?.task;
    if (!task || this.retryingC2m()) {
      return;
    }

    this.retryingC2m.set(true);
    this.tasksApi
      .retryC2m(task.id)
      .pipe(finalize(() => this.retryingC2m.set(false)))
      .subscribe({
        next: (res) => {
          const closed = res.value?.c2mStatus === 'CLOSED';
          this.messageService.add({
            severity: closed ? 'success' : 'warn',
            summary: this.translate.instant(closed ? 'common.success' : 'common.warning'),
            detail: closed
              ? this.translate.instant('tasks.c2m.retryClosed')
              : this.translate.instant('tasks.c2m.retryNotClosed', { message: res.value?.message ?? '' }),
            life: 8000,
          });
          this.load(task.id);
        },
        error: (error: unknown) =>
          this.messageService.add({
            severity: 'error',
            summary: this.translate.instant('common.error'),
            detail: apiErrorMessage(error, this.translate),
          }),
      });
  }

  protected exportPdf(): void {
    const task = this.detail()?.task;
    if (!task || this.exporting()) {
      return;
    }

    this.exporting.set(true);
    this.exporter
      .exportPdf(task)
      .pipe(finalize(() => this.exporting.set(false)))
      .subscribe({
        error: () =>
          this.messageService.add({
            severity: 'error',
            summary: this.translate.instant('common.error'),
            detail: this.translate.instant('tasks.messages.exportFailed'),
          }),
      });
  }

  protected openAnswerMap(entry: AnswerEntry): void {
    if (!entry.point) {
      return;
    }

    this.answerMapPoint.set(entry.point);
    this.answerMapLabel.set(entry.label);
    this.answerMapVisible.set(true);
  }

  protected openViewer(index: number): void {
    this.viewerIndex.set(index);
    this.viewerVisible.set(true);
  }

  protected isDownloading(item: MediaItem): boolean {
    return !!item.fileId && this.downloading().has(item.fileId);
  }

  protected downloadFile(item: MediaItem): void {
    const fileId = item.fileId;
    if (!fileId || this.isDownloading(item)) {
      return;
    }

    this.setDownloading(fileId, true);
    this.media
      .download(fileId, item.name)
      .pipe(finalize(() => this.setDownloading(fileId, false)))
      .subscribe({
        // The tile stays put; the same button retries.
        error: () => undefined,
      });
  }

  protected formatBytes(bytes: number): string {
    if (!bytes) {
      return '0 B';
    }

    const units = ['B', 'KB', 'MB', 'GB'];
    const exponent = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
    return `${parseFloat((bytes / Math.pow(1024, exponent)).toFixed(1))} ${units[exponent]}`;
  }

  private localized(english?: string | null, arabic?: string | null): string {
    const preferred = this.locale.locale() === 'ar' ? arabic : english;
    return preferred?.trim() || english?.trim() || arabic?.trim() || '';
  }

  private setDownloading(fileId: string, busy: boolean): void {
    const next = new Set(this.downloading());
    if (busy) {
      next.add(fileId);
    } else {
      next.delete(fileId);
    }
    this.downloading.set(next);
  }

  private load(id: string): void {
    this.orgNames.ensureLoaded();
    this.detail.set(null);
    this.timeline.set([]);
    this.fills.set([]);
    this.files.set([]);
    this.c2mLogs.set([]);
    this.openLogId.set(null);
    this.loadFailed.set(false);
    this.loading.set(true);

    // The side panels are not worth failing the dialog over: each falls back to empty on its own.
    forkJoin({
      detail: this.tasksApi.get(id),
      timeline: this.tasksApi.timeline(id).pipe(catchError(() => of(null))),
      fills: this.tasksApi.fills(id).pipe(catchError(() => of(null))),
      files: this.tasksApi.files(id).pipe(catchError(() => of(null))),
      c2mLogs: this.tasksApi.c2mLogs(id).pipe(catchError(() => of(null))),
    })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: ({ detail, timeline, fills, files, c2mLogs }) => {
          this.detail.set(detail.value ?? null);
          this.timeline.set(timeline?.value ?? []);
          this.fills.set(fills?.value ?? []);
          this.files.set(files?.value ?? []);
          this.c2mLogs.set(c2mLogs?.value ?? []);
        },
        error: () => this.loadFailed.set(true),
      });
  }
}
