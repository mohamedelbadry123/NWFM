export type ManualAudience = 'admin' | 'org' | 'both';
export type ManualCategory =
  | 'getting-started'
  | 'admin'
  | 'org'
  | 'designer'
  | 'ops'
  | 'reference';

export type HelpViewTab = 'paths' | 'guides' | 'glossary' | 'keyboard';

export interface ManualStep {
  titleKey: string;
  bodyKey: string;
  tipKey?: string;
  link?: string;
  linkLabelKey?: string;
}

export interface ManualGuide {
  id: string;
  audience: ManualAudience;
  category: ManualCategory;
  titleKey: string;
  summaryKey: string;
  steps: ManualStep[];
  relatedIds?: string[];
  /** When true, guide appears in the Learning Paths tab. */
  isLearningPath?: boolean;
}

export interface ManualNavGroup {
  category: ManualCategory;
  titleKey: string;
}

export interface ManualGlossaryEntry {
  termKey: string;
  definitionKey: string;
}

export interface ManualKeyboardShortcut {
  key: string;
  actionKey: string;
}

const g = (id: string) => `workflow.manual.guides.${id}`;
const s = (id: string, n: number, part: 'title' | 'body' | 'tip' | 'link') =>
  `${g(id)}.s${n}_${part}`;

function guideSteps(
  id: string,
  defs: Array<{ tip?: boolean; link?: string }>
): ManualStep[] {
  return defs.map((def, i) => {
    const n = i + 1;
    const step: ManualStep = {
      titleKey: s(id, n, 'title'),
      bodyKey: s(id, n, 'body'),
    };
    if (def.tip) step.tipKey = s(id, n, 'tip');
    if (def.link) {
      step.link = def.link;
      step.linkLabelKey = s(id, n, 'link');
    }
    return step;
  });
}

export const MANUAL_NAV_GROUPS: ManualNavGroup[] = [
  { category: 'getting-started', titleKey: 'workflow.help.nav_getting_started' },
  { category: 'admin', titleKey: 'workflow.help.nav_admin' },
  { category: 'org', titleKey: 'workflow.help.nav_org' },
  { category: 'designer', titleKey: 'workflow.help.nav_designer' },
  { category: 'ops', titleKey: 'workflow.help.nav_ops' },
  { category: 'reference', titleKey: 'workflow.help.nav_reference' },
];

