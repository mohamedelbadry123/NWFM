import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';

@Component({
  selector: 'app-workflow-tasks-redirect',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '',
})
export class WorkflowTasksRedirectComponent {
  constructor() {
    const router = inject(Router);
    const view = inject(ActivatedRoute).snapshot.data['view'] as string | undefined;
    void router.navigate(['/org/workflow/tasks'], {
      queryParams: { view: view ?? 'available' },
      replaceUrl: true,
    });
  }
}
