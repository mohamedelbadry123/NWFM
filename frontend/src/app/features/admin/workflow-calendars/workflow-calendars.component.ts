import {
  ChangeDetectionStrategy,
  Component,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { WorkflowCalendarsService } from './workflow-calendars.service';
import type { BusinessCalendarDto } from '@shared/models/models/Workflow/Application/DTOs/business-calendar-dto';
import { ToastService } from '@core/notifications/toast.service';
import { PrvEmptyStateComponent } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../../workflow/ui/workflow-page-header.component';
import { WorkflowTableShellComponent } from '../../workflow/ui/workflow-table-shell.component';

@Component({
  selector: 'app-workflow-calendars',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslatePipe,
    PrvEmptyStateComponent,
    WorkflowPageHeaderComponent,
    WorkflowTableShellComponent,
  ],
  templateUrl: './workflow-calendars.component.html',
})
export class WorkflowCalendarsComponent implements OnInit {
  private readonly service = inject(WorkflowCalendarsService);
  private readonly toast = inject(ToastService);
  private readonly fb = inject(FormBuilder);

  protected readonly items = signal<BusinessCalendarDto[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly showCreate = signal(false);
  protected readonly saving = signal(false);

  protected readonly form = this.fb.group({
    code: ['', [Validators.required, Validators.maxLength(100)]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    nameAr: ['', Validators.maxLength(200)],
    timeZone: ['UTC', [Validators.required, Validators.maxLength(100)]],
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.service.list().subscribe({
      next: items => {
        this.items.set(items ?? []);
        this.isLoading.set(false);
      },
      error: (err: unknown) => {
        const e = err as { error?: { detail?: string; title?: string } };
        this.loadError.set(e?.error?.detail ?? e?.error?.title ?? 'Failed to load calendars.');
        this.isLoading.set(false);
      },
    });
  }

  protected openCreate(): void {
    this.form.reset({ code: '', name: '', nameAr: '', timeZone: 'UTC' });
    this.showCreate.set(true);
  }

  protected closeCreate(): void {
    this.showCreate.set(false);
  }

  protected submit(): void {
    if (this.form.invalid || this.saving()) return;
    const v = this.form.getRawValue();
    this.saving.set(true);
    this.service.create({
      code: v.code!.trim(),
      name: v.name!.trim(),
      nameAr: v.nameAr?.trim() || null,
      timeZone: v.timeZone!.trim(),
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.showCreate.set(false);
        this.toast.success('Calendar created.');
        this.load();
      },
      error: (err: unknown) => {
        const e = err as { error?: { detail?: string; title?: string } };
        this.toast.error(e?.error?.detail ?? e?.error?.title ?? 'Create failed.');
        this.saving.set(false);
      },
    });
  }
}
