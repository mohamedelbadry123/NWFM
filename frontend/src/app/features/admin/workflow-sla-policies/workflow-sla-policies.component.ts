import {
  ChangeDetectionStrategy,
  Component,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { WorkflowSlaPoliciesService } from './workflow-sla-policies.service';
import { WorkflowCalendarsService } from '../workflow-calendars/workflow-calendars.service';
import type { SlaPolicyDto } from '@shared/models/models/Workflow/Application/DTOs/sla-policy-dto';
import type { BusinessCalendarDto } from '@shared/models/models/Workflow/Application/DTOs/business-calendar-dto';
import type { SlaDurationUnit } from '@shared/models/models/Workflow/Domain/Enums/sla-duration-unit';
import { ToastService } from '@core/notifications/toast.service';
import { PrvEmptyStateComponent } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../../workflow/ui/workflow-page-header.component';
import { WorkflowTableShellComponent } from '../../workflow/ui/workflow-table-shell.component';

@Component({
  selector: 'app-workflow-sla-policies',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslatePipe,
    PrvEmptyStateComponent,
    WorkflowPageHeaderComponent,
    WorkflowTableShellComponent,
  ],
  templateUrl: './workflow-sla-policies.component.html',
})
export class WorkflowSlaPoliciesComponent implements OnInit {
  private readonly service = inject(WorkflowSlaPoliciesService);
  private readonly calendarsService = inject(WorkflowCalendarsService);
  private readonly toast = inject(ToastService);
  private readonly fb = inject(FormBuilder);

  protected readonly items = signal<SlaPolicyDto[]>([]);
  protected readonly calendars = signal<BusinessCalendarDto[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly showCreate = signal(false);
  protected readonly saving = signal(false);

  protected readonly durationUnits: SlaDurationUnit[] = [
    'Minutes', 'Hours', 'BusinessHours', 'Days', 'BusinessDays',
  ];

  protected readonly form = this.fb.group({
    policyCode: ['', [Validators.required, Validators.maxLength(100)]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    duration: [8, [Validators.required, Validators.min(1)]],
    durationUnit: ['Hours' as SlaDurationUnit, Validators.required],
    calendarId: ['', Validators.required],
  });

  ngOnInit(): void {
    this.load();
    this.calendarsService.list().subscribe({
      next: c => this.calendars.set(c ?? []),
    });
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
        this.loadError.set(e?.error?.detail ?? e?.error?.title ?? 'Failed to load SLA policies.');
        this.isLoading.set(false);
      },
    });
  }

  protected openCreate(): void {
    this.form.reset({
      policyCode: '',
      name: '',
      duration: 8,
      durationUnit: 'Hours',
      calendarId: this.calendars()[0]?.id ?? '',
    });
    this.showCreate.set(true);
  }

  protected closeCreate(): void {
    this.showCreate.set(false);
  }

  protected calendarName(id: string | undefined): string {
    if (!id) return '—';
    return this.calendars().find(c => c.id === id)?.name ?? id.slice(0, 8);
  }

  protected submit(): void {
    if (this.form.invalid || this.saving()) return;
    const v = this.form.getRawValue();
    this.saving.set(true);
    this.service.create({
      policyCode: v.policyCode!.trim(),
      name: v.name!.trim(),
      duration: Number(v.duration),
      durationUnit: v.durationUnit as SlaDurationUnit,
      businessCalendarId: v.calendarId!,
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.showCreate.set(false);
        this.toast.success('SLA policy created.');
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
