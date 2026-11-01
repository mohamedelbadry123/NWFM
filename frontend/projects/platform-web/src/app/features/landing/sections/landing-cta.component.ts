import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18nDirective } from '@platform/shared';
import { ButtonModule } from 'primeng/button';
import { LANDING_SECTIONS, scrollToLandingSection } from '../landing-sections';

/** Closing call to action on the brand gradient: create a workspace, or find an existing one. */
@Component({
  selector: 'app-landing-cta',
  imports: [RouterLink, I18nDirective, ButtonModule],
  template: `
    <ng-container *appI18n="let t">
      <div class="container">
        <div class="card">
          <div class="grid" aria-hidden="true"></div>
          <h2>{{ t('landing.cta.title') }}</h2>
          <p>{{ t('landing.cta.subtitle') }}</p>
          <div class="actions">
            <p-button
              [label]="t('landing.cta.primary')"
              icon="pi pi-arrow-right"
              iconPos="right"
              size="large"
              routerLink="/signup"
              styleClass="cta-light"
            />
            <p-button
              [label]="t('landing.cta.secondary')"
              size="large"
              [text]="true"
              (onClick)="findWorkspace()"
              styleClass="cta-on-dark"
            />
          </div>
        </div>
      </div>
    </ng-container>
  `,
  styleUrl: './landing-cta.component.scss',
})
export class LandingCtaComponent {
  protected findWorkspace(): void {
    scrollToLandingSection(LANDING_SECTIONS.workspaces);
  }
}
