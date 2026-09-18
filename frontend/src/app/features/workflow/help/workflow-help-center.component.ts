import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import {
  HelpViewTab,
  MANUAL_GLOSSARY,
  MANUAL_GUIDES,
  MANUAL_KEYBOARD,
  MANUAL_NAV_GROUPS,
  ManualAudience,
  ManualCategory,
  ManualGuide,
} from './workflow-manual.content';
import { WorkflowPageHeaderComponent } from '../ui/workflow-page-header.component';

type AudienceFilter = 'all' | ManualAudience;

/** Maps legacy section hashes / aliases onto current guide ids. */
const FRAGMENT_ALIASES: Record<string, string> = {
  'saas-binding': 'bindings-saas',
  'bindings': 'bindings-saas',
  'create-draft': 'open-draft-draw',
  'simulate': 'simulate-binding',
  'shadow-active': 'modes-shadow-active',
  'monitor': 'monitor-ops',
  'user-tasks': 'configure-user-task',
  'assignment': 'assignment-groups',
  'glossary': '__tab_glossary__',
  'keyboard': '__tab_keyboard__',
};

@Component({
  selector: 'app-workflow-help-center',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslateModule, RouterLink, WorkflowPageHeaderComponent],
  templateUrl: './workflow-help-center.component.html',
})
export class WorkflowHelpCenterComponent implements OnInit {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly translate = inject(TranslateService);

  protected readonly activeTab = signal<HelpViewTab>('paths');
  protected readonly audienceFilter = signal<AudienceFilter>('all');
  protected readonly searchQuery = signal('');
  protected readonly activeGuideId = signal<string | null>(null);

  protected readonly navGroups = MANUAL_NAV_GROUPS;
  protected readonly glossary = MANUAL_GLOSSARY;
  protected readonly keyboardShortcuts = MANUAL_KEYBOARD;
  protected readonly allGuides = MANUAL_GUIDES;

  protected readonly isOrgContext = this.router.url.includes('/org/workflow/help');
  protected readonly backLink = this.isOrgContext
    ? '/org/workflow/tasks?view=claimedByMe'
    : '/admin/workflow/definitions';
  protected readonly backLinkKey = this.isOrgContext
    ? 'workflow.help.back_to_tasks'
    : 'workflow.help.back_to_definitions';

  constructor() {
    if (this.isOrgContext) {
      this.audienceFilter.set('org');
      const firstOrg =
        MANUAL_GUIDES.find(g => g.isLearningPath && g.audience === 'org') ??
        MANUAL_GUIDES.find(g => g.audience === 'org' || g.audience === 'both');
      this.activeGuideId.set(firstOrg?.id ?? 'path-org-prepare');
    } else if (this.router.url.includes('/admin/workflow/help')) {
      this.audienceFilter.set('admin');
      this.activeGuideId.set('path-admin-go-live');
    } else {
      this.activeGuideId.set('path-admin-go-live');
    }
  }

  ngOnInit(): void {
    this.route.fragment.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(fragment => {
      if (!fragment) return;
      this.applyFragment(fragment);
    });
  }

  protected readonly filteredGuides = computed(() => {
    const audience = this.audienceFilter();
    const q = this.searchQuery().trim().toLowerCase();

    return MANUAL_GUIDES.filter(guide => {
      if (!this.matchesAudience(guide, audience)) return false;
      if (!q) return true;
      return this.matchesSearch(guide, q);
    });
  });

  protected readonly learningPaths = computed(() =>
    this.filteredGuides().filter(g => g.isLearningPath)
  );

  protected readonly nonPathGuides = computed(() =>
    this.filteredGuides().filter(g => !g.isLearningPath)
  );

  protected readonly guidesByCategory = computed(() => {
    const guides = this.nonPathGuides();
    const map = new Map<ManualCategory, ManualGuide[]>();
    for (const group of MANUAL_NAV_GROUPS) {
      map.set(
        group.category,
        guides.filter(g => g.category === group.category)
      );
    }
    return map;
  });

