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

  /**
   * Development only: let `?tenant=acme` name the workspace when the page is not on the workspace's
   * own host. The API honours the resulting `X-Tenant` header only in Development.
   */
  allowTenantQuery: boolean;

  /** The platform web app (landing page and signup), linked from the workspace-unavailable page. */
  platformUrl: string;
}

export const DEFAULT_APP_CONFIG: AppConfig = {
  googleMapsApiKey: '',
  allowTenantQuery: false,
  platformUrl: '',
};

/** The value committed to source control, so a real key is never mistaken for one. */
export const PLACEHOLDER_PREFIX = 'REPLACE_';
