import { ChangeDetectionStrategy, Component } from '@angular/core';
import { WorkflowHubComponent } from './workflow-hub.component';

@Component({
  selector: 'app-org-workflow-hub-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [WorkflowHubComponent],
  template: `<app-workflow-hub audience="org" />`,
})
export class OrgWorkflowHubPageComponent {}
