import { Component, computed, effect, inject, input, model, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { catchError, finalize, forkJoin, of } from 'rxjs';

import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TableModule } from 'primeng/table';
import { TabsModule } from 'primeng/tabs';
import { TagModule } from 'primeng/tag';
import { TimelineModule } from 'primeng/timeline';
import { ButtonModule } from 'primeng/button';

import { TranslateContextDirective } from '../../../../core/i18n/translate-context.directive';
import { LocaleService } from '../../../../core/i18n/locale.service';
import { OrgNamesService } from '../../../../core/lookups/org-names.service';
import { TasksService } from '../../../../core/tasks/tasks.service';
import { TaskDetail, TaskFile, TaskFill, TaskHistoryEntry } from '../../../../core/tasks/tasks.models';
import { DynamicFormRendererComponent } from '../../../../shared/components/dynamic-form/dynamic-form-renderer.component';
import { GeoMapComponent } from '../../../../shared/components/geo-map/geo-map.component';
import type { GeoPoint } from '../../../../shared/components/geo-map/google-maps.types';
import { MediaThumbnailComponent } from '../../../../shared/components/media-viewer/media-thumbnail.component';
import { MediaViewerDialogComponent } from '../../../../shared/components/media-viewer/media-viewer-dialog.component';
import { MediaItem } from '../../../../shared/components/media-viewer/media-kind';
import {
  isOverdue,
  taskPrioritySeverity,
  taskReturnReasonSeverity,
  taskStatusSeverity,
} from '../../task-status';

/**
 * A task on its own: who holds it, where it is, what was found there. The answers are shown in the
 * form they were given in — the pinned version, read-only — because the grouping, the order and
 * the conditional sections are part of what an answer means.
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
    TableModule,
    TabsModule,
    TagModule,
    TimelineModule,
    DynamicFormRendererComponent,
    GeoMapComponent,
    MediaThumbnailComponent,
    MediaViewerDialogComponent,
  ],
  templateUrl: './task-detail-dialog.component.html',
})
export class TaskDetailDialogComponent {
  readonly visible = model.required<boolean>();
  readonly taskId = input<string | null>(null);

  private readonly tasksApi = inject(TasksService);
  private readonly locale = inject(LocaleService);
  protected readonly orgNames = inject(OrgNamesService);

  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly detail = signal<TaskDetail | null>(null);
  protected readonly timeline = signal<TaskHistoryEntry[]>([]);
  protected readonly fills = signal<TaskFill[]>([]);
  protected readonly files = signal<TaskFile[]>([]);

  /** The fill shown on the answers tab: the newest, or one picked from the records tab. */
  protected readonly shownFill = signal<TaskFill | null>(null);

  protected readonly activeTab = signal('answers');

  protected readonly viewerVisible = signal(false);
  protected readonly viewerIndex = signal(0);

  protected readonly statusSeverity = taskStatusSeverity;
  protected readonly prioritySeverity = taskPrioritySeverity;
  protected readonly returnReasonSeverity = taskReturnReasonSeverity;
  protected readonly isOverdue = isOverdue;

  protected readonly definition = computed<Record<string, unknown> | null>(() => {
    const json = this.detail()?.schemaJson;
    if (!json) {
      return null;
    }

    try {
      return JSON.parse(json) as Record<string, unknown>;
    } catch {
      return null;
    }
  });

  protected readonly point = computed<GeoPoint | null>(() => {
    const task = this.detail()?.task;
    return task ? { lat: task.latitude, lng: task.longitude, address: task.address } : null;
  });

  protected readonly mediaItems = computed<MediaItem[]>(() =>
    this.files().map((file) => ({
      fileId: file.fileId,
      name: file.fileName,
      contentType: file.contentType,
      sizeBytes: file.sizeBytes,
    })),
  );

  protected readonly typeName = computed(() => {
    const task = this.detail()?.task;
    return (this.locale.locale() === 'ar' ? task?.taskTypeNameAr : task?.taskTypeNameEn) ?? task?.taskTypeCode ?? '';
  });

  protected readonly formName = computed(() => {
    const detail = this.detail();
    return (this.locale.locale() === 'ar' ? detail?.formNameAr : detail?.formNameEn) ?? detail?.formCode ?? '';
  });

  constructor() {
    effect(() => {
      const id = this.taskId();
      if (this.visible() && id) {
        this.load(id);
      }
    });
  }

  protected showFill(fill: TaskFill): void {
    this.shownFill.set(fill);
    this.activeTab.set('answers');
  }

  protected openMedia(index: number): void {
    this.viewerIndex.set(index);
    this.viewerVisible.set(true);
  }

  private load(id: string): void {
    this.orgNames.ensureLoaded();
    this.detail.set(null);
    this.timeline.set([]);
    this.fills.set([]);
    this.files.set([]);
    this.shownFill.set(null);
    this.activeTab.set('answers');
    this.loadFailed.set(false);
    this.loading.set(true);

    // The side panels are not worth failing the dialog over: each falls back to empty on its own.
    forkJoin({
      detail: this.tasksApi.get(id),
      timeline: this.tasksApi.timeline(id).pipe(catchError(() => of(null))),
      fills: this.tasksApi.fills(id).pipe(catchError(() => of(null))),
      files: this.tasksApi.files(id).pipe(catchError(() => of(null))),
    })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: ({ detail, timeline, fills, files }) => {
          this.detail.set(detail.value ?? null);
          this.timeline.set(timeline?.value ?? []);
          this.fills.set(fills?.value ?? []);
          this.files.set(files?.value ?? []);
          this.shownFill.set(fills?.value?.[0] ?? null);
        },
        error: () => this.loadFailed.set(true),
      });
  }
}
