import {
  Directive,
  EmbeddedViewRef,
  TemplateRef,
  ViewContainerRef,
  inject,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslateService } from '@ngx-translate/core';
import { merge } from 'rxjs';

/** The translate function handed to the template. */
export type TranslateFn = (key: string, params?: Record<string, unknown>) => string;

export interface TranslateContext {
  $implicit: TranslateFn;
}

/**
 * Gives a template a `t(key, params)` function:
 *
 * ```html
 * <ng-container *translateContext="let t">{{ t('forms.title') }}</ng-container>
 * ```
 *
 * The `translate` pipe covers most screens, but a template with dozens of labels — the field
 * palette, the map picker, the preview dialog — reads far better with one short call than with a
 * pipe on every line. It is also the shape the form engine was written in, so those templates stay
 * comparable to where they came from.
 *
 * `t` resolves through `TranslateService.instant`, which is a plain function call: Angular only
 * re-reads it when the view is marked dirty. A language switch, or a translation file that lands
 * after the first render, does that here.
 */
@Directive({
  selector: '[translateContext]',
  standalone: true,
})
export class TranslateContextDirective {
  private readonly template = inject<TemplateRef<TranslateContext>>(TemplateRef);
  private readonly container = inject(ViewContainerRef);
  private readonly translate = inject(TranslateService);

  private readonly view: EmbeddedViewRef<TranslateContext>;

  private readonly translateFn: TranslateFn = (key, params) =>
    this.translate.instant(key, params);

  constructor() {
    this.view = this.container.createEmbeddedView(this.template, { $implicit: this.translateFn });

    merge(
      this.translate.onLangChange,
      this.translate.onDefaultLangChange,
      this.translate.onTranslationChange,
    )
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.view.markForCheck());
  }

  /** Types `let t` in the template instead of leaving it `any`. */
  static ngTemplateContextGuard(
    _directive: TranslateContextDirective,
    context: unknown,
  ): context is TranslateContext {
    return true;
  }
}
