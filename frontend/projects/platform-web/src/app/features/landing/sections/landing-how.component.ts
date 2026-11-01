import { Component } from '@angular/core';
import { I18nDirective } from '@platform/shared';

/** The three steps: design, assign, monitor. */
@Component({
  selector: 'app-landing-how',
  imports: [I18nDirective],
  template: `
    <ng-container *appI18n="let t">
      <div class="container">
        <header class="section-head center">
          <span class="eyebrow">{{ t('landing.how.eyebrow') }}</span>
          <h2>{{ t('landing.how.title') }}</h2>
          <p>{{ t('landing.how.subtitle') }}</p>
        </header>

        <ol class="steps">
          @for (step of steps; track step.titleKey; let i = $index) {
            <li class="step">
              <div class="step-top">
                <span class="step-icon"><i [class]="step.icon"></i></span>
                <span class="step-index">{{ t('landing.how.stepLabel', { n: i + 1 }) }}</span>
              </div>
              <h3>{{ t(step.titleKey) }}</h3>
              <p>{{ t(step.textKey) }}</p>
            </li>
          }
        </ol>
      </div>
    </ng-container>
  `,
  styleUrl: './landing-how.component.scss',
})
export class LandingHowComponent {
  protected readonly steps = [
    {
      icon: 'pi pi-sitemap',
      titleKey: 'landing.how.step1Title',
      textKey: 'landing.how.step1Text',
    },
    { icon: 'pi pi-users', titleKey: 'landing.how.step2Title', textKey: 'landing.how.step2Text' },
    {
      icon: 'pi pi-chart-line',
      titleKey: 'landing.how.step3Title',
      textKey: 'landing.how.step3Text',
    },
  ];
}
