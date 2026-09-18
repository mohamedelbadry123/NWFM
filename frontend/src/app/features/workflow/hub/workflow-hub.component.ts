import {
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { WorkflowPageHeaderComponent } from '../ui/workflow-page-header.component';
import { WorkflowKpiCardComponent } from '../ui/workflow-kpi-card.component';

interface HubLink {
  titleKey: string;
  descKey: string;
  link: string;
  tone: 'primary' | 'success' | 'warning' | 'danger' | 'neutral';
}

@Component({
  selector: 'app-workflow-hub',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    TranslateModule,
    RouterLink,
    WorkflowPageHeaderComponent,
    WorkflowKpiCardComponent,
  ],
  templateUrl: './workflow-hub.component.html',
})
export class WorkflowHubComponent {
  /** 'admin' | 'org' */
  readonly audience = input<'admin' | 'org'>('admin');

  protected readonly helpLink = computed(() =>
    this.audience() === 'admin' ? '/admin/workflow/help' : '/org/workflow/help'
  );

  protected readonly titleKey = computed(() =>
    this.audience() === 'admin' ? 'workflow.hub.admin_title' : 'workflow.hub.org_title'
  );

  protected readonly subtitleKey = computed(() =>
    this.audience() === 'admin' ? 'workflow.hub.admin_subtitle' : 'workflow.hub.org_subtitle'
  );

  protected readonly links = computed<HubLink[]>(() => {
    if (this.audience() === 'org') {
      return [
        { titleKey: 'workflow.hub.link_tasks', descKey: 'workflow.hub.link_tasks_desc', link: '/org/workflow/tasks', tone: 'primary' },
        { titleKey: 'workflow.hub.link_notifications', descKey: 'workflow.hub.link_notifications_desc', link: '/org/workflow/notifications', tone: 'success' },
        { titleKey: 'workflow.hub.link_workload', descKey: 'workflow.hub.link_workload_desc', link: '/org/workflow/workload', tone: 'warning' },
        { titleKey: 'workflow.hub.link_requests', descKey: 'workflow.hub.link_requests_desc', link: '/org/workflow/requests', tone: 'neutral' },
        { titleKey: 'workflow.hub.link_participants', descKey: 'workflow.hub.link_participants_desc', link: '/org/workflow/participants', tone: 'neutral' },
        { titleKey: 'workflow.hub.link_groups', descKey: 'workflow.hub.link_groups_desc', link: '/org/workflow/assignment-groups', tone: 'neutral' },
        { titleKey: 'workflow.hub.link_departments', descKey: 'workflow.hub.link_departments_desc', link: '/org/workflow/departments', tone: 'neutral' },
        { titleKey: 'workflow.hub.link_help', descKey: 'workflow.hub.link_help_desc', link: '/org/workflow/help', tone: 'primary' },
      ];
    }
    return [
      { titleKey: 'workflow.hub.link_definitions', descKey: 'workflow.hub.link_definitions_desc', link: '/admin/workflow/definitions', tone: 'primary' },
      { titleKey: 'workflow.hub.link_bindings', descKey: 'workflow.hub.link_bindings_desc', link: '/admin/workflow/bindings', tone: 'success' },
      { titleKey: 'workflow.hub.link_instances', descKey: 'workflow.hub.link_instances_desc', link: '/admin/workflow/instances', tone: 'neutral' },
      { titleKey: 'workflow.hub.link_dead_letters', descKey: 'workflow.hub.link_dead_letters_desc', link: '/admin/workflow/dead-letters', tone: 'danger' },
      { titleKey: 'workflow.hub.link_workload', descKey: 'workflow.hub.link_workload_desc', link: '/admin/workflow/workload', tone: 'warning' },
      { titleKey: 'workflow.hub.link_sla', descKey: 'workflow.hub.link_sla_desc', link: '/admin/workflow/sla-policies', tone: 'neutral' },
      { titleKey: 'workflow.hub.link_calendars', descKey: 'workflow.hub.link_calendars_desc', link: '/admin/workflow/calendars', tone: 'neutral' },
      { titleKey: 'workflow.hub.link_incidents', descKey: 'workflow.hub.link_incidents_desc', link: '/admin/workflow/incidents', tone: 'warning' },
      { titleKey: 'workflow.hub.link_actions', descKey: 'workflow.hub.link_actions_desc', link: '/admin/workflow/actions-catalog', tone: 'neutral' },
      { titleKey: 'workflow.hub.link_catalog', descKey: 'workflow.hub.link_catalog_desc', link: '/admin/workflow/modules', tone: 'neutral' },
      { titleKey: 'workflow.hub.link_help', descKey: 'workflow.hub.link_help_desc', link: '/admin/workflow/help', tone: 'primary' },
    ];
  });

  protected readonly pathSteps = computed(() =>
    this.audience() === 'admin' ? [1, 2, 3, 4] : [1, 2, 3]
  );

  protected toneBorder(tone: HubLink['tone']): string {
    switch (tone) {
      case 'primary': return 'hover:border-primary/50';
      case 'success': return 'hover:border-green-600/50';
      case 'warning': return 'hover:border-amber-600/50';
      case 'danger': return 'hover:border-red-600/50';
      default: return 'hover:border-ink-300 dark:hover:border-dark-500';
    }
  }

  protected toneAccent(tone: HubLink['tone']): string {
    switch (tone) {
      case 'primary': return 'bg-primary/15 text-primary-700 dark:bg-primary/20 dark:text-primary-200';
      case 'success': return 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300';
      case 'warning': return 'bg-amber-100 text-amber-800 dark:bg-amber-900/40 dark:text-amber-300';
      case 'danger': return 'bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300';
      default: return 'bg-ink-100 text-ink-600 dark:bg-dark-700 dark:text-dark-200';
    }
  }

  protected initial(titleKey: string): string {
    const last = titleKey.split('.').pop() ?? 'W';
    return last.charAt(0).toUpperCase();
  }
}
