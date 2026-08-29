// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ComponentType } from 'react';
import {
  CompassOutlined,
  EnvironmentOutlined,
  FileTextOutlined,
  FlagOutlined,
  FolderOutlined,
  GlobalOutlined,
  LinkOutlined,
  NodeIndexOutlined,
  PushpinOutlined,
  TeamOutlined,
  UserOutlined,
} from '@ant-design/icons';
import type { TFunction } from 'i18next';
import type { AnchorKind, ResLinkAnchorState, ResLinkTargetDisplay } from '../../api/hooks.ts';
import type { AnchorEditorProps } from './anchorTypes.ts';
import { formatMediaTime } from './anchorTypes.ts';
import {
  PageAnchorEditor,
  PageRangeAnchorEditor,
  TimePointAnchorEditor,
  TimeRangeAnchorEditor,
} from './anchorEditors.tsx';
import TextRangeAnchorEditor from './TextRangeAnchorEditor.tsx';
import {
  validatePageAnchor,
  validatePageRangeAnchor,
  validateTextRangeAnchor,
  validateTimePointAnchor,
  validateTimeRangeAnchor,
} from './anchorRules.ts';

/**
 * The one place that knows what a link member *is*: which icon and name a target type
 * wears, where its chip navigates, how each anchor kind reads in a sentence, and which
 * anchor kinds already have an editor. Everything that renders a member goes through the
 * tables below, so shipping a new selector (image regions, text selections, survey
 * stations, waypoints) is a matter of filling in one field of one entry.
 *
 * Both tables are keyed by a closed union and typed as a total `Record`, so a value added
 * on the server stops compiling here until it is given a name, an icon and a summary.
 */

type IconComponent = typeof LinkOutlined;

export type { AnchorEditorProps } from './anchorTypes.ts';
export { formatMediaTime } from './anchorTypes.ts';

export interface TargetTypeEntry {
  icon: IconComponent;
  labelKey: string;
  /**
   * Route to a whole target of this type, or null when this client has no page for it.
   * Only ever consulted for types the server did not route itself (see `memberRoute`).
   */
  route: ((id: string) => string) | null;
}

export interface AnchorKindEntry {
  /** Compact chip summary, or null for an anchor that addresses the whole resource. */
  summary: ((anchor: unknown, t: TFunction) => string) | null;
  /** Editor for composing this anchor, or null while its selector waits for its viewer. */
  editor: ComponentType<AnchorEditorProps> | null;
  /**
   * Why the composed payload would be refused, or null when it would be accepted. Mirrors
   * the server's own rule for the kind, so the form can say it before the round trip.
   */
  validate: ((anchor: unknown, t: TFunction) => string | null) | null;
}

/** Target types the server accepts, in the wire spelling it parses and emits. */
export const RESLINK_TARGET_TYPES = [
  'feature',
  'document',
  'tripLog',
  'caver',
  'cavingGroup',
  'mapView',
  'surveyModel',
  'geofile',
  'cabinet',
  'expedition',
] as const;

export type ResLinkTargetType = (typeof RESLINK_TARGET_TYPES)[number];

/**
 * Six of the ten worlds have no detail page in this client yet; their entries route to
 * null on purpose. A chip for one of them renders without navigation rather than dropping
 * the reader on a list page that is not the thing they clicked. The count is a fact about
 * this client, not about the vocabulary: an entry stops routing to null the day the page
 * it would open is shipped.
 */
const targetTypes: Record<ResLinkTargetType, TargetTypeEntry> = {
  feature: {
    icon: EnvironmentOutlined,
    labelKey: 'resLinks.targetTypes.feature',
    route: (id) => `/features/${id}`,
  },
  document: {
    icon: FileTextOutlined,
    labelKey: 'resLinks.targetTypes.document',
    route: (id) => `/documents/${id}`,
  },
  tripLog: {
    icon: CompassOutlined,
    labelKey: 'resLinks.targetTypes.tripLog',
    route: (id) => `/trip-logs/${id}`,
  },
  caver: { icon: UserOutlined, labelKey: 'resLinks.targetTypes.caver', route: null },
  cavingGroup: { icon: TeamOutlined, labelKey: 'resLinks.targetTypes.cavingGroup', route: null },
  mapView: { icon: GlobalOutlined, labelKey: 'resLinks.targetTypes.mapView', route: null },
  surveyModel: { icon: NodeIndexOutlined, labelKey: 'resLinks.targetTypes.surveyModel', route: null },
  geofile: { icon: PushpinOutlined, labelKey: 'resLinks.targetTypes.geofile', route: null },
  cabinet: { icon: FolderOutlined, labelKey: 'resLinks.targetTypes.cabinet', route: null },
  expedition: {
    icon: FlagOutlined,
    labelKey: 'resLinks.targetTypes.expedition',
    route: (id) => `/expeditions/${id}`,
  },
};

