import { ChangeDetectionStrategy, Component } from '@angular/core';
import { WorkflowHubComponent } from '../../workflow/hub/workflow-hub.component';

@Component({
  selector: 'app-admin-workflow-hub-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [WorkflowHubComponent],
  template: `<app-workflow-hub audience="admin" />`,
})
export class AdminWorkflowHubPageComponent {}
