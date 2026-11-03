import { Component } from '@angular/core';
import { I18nDirective } from '@platform/shared';
import { DesignerPreviewComponent } from './previews/designer-preview.component';
import { FormPreviewComponent } from './previews/form-preview.component';
import { TasksPreviewComponent } from './previews/tasks-preview.component';
import { MonitorPreviewComponent } from './previews/monitor-preview.component';
import { PermissionsPreviewComponent } from './previews/permissions-preview.component';
import { SlaPreviewComponent } from './previews/sla-preview.component';
import { IntegrationsPreviewComponent } from './previews/integrations-preview.component';

/**
 * The feature bento: one tile per capability, each with a small drawing of the screen it refers
 * to. Every capability shown here exists in the tenant app — keep it that way when editing.
 */
@Component({
  selector: 'app-landing-features',
  imports: [
    I18nDirective,
    DesignerPreviewComponent,
    FormPreviewComponent,
    TasksPreviewComponent,
    MonitorPreviewComponent,
    PermissionsPreviewComponent,
    SlaPreviewComponent,
    IntegrationsPreviewComponent,
  ],
  templateUrl: './landing-features.component.html',
  styleUrl: './landing-features.component.scss',
})
export class LandingFeaturesComponent {}