/** What a member of a type this client has never heard of falls back to. */
const unknownTargetType: TargetTypeEntry = {
  icon: LinkOutlined,
  labelKey: 'resLinks.targetTypes.unknown',
  route: null,
};

export function isResLinkTargetType(value: string): value is ResLinkTargetType {
  return Object.hasOwn(targetTypes, value);
}

export function targetTypeEntry(targetType: string): TargetTypeEntry {
  return isResLinkTargetType(targetType) ? targetTypes[targetType] : unknownTargetType;
}

/**
 * Where a member chip navigates. The server's own answer wins whenever it has one: it
 * knows that a cave feature belongs on the cave page rather than the generic feature page,
 * and that a survey model is opened through the cave that holds it. The table above only
 * covers what the server leaves unset.
 */
export function memberRoute(
  targetType: string,
  targetId: string,
  display?: ResLinkTargetDisplay | null,
): string | null {
  if (display?.route) {
    return display.route;
  }
  return targetTypeEntry(targetType).route?.(targetId) ?? null;
}

/** A link's own page, addressed by the short code it keeps for life. */
export function linkPageRoute(shortCode: string): string {
  return `/links/${shortCode}`;
}

/** The same page as something pasteable into a chat window. */
export function linkPageUrl(shortCode: string): string {
  return `${window.location.origin}${linkPageRoute(shortCode)}`;
}

// --- anchor payload readers -------------------------------------------------
// The payload travels as free-form JSON, so every read is defensive: a member written by
// a newer server, or one whose payload the server withheld, must degrade to a generic
// label instead of throwing inside a chip.

function field(anchor: unknown, key: string): unknown {
  return typeof anchor === 'object' && anchor !== null
    ? (anchor as Record<string, unknown>)[key]
    : undefined;
}

