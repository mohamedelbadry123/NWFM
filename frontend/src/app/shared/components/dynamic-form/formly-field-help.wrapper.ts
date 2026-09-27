import { ChangeDetectionStrategy, Component } from '@angular/core';
import { FieldWrapper } from '@ngx-formly/core';

/**
 * Formly wrapper `field-help` — renders `props.description` under the control.
 *
 * The PrimeNG `form-field` wrapper only renders the label and validation
 * message, so descriptions would otherwise be dropped.
 */
@Component({
  selector: 'formly-wrapper-field-help',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container #fieldComponent></ng-container>
    @if (props.description) {
      <small class="block mt-1 text-xs text-[var(--p-text-muted-color)]">{{ props.description }}</small>
    }
  `,
})
export class FormlyFieldHelpWrapperComponent extends FieldWrapper {}
