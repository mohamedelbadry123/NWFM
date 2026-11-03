import { Component } from '@angular/core';
import { I18nDirective } from '@platform/shared';

/**
 * The assignee's side of the hero: the open task with its form and the Accept / Reject actions,
 * drawn as a card that floats beside the browser window.
 */
@Component({
  selector: 'app-landing-hero-task',
  imports: [I18nDirective],
  templateUrl: './landing-hero-task.component.html',
  styleUrl: './landing-hero-task.component.scss',
})
export class LandingHeroTaskComponent {}
