import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18nDirective, LanguageService, ThemeService, NWFM_LOGO_MARK } from '@platform/shared';
import {
  LANDING_NAV_LINKS,
  LANDING_SECTIONS,
  LandingSectionId,
  scrollToLandingSection,
} from '../landing-sections';

/** Page footer: section links, the ways in (signup, workspace sign-in, staff console) and the display toggles. */
@Component({
  selector: 'app-landing-footer',
  imports: [RouterLink, I18nDirective],
  template: `
    <ng-container *appI18n="let t">
      <div class="container grid">
        <div class="brand-col">
          <a routerLink="/" class="brand">
            <img [src]="logoMark" alt="" />
            <span>{{ t('common.appName') }}</span>
          </a>
          <p>{{ t('landing.footer.tagline') }}</p>
        </div>

        <div class="col">
          <h4>{{ t('landing.footer.product') }}</h4>
          @for (link of links; track link.id) {
            <button type="button" class="link" (click)="go(link.id)">{{ t(link.labelKey) }}</button>
          }
        </div>

        <div class="col">
          <h4>{{ t('landing.footer.getStarted') }}</h4>
          <a routerLink="/signup" class="link">{{ t('landing.nav.cta') }}</a>
          <button type="button" class="link" (click)="go(sections.workspaces)">
            {{ t('landing.nav.signIn') }}
          </button>
        </div>

        <div class="col">
          <h4>{{ t('landing.footer.team') }}</h4>
          <a routerLink="/console/login" class="link">{{ t('landing.footer.staffSignIn') }}</a>
        </div>
      </div>

      <div class="container bottom">
        <span>{{ t('landing.footer.copyright', { year: year, app: t('common.appName') }) }}</span>
        <span class="toggles">
          <button
            type="button"
            class="icon-btn"
            (click)="language.toggle()"
            [attr.aria-label]="t('common.language')"
          >
            <i class="pi pi-globe"></i>
            <span>{{
              language.current() === 'ar' ? t('common.english') : t('common.arabic')
            }}</span>
          </button>
          <button
            type="button"
            class="icon-btn"
            (click)="theme.toggle()"
            [attr.aria-label]="t('common.toggleTheme')"
          >
            <i
              class="pi"
              [class.pi-moon]="theme.mode() === 'light'"
              [class.pi-sun]="theme.mode() === 'dark'"
            ></i>
          </button>
        </span>
      </div>
    </ng-container>
  `,
  styleUrl: './landing-footer.component.scss',
})
export class LandingFooterComponent {
  protected readonly language = inject(LanguageService);
  protected readonly theme = inject(ThemeService);
  protected readonly logoMark = NWFM_LOGO_MARK;
  protected readonly links = LANDING_NAV_LINKS;
  protected readonly sections = LANDING_SECTIONS;
  protected readonly year = new Date().getFullYear();

  protected go(id: LandingSectionId): void {
    scrollToLandingSection(id);
  }
}
