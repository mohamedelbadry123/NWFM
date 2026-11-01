import { Component, HostListener, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18nDirective, LanguageService, ThemeService, NWFM_LOGO_MARK } from '@platform/shared';
import { ButtonModule } from 'primeng/button';
import {
  LANDING_NAV_LINKS,
  LANDING_SECTIONS,
  LandingSectionId,
  scrollToLandingSection,
} from '../landing-sections';

/** Scroll distance after which the nav takes its frosted background. */
const NAV_SOLID_AFTER_PX = 8;

/** Sticky top bar: section links, language and theme toggles, sign-in and the signup call to action. */
@Component({
  selector: 'app-landing-nav',
  imports: [RouterLink, I18nDirective, ButtonModule],
  templateUrl: './landing-nav.component.html',
  styleUrl: './landing-nav.component.scss',
})
export class LandingNavComponent implements OnInit {
  protected readonly language = inject(LanguageService);
  protected readonly theme = inject(ThemeService);
  protected readonly logoMark = NWFM_LOGO_MARK;
  protected readonly links = LANDING_NAV_LINKS;
  protected readonly sections = LANDING_SECTIONS;

  protected readonly scrolled = signal(false);
  protected readonly menuOpen = signal(false);

  ngOnInit(): void {
    this.onScroll();
  }

  @HostListener('window:scroll')
  protected onScroll(): void {
    this.scrolled.set(window.scrollY > NAV_SOLID_AFTER_PX);
  }

  @HostListener('document:keydown.escape')
  protected closeMenu(): void {
    this.menuOpen.set(false);
  }

  protected toggleMenu(): void {
    this.menuOpen.update((open) => !open);
  }

  protected go(id: LandingSectionId): void {
    this.menuOpen.set(false);
    scrollToLandingSection(id);
  }
}
