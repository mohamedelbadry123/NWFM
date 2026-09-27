import { EnvironmentProviders, importProvidersFrom, makeEnvironmentProviders } from '@angular/core';
import { FORMLY_CONFIG, FormlyModule } from '@ngx-formly/core';
import { FormlyPrimeNGModule } from '@ngx-formly/primeng';
import { TranslateService } from '@ngx-translate/core';
import { FORMLY_TYPES, FORMLY_WRAPPERS } from './formly-preview.types';
import { FormlyDateComponent } from './formly-date.component';
import { FormlyTimeComponent } from './formly-time.component';
import { FormlyDateTimeComponent } from './formly-date-time.component';
import { FormlyChoiceComponent } from './formly-choice.component';
import { FormlySignatureComponent } from './formly-signature.component';
import { FormlyAttachmentComponent } from './formly-attachment.component';
import { FormlyGeolocationComponent } from './formly-geolocation.component';
import { FormlyBarcodeComponent } from './formly-barcode.component';
import { FormlyCalendarWithHoursComponent } from './formly-calendar-with-hours.component';
import { FormlyFieldHelpWrapperComponent } from './formly-field-help.wrapper';
import { FormlySectionWrapperComponent } from './formly-section.wrapper';
import { formlyValidationConfig } from './formly-validators';

/**
 * Registers the form engine's field types with Formly.
 *
 * Text, textarea and radio come from `FormlyPrimeNGModule`; everything a plain input cannot express
 * — a signature pad, a map, a media picker, a weekly shift grid — is a component of our own. The
 * registration is app-wide rather than per-route so any page can render a form, which is what a
 * workflow task screen will need.
 */
export function provideDynamicForms(): EnvironmentProviders {
  return makeEnvironmentProviders([
    importProvidersFrom(
      FormlyModule.forRoot({
        types: [
          { name: FORMLY_TYPES.Date, component: FormlyDateComponent },
          { name: FORMLY_TYPES.Time, component: FormlyTimeComponent },
          {
            name: FORMLY_TYPES.Choice,
            component: FormlyChoiceComponent,
            defaultOptions: { wrappers: ['form-field'] },
          },
          {
            name: FORMLY_TYPES.MultiChoice,
            extends: FORMLY_TYPES.Choice,
            defaultOptions: { props: { multiple: true }, wrappers: ['form-field'] },
          },
          { name: FORMLY_TYPES.Signature, component: FormlySignatureComponent },
          { name: FORMLY_TYPES.Attachment, component: FormlyAttachmentComponent },
          { name: FORMLY_TYPES.Geolocation, component: FormlyGeolocationComponent },
          { name: FORMLY_TYPES.Barcode, component: FormlyBarcodeComponent },
          { name: FORMLY_TYPES.CalendarWithHours, component: FormlyCalendarWithHoursComponent },
          { name: FORMLY_TYPES.DateTime, component: FormlyDateTimeComponent },
        ],
        wrappers: [
          { name: FORMLY_WRAPPERS.FieldHelp, component: FormlyFieldHelpWrapperComponent },
          { name: FORMLY_WRAPPERS.SectionPanel, component: FormlySectionWrapperComponent },
        ],
      }),
      FormlyPrimeNGModule,
    ),
    // Separate, so validation messages resolve through the app's translations.
    {
      provide: FORMLY_CONFIG,
      multi: true,
      useFactory: (translate: TranslateService) => formlyValidationConfig(translate),
      deps: [TranslateService],
    },
  ]);
}
