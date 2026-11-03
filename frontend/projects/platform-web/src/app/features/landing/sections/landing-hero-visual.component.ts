import { Component } from '@angular/core';
import { I18nDirective, NWFM_LOGO_MARK } from '@platform/shared';
import { LandingHeroTaskComponent } from './landing-hero-task.component';

type Tone = 'success' | 'brand' | 'warn' | 'danger';
type StepState = 'done' | 'current' | 'pending';

/**
 * The product shot under the hero: the Instances screen in a browser frame, following one request
 * through its workflow live, with the assignee's task card floating beside it. Drawn in markup
 * rather than a screenshot so it follows the theme and language and never goes stale against the
 * real UI's colours.
 */
@Component({
  selector: 'app-landing-hero-visual',
  imports: [I18nDirective, LandingHeroTaskComponent],
  templateUrl: './landing-hero-visual.component.html',
  styleUrl: './landing-hero-visual.component.scss',
})
export class LandingHeroVisualComponent {
  protected readonly logoMark = NWFM_LOGO_MARK;

  /** A slice of the tenant app's navigation, Instances selected. */
  protected readonly nav = [
    { key: 'workflows', icon: 'pi pi-sitemap', active: false },
    { key: 'instances', icon: 'pi pi-list', active: true },
    { key: 'forms', icon: 'pi pi-objects-column', active: false },
    { key: 'fieldTasks', icon: 'pi pi-briefcase', active: false },
    { key: 'teams', icon: 'pi pi-users', active: false },
    { key: 'workload', icon: 'pi pi-chart-bar', active: false },
  ];

  protected readonly kpis: { key: string; value: string; trend: string; tone: Tone }[] = [
    { key: 'open', value: '128', trend: '+12%', tone: 'success' },
    { key: 'completed', value: '96', trend: '75%', tone: 'brand' },
    { key: 'waiting', value: '18', trend: '−4', tone: 'brand' },
    { key: 'overdue', value: '4', trend: '!', tone: 'warn' },
  ];

  /** The selected instance's path through its workflow, as the live graph shows it. */
  protected readonly steps: { key: string; icon: string; state: StepState }[] = [
    { key: 'submit', icon: 'pi pi-check', state: 'done' },
    { key: 'review', icon: 'pi pi-check', state: 'done' },
    { key: 'finance', icon: 'pi pi-user', state: 'current' },
    { key: 'notify', icon: 'pi pi-envelope', state: 'pending' },
  ];

  protected readonly feed: { key: string; status: string; tone: Tone }[] = [
    { key: 'one', status: 'inProgress', tone: 'brand' },
    { key: 'two', status: 'completed', tone: 'success' },
    { key: 'three', status: 'returned', tone: 'danger' },
    { key: 'four', status: 'overdue', tone: 'warn' },
  ];
}