export const MANUAL_GUIDES: ManualGuide[] = [
  // ── Learning paths ───────────────────────────────────────────────────────
  {
    id: 'path-admin-go-live',
    audience: 'admin',
    category: 'getting-started',
    isLearningPath: true,
    titleKey: `${g('path-admin-go-live')}.title`,
    summaryKey: `${g('path-admin-go-live')}.summary`,
    relatedIds: ['create-definition', 'bindings-saas', 'modes-shadow-active', 'monitor-ops'],
    steps: guideSteps('path-admin-go-live', [
      { tip: true, link: '/admin/workflow/definitions' },
      { tip: true },
      { tip: true },
      { tip: true },
      { tip: true, link: '/admin/workflow/bindings/new' },
      { tip: true },
      { tip: true },
      { tip: true },
      { tip: true, link: '/admin/workflow/instances' },
    ]),
  },
  {
    id: 'path-org-prepare',
    audience: 'org',
    category: 'getting-started',
    isLearningPath: true,
    titleKey: `${g('path-org-prepare')}.title`,
    summaryKey: `${g('path-org-prepare')}.summary`,
    relatedIds: ['register-participants', 'my-tasks-inbox', 'claim-complete', 'workload'],
    steps: guideSteps('path-org-prepare', [
      { tip: true, link: '/org/workflow/participants' },
      { tip: true, link: '/org/workflow/assignment-groups' },
      { link: '/org/workflow/departments' },
      { tip: true },
      { tip: true, link: '/org/workflow/tasks?view=claimedByMe' },
      { tip: true },
      { tip: true },
      { link: '/org/workflow/requests' },
      { link: '/org/workflow/workload' },
    ]),
  },
  {
    id: 'standalone-walkthrough',
    audience: 'both',
    category: 'getting-started',
    isLearningPath: true,
    titleKey: `${g('standalone-walkthrough')}.title`,
    summaryKey: `${g('standalone-walkthrough')}.summary`,
    relatedIds: ['path-admin-go-live', 'bindings-saas', 'modes-shadow-active'],
    steps: guideSteps('standalone-walkthrough', [
      { tip: true },
      { tip: true, link: '/admin/workflow/definitions' },
      { tip: true },
      { tip: true },
      { tip: true, link: '/admin/workflow/bindings/new' },
      { tip: true },
      { tip: true },
      { tip: true },
      { tip: true },
    ]),
  },

  // ── Getting started ──────────────────────────────────────────────────────
  {
    id: 'overview',
    audience: 'both',
    category: 'getting-started',
    titleKey: `${g('overview')}.title`,
    summaryKey: `${g('overview')}.summary`,
    relatedIds: ['path-admin-go-live', 'path-org-prepare', 'roles'],
    steps: guideSteps('overview', [{}, {}, { tip: true }, {}]),
  },
  {
    id: 'roles',
    audience: 'both',
    category: 'getting-started',
    titleKey: `${g('roles')}.title`,
    summaryKey: `${g('roles')}.summary`,
    relatedIds: ['path-admin-go-live', 'path-org-prepare'],
    steps: guideSteps('roles', [{}, {}, {}]),
  },

  // ── Super Admin manual ───────────────────────────────────────────────────
  {
    id: 'create-definition',
    audience: 'admin',
    category: 'admin',
    titleKey: `${g('create-definition')}.title`,
    summaryKey: `${g('create-definition')}.summary`,
    relatedIds: ['open-draft-draw', 'validate-publish'],
    steps: guideSteps('create-definition', [
      { link: '/admin/workflow/definitions' },
      { tip: true },
      {},
      { tip: true },
    ]),
  },
  {
    id: 'open-draft-draw',
    audience: 'admin',
    category: 'admin',
    titleKey: `${g('open-draft-draw')}.title`,
    summaryKey: `${g('open-draft-draw')}.summary`,
    relatedIds: ['draw-connect', 'configure-user-task', 'node-reference'],
    steps: guideSteps('open-draft-draw', [{}, { tip: true }, {}, { tip: true }]),
  },
  {
    id: 'configure-user-task',
    audience: 'admin',
    category: 'admin',
    titleKey: `${g('configure-user-task')}.title`,
    summaryKey: `${g('configure-user-task')}.summary`,
    relatedIds: ['outcomes', 'sla', 'group-mappings'],
    steps: guideSteps('configure-user-task', [
      { tip: true },
      {},
      {},
      {},
      { tip: true },
    ]),
  },
  {
    id: 'validate-publish',
    audience: 'admin',
    category: 'admin',
    titleKey: `${g('validate-publish')}.title`,
    summaryKey: `${g('validate-publish')}.summary`,
    relatedIds: ['bindings-saas', 'open-draft-draw'],
    steps: guideSteps('validate-publish', [{}, { tip: true }, { tip: true }, {}]),
  },
  {
    id: 'bindings-saas',
    audience: 'admin',
    category: 'admin',
    titleKey: `${g('bindings-saas')}.title`,
    summaryKey: `${g('bindings-saas')}.summary`,
    relatedIds: ['group-mappings', 'simulate-binding', 'modes-shadow-active', 'standalone-walkthrough'],
    steps: guideSteps('bindings-saas', [
      { link: '/admin/workflow/bindings' },
      { tip: true },
      {},
      {},
      { tip: true },
    ]),
  },
  {
    id: 'group-mappings',
    audience: 'admin',
    category: 'admin',
    titleKey: `${g('group-mappings')}.title`,
    summaryKey: `${g('group-mappings')}.summary`,
    relatedIds: ['bindings-saas', 'assignment-keys-note'],
    steps: guideSteps('group-mappings', [{ tip: true }, {}, { tip: true }]),
  },
  {
    id: 'simulate-binding',
    audience: 'admin',
    category: 'admin',
    titleKey: `${g('simulate-binding')}.title`,
    summaryKey: `${g('simulate-binding')}.summary`,
    relatedIds: ['bindings-saas', 'modes-shadow-active'],
    steps: guideSteps('simulate-binding', [{ tip: true }, {}, { tip: true }]),
  },
  {
    id: 'modes-shadow-active',
    audience: 'admin',
    category: 'admin',
    titleKey: `${g('modes-shadow-active')}.title`,
    summaryKey: `${g('modes-shadow-active')}.summary`,
    relatedIds: ['simulate-binding', 'monitor-ops'],
    steps: guideSteps('modes-shadow-active', [{}, { tip: true }, {}, { tip: true }]),
  },
  {
    id: 'monitor-ops',
    audience: 'admin',
    category: 'admin',
    titleKey: `${g('monitor-ops')}.title`,
    summaryKey: `${g('monitor-ops')}.summary`,
    relatedIds: ['dead-letters', 'workload', 'troubleshooting'],
    steps: guideSteps('monitor-ops', [
      { link: '/admin/workflow/instances' },
      { link: '/admin/workflow/dead-letters' },
      { link: '/admin/workflow/workload' },
      { tip: true },
    ]),
  },

  // ── Organization manual ──────────────────────────────────────────────────
  {
    id: 'register-participants',
    audience: 'org',
    category: 'org',
    titleKey: `${g('register-participants')}.title`,
    summaryKey: `${g('register-participants')}.summary`,
    relatedIds: ['assignment-groups', 'path-org-prepare'],
    steps: guideSteps('register-participants', [
      { link: '/org/workflow/participants' },
      { tip: true },
      {},
    ]),
  },
  {
    id: 'assignment-groups',
    audience: 'org',
    category: 'org',
    titleKey: `${g('assignment-groups')}.title`,
    summaryKey: `${g('assignment-groups')}.summary`,
    relatedIds: ['register-participants', 'departments', 'assignment-keys-note'],
    steps: guideSteps('assignment-groups', [
      { link: '/org/workflow/assignment-groups' },
      {},
      { tip: true },
    ]),
  },
  {
    id: 'departments',
    audience: 'org',
    category: 'org',
    titleKey: `${g('departments')}.title`,
    summaryKey: `${g('departments')}.summary`,
    relatedIds: ['assignment-groups'],
    steps: guideSteps('departments', [
      { link: '/org/workflow/departments' },
      {},
    ]),
  },
  {
    id: 'assignment-keys-note',
    audience: 'org',
    category: 'org',
    titleKey: `${g('assignment-keys-note')}.title`,
    summaryKey: `${g('assignment-keys-note')}.summary`,
    relatedIds: ['assignment-groups', 'group-mappings'],
    steps: guideSteps('assignment-keys-note', [{ tip: true }, {}]),
  },
  {
    id: 'my-tasks-inbox',
    audience: 'org',
    category: 'org',
    titleKey: `${g('my-tasks-inbox')}.title`,
    summaryKey: `${g('my-tasks-inbox')}.summary`,
    relatedIds: ['claim-complete', 'delegate-release'],
    steps: guideSteps('my-tasks-inbox', [
      { link: '/org/workflow/tasks?view=claimedByMe' },
      { link: '/org/workflow/tasks?view=available' },
      { tip: true },
    ]),
  },
  {
    id: 'claim-complete',
    audience: 'org',
    category: 'org',
    titleKey: `${g('claim-complete')}.title`,
    summaryKey: `${g('claim-complete')}.summary`,
    relatedIds: ['my-tasks-inbox', 'delegate-release', 'track-requests'],
    steps: guideSteps('claim-complete', [{}, { tip: true }, {}, { tip: true }]),
  },
  {
    id: 'delegate-release',
    audience: 'org',
    category: 'org',
    titleKey: `${g('delegate-release')}.title`,
    summaryKey: `${g('delegate-release')}.summary`,
    relatedIds: ['claim-complete', 'my-tasks-inbox'],
    steps: guideSteps('delegate-release', [{ tip: true }, {}, {}]),
  },
  {
    id: 'track-requests',
    audience: 'org',
    category: 'org',
    titleKey: `${g('track-requests')}.title`,
    summaryKey: `${g('track-requests')}.summary`,
    relatedIds: ['claim-complete', 'workload'],
    steps: guideSteps('track-requests', [
      { link: '/org/workflow/requests' },
      {},
      { tip: true },
    ]),
  },
  {
    id: 'workload',
    audience: 'both',
    category: 'org',
    titleKey: `${g('workload')}.title`,
    summaryKey: `${g('workload')}.summary`,
    relatedIds: ['monitor-ops', 'track-requests'],
    steps: guideSteps('workload', [
      { link: '/org/workflow/workload' },
      {},
      { tip: true },
    ]),
  },

  // ── Designer reference ───────────────────────────────────────────────────
  {
    id: 'draw-connect',
    audience: 'admin',
    category: 'designer',
    titleKey: `${g('draw-connect')}.title`,
    summaryKey: `${g('draw-connect')}.summary`,
    relatedIds: ['node-reference', 'open-draft-draw'],
    steps: guideSteps('draw-connect', [{ tip: true }, {}, { tip: true }]),
  },
  {
    id: 'node-reference',
    audience: 'admin',
    category: 'designer',
    titleKey: `${g('node-reference')}.title`,
    summaryKey: `${g('node-reference')}.summary`,
    relatedIds: ['draw-connect', 'configure-user-task'],
    steps: guideSteps('node-reference', [{}, {}, {}, {}, {}, { tip: true }]),
  },
  {
    id: 'outcomes',
    audience: 'admin',
    category: 'designer',
    titleKey: `${g('outcomes')}.title`,
    summaryKey: `${g('outcomes')}.summary`,
    relatedIds: ['configure-user-task', 'draw-connect'],
    steps: guideSteps('outcomes', [{}, { tip: true }, {}]),
  },
  {
    id: 'actions',
    audience: 'admin',
    category: 'designer',
    titleKey: `${g('actions')}.title`,
    summaryKey: `${g('actions')}.summary`,
    relatedIds: ['configure-user-task'],
    steps: guideSteps('actions', [{}, {}, { tip: true }]),
  },
  {
    id: 'variables',
    audience: 'admin',
    category: 'designer',
    titleKey: `${g('variables')}.title`,
    summaryKey: `${g('variables')}.summary`,
    relatedIds: ['configure-user-task', 'bindings-saas'],
    steps: guideSteps('variables', [{}, { tip: true }, {}]),
  },
  {
    id: 'sla',
    audience: 'admin',
    category: 'designer',
    titleKey: `${g('sla')}.title`,
    summaryKey: `${g('sla')}.summary`,
    relatedIds: ['configure-user-task'],
    steps: guideSteps('sla', [
      { link: '/admin/workflow/sla-policies' },
      {},
      { tip: true },
    ]),
  },

  // ── Operations & troubleshooting ─────────────────────────────────────────
  {
    id: 'dead-letters',
    audience: 'admin',
    category: 'ops',
    titleKey: `${g('dead-letters')}.title`,
    summaryKey: `${g('dead-letters')}.summary`,
    relatedIds: ['monitor-ops', 'troubleshooting'],
    steps: guideSteps('dead-letters', [
      { link: '/admin/workflow/dead-letters' },
      { tip: true },
      {},
    ]),
  },
  {
    id: 'troubleshooting',
    audience: 'both',
    category: 'ops',
    titleKey: `${g('troubleshooting')}.title`,
    summaryKey: `${g('troubleshooting')}.summary`,
    relatedIds: ['validate-publish', 'group-mappings', 'dead-letters'],
    steps: guideSteps('troubleshooting', [{}, {}, {}, {}, { tip: true }, {}]),
  },

  // ── Reference ────────────────────────────────────────────────────────────
  {
    id: 'modes-reference',
    audience: 'admin',
    category: 'reference',
    titleKey: `${g('modes-reference')}.title`,
    summaryKey: `${g('modes-reference')}.summary`,
    relatedIds: ['modes-shadow-active', 'bindings-saas'],
    steps: guideSteps('modes-reference', [{}, {}, {}, {}]),
  },
];