function num(anchor: unknown, key: string): number | null {
  const value = field(anchor, key);
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

function text(anchor: unknown, key: string): string | null {
  const value = field(anchor, key);
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : null;
}

const anchorKinds: Record<AnchorKind, AnchorKindEntry> = {
  whole: { summary: null, editor: null, validate: null },
  textRange: {
    summary: (anchor, t) => {
      const page = num(anchor, 'page');
      return page === null
        ? t('resLinks.anchors.quote')
        : t('resLinks.anchors.quoteOnPage', { page });
    },
    editor: TextRangeAnchorEditor,
    validate: validateTextRangeAnchor,
  },
  page: {
    summary: (anchor, t) => {
      const page = num(anchor, 'page');
      return page === null ? t('resLinks.anchors.part') : t('resLinks.anchors.page', { page });
    },
    editor: PageAnchorEditor,
    validate: validatePageAnchor,
  },
  pageRange: {
    summary: (anchor, t) => {
      const from = num(anchor, 'fromPage');
      const to = num(anchor, 'toPage');
      return from === null || to === null
        ? t('resLinks.anchors.part')
        : t('resLinks.anchors.pageRange', { from, to });
    },
    editor: PageRangeAnchorEditor,
    validate: validatePageRangeAnchor,
  },
  imageRegion: { summary: (_anchor, t) => t('resLinks.anchors.region'), editor: null, validate: null },
  timePoint: {
    summary: (anchor, t) => {
      const at = num(anchor, 't');
      return at === null ? t('resLinks.anchors.part') : formatMediaTime(at);
    },
    editor: TimePointAnchorEditor,
    validate: validateTimePointAnchor,
  },
  timeRange: {
    summary: (anchor, t) => {
      const start = num(anchor, 'start');
      const end = num(anchor, 'end');
      return start === null || end === null
        ? t('resLinks.anchors.part')
        : `${formatMediaTime(start)}–${formatMediaTime(end)}`;
    },
    editor: TimeRangeAnchorEditor,
    validate: validateTimeRangeAnchor,
  },
  modelStation: {
    summary: (anchor, t) => {
      const station = text(anchor, 'station');
      return station === null
        ? t('resLinks.anchors.part')
        : t('resLinks.anchors.station', { station });
    },
    editor: null,
    validate: null,
  },
  modelStationRange: {
    summary: (anchor, t) => {
      const from = text(anchor, 'fromStation');
      const to = text(anchor, 'toStation');
      return from === null || to === null
        ? t('resLinks.anchors.part')
        : t('resLinks.anchors.stationRange', { from, to });
    },
    editor: null,
    validate: null,
  },
  modelSurvey: {
    summary: (anchor, t) => {
      const survey = text(anchor, 'survey');
      return survey === null ? t('resLinks.anchors.part') : t('resLinks.anchors.survey', { survey });
    },
    editor: null,
    validate: null,
  },
  modelSurveyRange: {
    summary: (anchor, t) => {
      const from = text(anchor, 'fromSurvey');
      const to = text(anchor, 'toSurvey');
      return from === null || to === null
        ? t('resLinks.anchors.part')
        : t('resLinks.anchors.surveyRange', { from, to });
    },
    editor: null,
    validate: null,
  },
  modelPoint: { summary: (_anchor, t) => t('resLinks.anchors.modelPoint'), editor: null, validate: null },
  waypoint: {
    summary: (anchor, t) => {
      const name = text(anchor, 'name');
      if (name !== null) {
        return t('resLinks.anchors.waypointNamed', { name });
      }
      const index = num(anchor, 'index');
      return index === null
        ? t('resLinks.anchors.part')
        : t('resLinks.anchors.waypoint', { index });
    },
    editor: null,
    validate: null,
  },
  waypointRange: {
    summary: (anchor, t) => {
      const from = num(anchor, 'fromIndex');
      const to = num(anchor, 'toIndex');
      return from === null || to === null
        ? t('resLinks.anchors.part')
        : t('resLinks.anchors.waypointRange', { from, to });
    },
    editor: null,
    validate: null,
  },
};

/**
 * Every anchor kind, derived from the table rather than listed again — the table is total
 * over the generated union, so this cannot drift from what the server can send.
 */
export const RESLINK_ANCHOR_KINDS = Object.keys(anchorKinds) as AnchorKind[];

export function isAnchorKind(value: string): value is AnchorKind {
  return Object.hasOwn(anchorKinds, value);
}

export function anchorKindEntry(anchorKind: string): AnchorKindEntry | null {
  return isAnchorKind(anchorKind) ? anchorKinds[anchorKind] : null;
}

/**
 * The compact part label a chip carries — null when the member addresses its target whole.
 * A kind this client does not know, and a payload it cannot read, both degrade to a
 * generic "part": an old client must keep rendering members a newer server writes.
 */
export function anchorSummary(anchorKind: string, anchor: unknown, t: TFunction): string | null {
  const entry = anchorKindEntry(anchorKind);
  if (entry === null) {
    return t('resLinks.anchors.part');
  }
  return entry.summary === null ? null : entry.summary(anchor, t);
}

/**
 * Which anchor kinds a target of this type accepts, whole-resource first. This mirrors the
 * server's own matrix: a kind offered here that the server refuses would be a form that
 * cannot be submitted, and one it accepts but that is missing here is a capability the user
 * never sees. A type this client does not know is offered the whole-resource anchor only,
 * which every type accepts.
 */
const admittedAnchors: Record<ResLinkTargetType, readonly AnchorKind[]> = {
  feature: ['whole'],
  document: [
    'whole',
    'textRange',
    'page',
    'pageRange',
    'imageRegion',
    'timePoint',
    'timeRange',
    'modelPoint',
  ],
  tripLog: ['whole'],
  caver: ['whole'],
  cavingGroup: ['whole'],
  mapView: ['whole'],
  surveyModel: ['whole', 'modelStation', 'modelStationRange', 'modelSurvey', 'modelSurveyRange'],
  geofile: ['whole', 'waypoint', 'waypointRange'],
  cabinet: ['whole'],
  expedition: ['whole'],
};

export function admittedAnchorKinds(targetType: string): readonly AnchorKind[] {
  return isResLinkTargetType(targetType) ? admittedAnchors[targetType] : ['whole'];
}

/**
 * Whether an anchor kind can be composed today. A kind with no editor is one whose selector
 * rides a viewer this client has not shipped yet — it is still listed, disabled, so the
 * scope is visible rather than silently absent.
 */
export function canComposeAnchor(anchorKind: AnchorKind): boolean {
  return anchorKind === 'whole' || anchorKinds[anchorKind].editor !== null;
}

/** Why a composed anchor would be refused, or null when it is ready to send. */
export function anchorProblem(anchorKind: string, anchor: unknown, t: TFunction): string | null {
  const entry = anchorKindEntry(anchorKind);
  return entry?.validate ? entry.validate(anchor, t) : null;
}

/**
 * What to say about how well the anchor still resolves. An exact anchor says nothing —
 * the common case earns no chrome. The rest indicate rather than mislead: a selection
 * measured against a superseded file is shown as such instead of being silently re-aimed.
 */
export function anchorStateNote(state: ResLinkAnchorState, t: TFunction): string | null {
  const notes: Record<ResLinkAnchorState, string | null> = {
    exact: null,
    reanchored: t('resLinks.anchorStates.reanchored'),
    degraded: t('resLinks.anchorStates.degraded'),
    unresolvable: t('resLinks.anchorStates.unresolvable'),
  };
  return notes[state] ?? null;
}