  protected readonly activeGuide = computed(() => {
    const id = this.activeGuideId();
    if (!id) return null;
    return MANUAL_GUIDES.find(g => g.id === id) ?? null;
  });

  protected readonly relatedGuides = computed(() => {
    const guide = this.activeGuide();
    if (!guide?.relatedIds?.length) return [];
    return guide.relatedIds
      .map(id => MANUAL_GUIDES.find(g => g.id === id))
      .filter((g): g is ManualGuide => !!g);
  });

  protected setTab(tab: HelpViewTab): void {
    this.activeTab.set(tab);
    if (tab === 'paths') {
      const paths = this.learningPaths();
      if (paths.length && !paths.some(p => p.id === this.activeGuideId())) {
        this.activeGuideId.set(paths[0].id);
      }
    } else if (tab === 'guides') {
      const guides = this.filteredGuides().filter(g => !g.isLearningPath);
      if (guides.length && !guides.some(g => g.id === this.activeGuideId())) {
        this.activeGuideId.set(guides[0].id);
      }
    }
  }

  protected setAudience(filter: AudienceFilter): void {
    this.audienceFilter.set(filter);
    const visible =
      this.activeTab() === 'paths'
        ? MANUAL_GUIDES.filter(
            g => g.isLearningPath && this.matchesAudience(g, filter)
          )
        : MANUAL_GUIDES.filter(
            g => !g.isLearningPath && this.matchesAudience(g, filter)
          );
    const current = this.activeGuideId();
    if (!visible.some(g => g.id === current) && visible.length) {
      this.activeGuideId.set(visible[0].id);
    }
  }

  protected selectGuide(id: string): void {
    this.activeGuideId.set(id);
    const guide = MANUAL_GUIDES.find(g => g.id === id);
    if (guide?.isLearningPath) {
      this.activeTab.set('paths');
    } else if (guide) {
      this.activeTab.set('guides');
    }
    void this.router.navigate([], {
      relativeTo: this.route,
      fragment: id,
      replaceUrl: true,
    });
  }

  protected categoryBadge(category: ManualCategory): string {
    switch (category) {
      case 'getting-started':
        return 'GS';
      case 'admin':
        return 'SA';
      case 'org':
        return 'OR';
      case 'designer':
        return 'DR';
      case 'ops':
        return 'OP';
      case 'reference':
        return 'RF';
    }
  }

  protected audienceBadgeKey(audience: ManualAudience): string {
    switch (audience) {
      case 'admin':
        return 'workflow.help.badge_admin';
      case 'org':
        return 'workflow.help.badge_org';
      case 'both':
        return 'workflow.help.badge_both';
    }
  }

  protected guidesForCategory(category: ManualCategory): ManualGuide[] {
    return this.guidesByCategory().get(category) ?? [];
  }

  private applyFragment(fragment: string): void {
    const resolved = FRAGMENT_ALIASES[fragment] ?? fragment;
    if (resolved === '__tab_glossary__') {
      this.activeTab.set('glossary');
      return;
    }
    if (resolved === '__tab_keyboard__') {
      this.activeTab.set('keyboard');
      return;
    }
    const guide = MANUAL_GUIDES.find(g => g.id === resolved);
    if (!guide) return;
    this.activeGuideId.set(guide.id);
    this.activeTab.set(guide.isLearningPath ? 'paths' : 'guides');
  }

  private matchesAudience(guide: ManualGuide, filter: AudienceFilter): boolean {
    if (filter === 'all') return true;
    if (filter === 'both') return true;
    return guide.audience === filter || guide.audience === 'both';
  }

  private matchesSearch(guide: ManualGuide, q: string): boolean {
    const haystack = [
      guide.id,
      guide.titleKey,
      guide.summaryKey,
      this.translate.instant(guide.titleKey),
      this.translate.instant(guide.summaryKey),
      ...guide.steps.flatMap(step => [
        step.titleKey,
        this.translate.instant(step.titleKey),
      ]),
    ]
      .join(' ')
      .toLowerCase();
    return haystack.includes(q);
  }
}
