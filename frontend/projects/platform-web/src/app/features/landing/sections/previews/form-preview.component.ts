import { Component } from '@angular/core';
import { I18nDirective } from '@platform/shared';

/** Feature-tile drawing: a task form from the form builder, one field lifted and one carrying a rule. */
@Component({
  selector: 'app-form-preview',
  imports: [I18nDirective],
  template: `
    <ng-container *appI18n="let t">
      <div class="field">
        <small><i class="pi pi-hashtag"></i>{{ t('landing.features.forms.fieldAmount') }}</small>
        <span class="input"></span>
      </div>
      <div class="field lifted">
        <small><i class="pi pi-file"></i>{{ t('landing.features.forms.fieldQuote') }}</small>
        <span class="input dashed"></span>
      </div>
      <div class="field">
        <small><i class="pi pi-align-left"></i>{{ t('landing.features.forms.fieldReason') }}</small>
        <span class="rule"><i class="pi pi-bolt"></i>{{ t('landing.features.forms.rule') }}</span>
      </div>
    </ng-container>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 0.73rem;
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 0.47rem;
      padding: 0.73rem 0.87rem;
      background: var(--app-surface);
      border: 1px solid var(--app-border);
      border-radius: 0.73rem;

      &.lifted {
        border-color: var(--p-primary-color);
        box-shadow:
          0 10px 22px -12px var(--lp-glow),
          0 0 0 3px var(--acc-brand-bg);
        transform: rotate(-1deg);
      }

      small {
        display: inline-flex;
        align-items: center;
        gap: 0.47rem;
        font-size: 0.93rem;
        font-weight: 600;

        i {
          font-size: 0.93rem;
          color: var(--p-primary-color);
        }
      }
    }

    .input {
      height: 1.87rem;
      border-radius: 0.47rem;
      background: var(--app-surface-alt);
      border: 1px solid var(--app-border);

      &.dashed {
        border-style: dashed;
      }
    }

    .rule {
      display: inline-flex;
      align-items: center;
      gap: 0.47rem;
      align-self: flex-start;
      max-width: 100%;
      padding: 0.27rem 0.73rem;
      border-radius: 999px;
      background: var(--acc-rule-bg);
      color: var(--acc-rule-fg);
      font-size: 0.91rem;
      font-weight: 600;

      i {
        font-size: 0.87rem;
      }
    }
  `,
})
export class FormPreviewComponent {}
