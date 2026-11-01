import {
  ChangeDetectorRef,
  DestroyRef,
  Directive,
  EmbeddedViewRef,
  OnInit,
  TemplateRef,
  ViewContainerRef,
  inject,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslateService } from '@ngx-translate/core';

/** Looks a key up in the active dictionary, interpolating <c>{{ name }}</c> placeholders. */
export type TranslateFn = (key: string, params?: Record<string, unknown>) => string;

interface I18nContext {
  $implicit: TranslateFn;
}

/**
 * Exposes a translate function to a template block: `<ng-container *appI18n="let t">{{ t('key') }}</ng-container>`.
 * The block re-renders when the language changes, so every `t()` call picks up the new dictionary.
 */
@Directive({ selector: '[appI18n]' })
export class I18nDirective implements OnInit {
  private readonly template = inject<TemplateRef<I18nContext>>(TemplateRef);
  private readonly container = inject(ViewContainerRef);
  private readonly translate = inject(TranslateService);
  private readonly changes = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);
  private view?: EmbeddedViewRef<I18nContext>;

  // Angular reads this signature to type `let t`; the parameter exists only for the type predicate.
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  static ngTemplateContextGuard(_dir: I18nDirective, context: unknown): context is I18nContext {
    return true;
  }

  ngOnInit(): void {
    this.view = this.container.createEmbeddedView(this.template, { $implicit: this.translator() });
    this.translate.onLangChange.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      if (!this.view) return;
      this.view.context.$implicit = this.translator();
      this.view.markForCheck();
      this.changes.markForCheck();
    });
  }

  private translator(): TranslateFn {
    return (key, params) => this.translate.instant(key, params);
  }
}
