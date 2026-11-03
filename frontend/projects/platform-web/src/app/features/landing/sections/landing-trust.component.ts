import { Component } from '@angular/core';
import { I18nDirective } from '@platform/shared';

/**
 * Security and data-ownership band, dark in both themes. Each claim maps to something the
 * platform does: a database per tenant, bring-your-own SQL Server, SAML SSO and consent-only
 * support access (`SupportAccessGrant`).
 */
@Component({
  selector: 'app-landing-trust',
  imports: [I18nDirective],
  template: `
    <ng-container *appI18n="let t">
      <div class="panel">
        <div class="glow" aria-hidden="true"></div>
        <header class="section-head">
          <span class="eyebrow">{{ t('landing.trust.eyebrow') }}</span>
          <h2>{{ t('landing.trust.title') }}</h2>
          <p>{{ t('landing.trust.subtitle') }}</p>
        </header>
        <div class="grid">
          @for (item of items; track item.titleKey) {
            <article class="item">
              <span class="icon"><i [class]="item.icon"></i></span>
              <h3>{{ t(item.titleKey) }}</h3>
              <p>{{ t(item.textKey) }}</p>
            </article>
          }
        </div>
      </div>
    </ng-container>
  `,
  styleUrl: './landing-trust.component.scss',
})
export class LandingTrustComponent {
  protected readonly items = [
    {
      icon: 'pi pi-database',
      titleKey: 'landing.trust.databaseTitle',
      textKey: 'landing.trust.databaseText',
    },
    { icon: 'pi pi-server', titleKey: 'landing.trust.byoTitle', textKey: 'landing.trust.byoText' },
    { icon: 'pi pi-id-card', titleKey: 'landing.trust.ssoTitle', textKey: 'landing.trust.ssoText' },
    {
      icon: 'pi pi-key',
      titleKey: 'landing.trust.supportTitle',
      textKey: 'landing.trust.supportText',
    },
  ];
}
