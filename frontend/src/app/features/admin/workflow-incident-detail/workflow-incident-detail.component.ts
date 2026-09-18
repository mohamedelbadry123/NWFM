import {
  ChangeDetectionStrategy,
  Component,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { WorkflowIncidentsService } from '../workflow-incidents/workflow-incidents.service';
import type { WorkflowIncidentDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-incident-dto';
import { PrvStatusPillComponent, type PrvTone } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../../workflow/ui/workflow-page-header.component';

@Component({
  selector: 'app-workflow-incident-detail',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, TranslatePipe, PrvStatusPillComponent, WorkflowPageHeaderComponent],
  templateUrl: './workflow-incident-detail.component.html',
})
export class WorkflowIncidentDetailComponent implements OnInit {
  private readonly service = inject(WorkflowIncidentsService);
  private readonly route = inject(ActivatedRoute);
  protected readonly router = inject(Router);

  protected readonly incident = signal<WorkflowIncidentDto | null>(null);
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);

  protected severityTone(severity: string | undefined): PrvTone {
    switch (severity) {
      case 'Critical': return 'danger';
      case 'High':     return 'warning';
      case 'Medium':   return 'info';
      default:         return 'neutral';
    }
  }

  protected statusTone(status: string | undefined): PrvTone {
    switch (status) {
      case 'Resolved':
      case 'Closed':
        return 'success';
      case 'Open':
      case 'Investigating':
        return 'warning';
      default:
        return 'neutral';
    }
  }

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id')!;
    this.service.getById(id).subscribe({
      next: inc => {
        this.incident.set(inc);
        this.isLoading.set(false);
      },
      error: (err: unknown) => {
        const e = err as { error?: { detail?: string; title?: string } };
        this.loadError.set(e?.error?.detail ?? e?.error?.title ?? 'Incident not found.');
        this.isLoading.set(false);
      },
    });
  }
}
