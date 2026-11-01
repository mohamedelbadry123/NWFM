import { Component } from '@angular/core';
import { I18nDirective } from '@platform/shared';

/** Frequently asked questions as native disclosure widgets — keyboard and screen-reader friendly without script. */
@Component({
  selector: 'app-landing-faq',
  imports: [I18nDirective],
  template: `
    <ng-container *appI18n="let t">
      <div class="layout">
        <header class="section-head">
          <span class="eyebrow">{{ t('landing.faq.eyebrow') }}</span>
          <h2>{{ t('landing.faq.title') }}</h2>
          <p>{{ t('landing.faq.subtitle') }}</p>
        </header>
        <div class="list">
          @for (item of items; track item.q) {
            <details class="item">
              <summary>
                <span>{{ t(item.q) }}</span>
                <i class="pi pi-plus"></i>
              </summary>
              <p>{{ t(item.a) }}</p>
            </details>
          }
        </div>
      </div>
    </ng-container>
  `,
  styleUrl: './landing-faq.component.scss',
})
export class LandingFaqComponent {
  protected readonly items = [
    { q: 'landing.faq.q1', a: 'landing.faq.a1' },
    { q: 'landing.faq.q2', a: 'landing.faq.a2' },
    { q: 'landing.faq.q3', a: 'landing.faq.a3' },
    { q: 'landing.faq.q4', a: 'landing.faq.a4' },
    { q: 'landing.faq.q5', a: 'landing.faq.a5' },
    { q: 'landing.faq.q6', a: 'landing.faq.a6' },
    { q: 'landing.faq.q7', a: 'landing.faq.a7' },
  ];
}
