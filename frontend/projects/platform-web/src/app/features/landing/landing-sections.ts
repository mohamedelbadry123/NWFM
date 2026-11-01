/** Anchor ids of the landing page sections the nav, footer and calls to action jump to. */
export const LANDING_SECTIONS = {
  features: 'features',
  how: 'how-it-works',
  pricing: 'pricing',
  workspaces: 'workspaces',
  faq: 'faq',
} as const;

export type LandingSectionId = (typeof LANDING_SECTIONS)[keyof typeof LANDING_SECTIONS];

/** The sections the nav and footer list, in page order. */
export const LANDING_NAV_LINKS: readonly { id: LandingSectionId; labelKey: string }[] = [
  { id: LANDING_SECTIONS.features, labelKey: 'landing.nav.features' },
  { id: LANDING_SECTIONS.how, labelKey: 'landing.nav.how' },
  { id: LANDING_SECTIONS.pricing, labelKey: 'landing.nav.pricing' },
  { id: LANDING_SECTIONS.faq, labelKey: 'landing.nav.faq' },
];

/** In-page jumps scroll rather than route: the landing page is one route and the sticky nav covers the top. */
export function scrollToLandingSection(id: LandingSectionId): void {
  document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
}
