import {
  ELEMENT_TYPES,
  PALETTE_GROUPS,
  type ElementType,
  type PaletteGroup,
} from '../../../../shared/form-schema/form-schema.types';

export interface PaletteItem {
  readonly type: ElementType;
  readonly icon: string;
  readonly group: PaletteGroup;
  /** Translation key for the display label. */
  readonly labelKey: string;
}

export const PALETTE_ITEMS: readonly PaletteItem[] = [
  { type: ELEMENT_TYPES.Text, icon: 'pi pi-pencil', group: PALETTE_GROUPS.Basic, labelKey: 'formBuilder.types.text' },
  { type: ELEMENT_TYPES.Memo, icon: 'pi pi-align-left', group: PALETTE_GROUPS.Basic, labelKey: 'formBuilder.types.memo' },
  { type: ELEMENT_TYPES.Numeric, icon: 'pi pi-hashtag', group: PALETTE_GROUPS.Basic, labelKey: 'formBuilder.types.numeric' },
  { type: ELEMENT_TYPES.YesNo, icon: 'pi pi-check-square', group: PALETTE_GROUPS.Basic, labelKey: 'formBuilder.types.yes_no' },
  { type: ELEMENT_TYPES.Date, icon: 'pi pi-calendar', group: PALETTE_GROUPS.Basic, labelKey: 'formBuilder.types.date' },
  { type: ELEMENT_TYPES.Time, icon: 'pi pi-clock', group: PALETTE_GROUPS.Basic, labelKey: 'formBuilder.types.time' },
  { type: ELEMENT_TYPES.CalendarWithHours, icon: 'pi pi-calendar-clock', group: PALETTE_GROUPS.Basic, labelKey: 'formBuilder.types.calendar_with_hours' },
  { type: ELEMENT_TYPES.DateTime, icon: 'pi pi-calendar-plus', group: PALETTE_GROUPS.Basic, labelKey: 'formBuilder.types.date_time' },
  { type: ELEMENT_TYPES.Barcode, icon: 'pi pi-qrcode', group: PALETTE_GROUPS.Basic, labelKey: 'formBuilder.types.barcode' },
  { type: ELEMENT_TYPES.SingleChoice, icon: 'pi pi-list', group: PALETTE_GROUPS.Choice, labelKey: 'formBuilder.types.single_choice' },
  { type: ELEMENT_TYPES.MultipleChoice, icon: 'pi pi-check-circle', group: PALETTE_GROUPS.Choice, labelKey: 'formBuilder.types.multiple_choice' },
  { type: ELEMENT_TYPES.Signature, icon: 'pi pi-pencil', group: PALETTE_GROUPS.Media, labelKey: 'formBuilder.types.signature' },
  { type: ELEMENT_TYPES.Photo, icon: 'pi pi-camera', group: PALETTE_GROUPS.Media, labelKey: 'formBuilder.types.photo' },
  { type: ELEMENT_TYPES.Video, icon: 'pi pi-video', group: PALETTE_GROUPS.Media, labelKey: 'formBuilder.types.video' },
  { type: ELEMENT_TYPES.Audio, icon: 'pi pi-volume-up', group: PALETTE_GROUPS.Media, labelKey: 'formBuilder.types.audio' },
  { type: ELEMENT_TYPES.File, icon: 'pi pi-file', group: PALETTE_GROUPS.Media, labelKey: 'formBuilder.types.file' },
  { type: ELEMENT_TYPES.Geolocation, icon: 'pi pi-map-marker', group: PALETTE_GROUPS.Location, labelKey: 'formBuilder.types.geolocation' },
  { type: ELEMENT_TYPES.Section, icon: 'pi pi-folder', group: PALETTE_GROUPS.Design, labelKey: 'formBuilder.types.section' },
];

export const PALETTE_GROUP_ORDER: readonly PaletteGroup[] = [
  PALETTE_GROUPS.Basic,
  PALETTE_GROUPS.Choice,
  PALETTE_GROUPS.Media,
  PALETTE_GROUPS.Location,
  PALETTE_GROUPS.Design,
];
