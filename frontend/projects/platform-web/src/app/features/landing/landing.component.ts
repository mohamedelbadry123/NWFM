import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18nDirective } from '@platform/shared';
import { ButtonModule } from 'primeng/button';
import { finalize } from 'rxjs';
import { PublicApiService, PublicTenantCard } from '../../core/api/public-api.service';
import { LANDING_SECTIONS, LandingSectionId, scrollToLandingSection } from './landing-sections';
import { tenantInitials } from './tenant-initials';
import { LandingNavComponent } from './sections/landing-nav.component';
import { LandingHeroVisualComponent } from './sections/landing-hero-visual.component';
import { LandingFeaturesComponent } from './sections/landing-features.component';
import { LandingHowComponent } from './sections/landing-how.component';
import { LandingTrustComponent } from './sections/landing-trust.component';
import { LandingPricingComponent } from './sections/landing-pricing.component';
import { LandingWorkspacesComponent } from './sections/landing-workspaces.component';
import { LandingFaqComponent } from './sections/landing-faq.component';
import { LandingCtaComponent } from './sections/landing-cta.component';
import { LandingFooterComponent } from './sections/landing-footer.component';

/** Fewer serving workspaces than this and the hero shows no "already in use" line — it would not read as proof. */
const MIN_PROOF_TENANTS = 3;
/** Workspace initials stacked in the hero before the count carries the rest. */
const PROOF_AVATARS = 5;

/**
 * The public NWFM front door on the apex host: what the product is, how it works, the plans,
 * a sign-in card per serving workspace, and the way in for new customers and NWFM staff.
 * `landingGuard` keeps workspace hosts and signed-in users away from it.
 *
 * The page owns the hero and the workspace list (the hero's proof line and the workspaces section
 * both use it); every other section is its own component under `sections/`.
 */
@Component({
  selector: 'app-landing',
  imports: [
    RouterLink,
    I18nDirective,
    ButtonModule,
    LandingNavComponent,
    LandingHeroVisualComponent,
    LandingFeaturesComponent,
    LandingHowComponent,
    LandingTrustComponent,
    LandingPricingComponent,
    LandingWorkspacesComponent,
    LandingFaqComponent,
    LandingCtaComponent,
    LandingFooterComponent,
  ],
  templateUrl: './landing.component.html',
  styleUrl: './landing.component.scss',
})
export class LandingComponent implements OnInit {
  private readonly api = inject(PublicApiService);
  protected readonly sections = LANDING_SECTIONS;
  protected readonly initials = tenantInitials;

  protected readonly tenants = signal<PublicTenantCard[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  protected readonly showProof = computed(() => this.tenants().length >= MIN_PROOF_TENANTS);
  protected readonly proofTenants = computed(() => this.tenants().slice(0, PROOF_AVATARS));

  protected readonly heroPoints = [
    'landing.hero.pointTrial',
    'landing.hero.pointHosting',
    'landing.hero.pointLanguages',
  ];

  protected readonly capabilities = [
    { icon: 'pi pi-sitemap', key: 'landing.strip.workflow' },
    { icon: 'pi pi-objects-column', key: 'landing.strip.forms' },
    { icon: 'pi pi-users', key: 'landing.strip.teams' },
    { icon: 'pi pi-chart-line', key: 'landing.strip.monitoring' },
    { icon: 'pi pi-shield', key: 'landing.strip.permissions' },
  ];

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.api
      .getTenants()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (tenants) => this.tenants.set([...tenants]),
        error: () => this.failed.set(true),
      });
  }

  protected go(id: LandingSectionId): void {
    scrollToLandingSection(id);
  }
}