export const MANUAL_GLOSSARY: ManualGlossaryEntry[] = [
  'definition',
  'version',
  'draft',
  'published',
  'binding',
  'assignment_key',
  'assignment_group',
  'outcome',
  'transition',
  'exclusive_gateway',
  'inclusive_gateway',
  'call_activity',
  'parallel_gateway',
  'join_gateway',
  'sla_policy',
  'screen_key',
  'shadow_mode',
  'active_mode',
  'paused_mode',
  'disabled_mode',
  'simulate',
  'dead_letter',
  'workload',
  'set_variables',
  'wait_for_event',
  'work_item',
  'instance',
].map(id => ({
  termKey: `workflow.help.glossary.${id}.term`,
  definitionKey: `workflow.help.glossary.${id}.definition`,
}));

export const MANUAL_KEYBOARD: ManualKeyboardShortcut[] = [
  { key: 'Ctrl + Z', actionKey: 'workflow.help.keyboard.undo' },
  { key: 'Ctrl + Y', actionKey: 'workflow.help.keyboard.redo' },
  { key: 'Ctrl + C', actionKey: 'workflow.help.keyboard.copy' },
  { key: 'Ctrl + X', actionKey: 'workflow.help.keyboard.cut' },
  { key: 'Ctrl + V', actionKey: 'workflow.help.keyboard.paste' },
  { key: 'Ctrl + D', actionKey: 'workflow.help.keyboard.duplicate' },
  { key: 'Ctrl + A', actionKey: 'workflow.help.keyboard.select_all' },
  { key: 'Ctrl + S', actionKey: 'workflow.help.keyboard.save' },
  { key: 'Ctrl + 0', actionKey: 'workflow.help.keyboard.fit' },
  { key: 'Delete', actionKey: 'workflow.help.keyboard.delete' },
  { key: 'Escape', actionKey: 'workflow.help.keyboard.escape' },
  { key: '+', actionKey: 'workflow.help.keyboard.zoom_in' },
  { key: '−', actionKey: 'workflow.help.keyboard.zoom_out' },
  { key: 'Space + drag', actionKey: 'workflow.help.keyboard.pan' },
  { key: 'Shift + click', actionKey: 'workflow.help.keyboard.multi_select' },
];
