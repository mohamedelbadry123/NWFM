/**
 * Runtime configuration, fetched before the app starts. Kept out of the bundle so the same build can
 * be deployed to several environments — only the JSON beside it changes.
 */
export interface AppConfig {
  /**
   * Google Maps JavaScript API key for the geolocation field. Blank, missing, or left as the
   * committed placeholder means no key: the field then falls back to manual latitude/longitude entry
   * rather than showing a broken map.
   */
  googleMapsApiKey: string;
}

export const DEFAULT_APP_CONFIG: AppConfig = {
  googleMapsApiKey: '',
};

/** The value committed to source control, so a real key is never mistaken for one. */
export const PLACEHOLDER_PREFIX = 'REPLACE_';
