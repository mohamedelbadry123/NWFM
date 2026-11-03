import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18nDirective, LanguageService } from '@platform/shared';
import { ButtonModule } from 'primeng/button';
import { SkeletonModule } from 'primeng/skeleton';
import { finalize } from 'rxjs';
import { PublicApiService, SignupPlan } from '../../../core/api/public-api.service';
import { SIGNUP_PLAN_PARAM } from '../../signup/signup-params';

const SKELETON_PLANS = 3;

/**
 * Plans as the signup form offers them, read from the same public options endpoint so the page
 * never advertises a plan signup would refuse. Each card opens signup with that plan selected.
 */
@Component({
  selector: 'app-landing-pricing',
  imports: [RouterLink, I18nDirective, ButtonModule, SkeletonModule],
  templateUrl: './landing-pricing.component.html',
  styleUrl: './landing-pricing.component.scss',
})
export class LandingPricingComponent implements OnInit {
  private readonly api = inject(PublicApiService);
  protected readonly language = inject(LanguageService);
  protected readonly skeletons = Array.from({ length: SKELETON_PLANS }, (_, i) => i);

  protected readonly plans = signal<SignupPlan[]>([]);
  protected readonly defaultCode = signal('');
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  /** One card per plan, named in the current language; the default plan is the featured one. */
  protected readonly cards = computed(() => {
    const arabic = this.language.current() === 'ar';
    const featuredCode = this.defaultCode();
    return this.plans().map((plan) => ({
      plan,
      name: arabic ? plan.nameAr : plan.nameEn,
      description: (arabic ? plan.descriptionAr : plan.descriptionEn) ?? '',
      featured: plan.code === featuredCode,
      query: { [SIGNUP_PLAN_PARAM]: plan.code },
    }));
  });

  protected readonly included = [
    'landing.pricing.included.designer',
    'landing.pricing.included.forms',
    'landing.pricing.included.teams',
    'landing.pricing.included.monitoring',
    'landing.pricing.included.permissions',
    'landing.pricing.included.languages',
  ];

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.api
      .getSignupOptions()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (options) => {
          this.plans.set([...options.plans]);
          this.defaultCode.set(options.defaultPlanCode);
        },
        error: () => this.failed.set(true),
      });
  }
}
