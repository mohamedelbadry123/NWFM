import { ChangeDetectionStrategy, Component } from '@angular/core';
import { FieldWrapper } from '@ngx-formly/core';

/**
 * Formly wrapper `section-panel` — a titled panel around a keyless `fieldGroup`.
 *
 * Form-builder sections stay keyless so the model remains flat (cross-field
 * rules address every field by its `data_name`); this wrapper gives them a
 * visual boundary anyway.
 *
 * Each section is tinted with one of the six `--sec-*` tones assigned by
 * `FormSchemaFormlyMapper`. Every section used to share one surface, so a long
 * form read as a single undivided wall of inputs; the tint plus the accent rail
 * is what separates one section from the next at a glance. The heading still
 * carries the name — the colour is a second cue, never the only one.
 */
@Component({
  selector: 'formly-wrapper-section-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <fieldset
      class="app-form-section overflow-hidden rounded-xl border border-[var(--app-border)]"
      [style.--sec-bg]="'var(--sec-' + tone + '-bg)'"
      [style.--sec-accent]="'var(--sec-' + tone + '-accent)'"
    >
      @if (props.label) {
        <legend class="app-form-section__legend">
          <span class="app-form-section__dot"></span>
          {{ props.label }}
        </legend>
      }
      @if (props.description) {
        <p class="mb-3 text-xs text-[var(--p-text-muted-color)]">{{ props.description }}</p>
      }
      <ng-container #fieldComponent></ng-container>
    </fieldset>
  `,
  styles: [
    `
      .app-form-section {
        background: var(--sec-bg);
        /* An accent rail on the leading edge. The logical border property keeps
           it on the right in Arabic without a separate RTL rule. */
        border-inline-start: 3px solid var(--sec-accent);
        padding: 1rem 1.25rem 1.25rem;
      }

      .app-form-section__legend {
        display: inline-flex;
        align-items: center;
        gap: 0.4rem;
        padding: 0 0.5rem 0 0;
        font-size: 0.8125rem;
        font-weight: 700;
        letter-spacing: 0.01em;
        color: var(--sec-accent);
      }

      .app-form-section__dot {
        width: 0.5rem;
        height: 0.5rem;
        border-radius: 9999px;
        background: var(--sec-accent);
      }
    `,
  ],
})
export class FormlySectionWrapperComponent extends FieldWrapper {
  /** Tone index (1-6) set by the mapper; falls back to the first tone. */
  protected get tone(): number {
    return (this.props as { tone?: number })?.tone ?? 1;
  }
}
