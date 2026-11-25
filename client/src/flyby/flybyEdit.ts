// SPDX-License-Identifier: AGPL-3.0-or-later
import { FILTER_LAYER_COLORS, FILTER_LAYER_SYMBOLS } from '../map/filterLayerList.ts';
import {
  flybyFeatureLayerKey,
  FLYBY_MAX_SECTIONS,
  type FlybyChange,
  type FlybyChangeKind,
  type FlybyDocument,
  type FlybyFeatureLayer,
  type FlybyLayerKey,
  type FlybyLayerLook,
  type FlybyLook,
  type FlybyPose,
  type FlybySection,
  type FlybySectionKind,
  type FlybySubjectRef,
} from './flybyDocument.ts';

// Editing a flyby's sections by hand: adding one of a kind, taking one out, copying one, moving one
// up or down the list, and changing what one does.
//
// Every edit takes a whole document and gives a whole new one back, so the panel only ever holds a
// document and never half of an edit. What is tied to a section — its timed changes of the look, its
// caption — goes where the section goes: out with it when it is removed, and nowhere when it is
// only moved, because a change is tied to its section by id and not by place in the list.

/** The kinds a person can add from the panel, in the order the picker offers them. */
export const FLYBY_ADDABLE_KINDS: readonly FlybySectionKind[] = [
  'approach',
  'orbit',
  'hop',
  'tilt',
  'hold',
  'dive',
  'descent',
  'rise',
];

/** What a new section of each kind does, and for how long, before anybody changes it. */
const DEFAULTS: Record<FlybySectionKind, { seconds: number; to: FlybyPose }> = {
  approach: { seconds: 6, to: { pitch: -32, range: 2.8, lift: 0 } },
  orbit: { seconds: 24, to: { headingBy: 360 } },
  hop: { seconds: 8, to: { pitch: -32, range: 2.8, lift: 0, clearance: 60 } },
  dive: { seconds: 9, to: { aim: 'bottom', pitch: -32, range: 0.5, clearance: null } },
  rise: { seconds: 6, to: { aim: 'centre', pitch: -32, range: 2.8, lift: 0, clearance: 60 } },
  hold: { seconds: 3, to: {} },
  tilt: { seconds: 7, to: { pitch: -8 } },
  keyframe: { seconds: 6, to: { range: 1, lift: 0, aim: 'centre' } },
  descent: { seconds: 30, to: { aim: 'bottom', pitch: -30, range: 0.15, clearance: null } },
  follow: { seconds: 60, to: { pitch: -22, rangeM: 250, clearance: 60 } },
  fromFar: { seconds: 14, to: { pitch: -30, range: 4 } },
};

/** The defaults a new section of `kind` starts from. */
export function sectionDefaults(kind: FlybySectionKind): { seconds: number; to: FlybyPose } {
  const defaults = DEFAULTS[kind];
  return { seconds: defaults.seconds, to: { ...defaults.to } };
}

/** An id for a new section of `kind` that no section of the document has: `orbit-7`. */
export function newSectionId(document: Pick<FlybyDocument, 'sections'>, kind: FlybySectionKind): string {
  const taken = new Set(document.sections.map((section) => section.id));
  // Numbered on from the highest number in use, so ids keep reading in the order they were made.
  let next = 1 + Math.max(0, ...document.sections.map((section) => Number(/-(\d+)$/.exec(section.id)?.[1] ?? 0)));
  while (taken.has(`${kind}-${next}`)) {
    next += 1;
  }
  return `${kind}-${next}`;
}

/**
 * Adds a section of `kind` after the one at `after` — at the end when left out — about the same
 * subject as the section before it. A hop goes on to the next subject, since a hop to where the
 * camera already is would be a plain move.
 */
export function addSection(document: FlybyDocument, kind: FlybySectionKind, after?: number): FlybyDocument {
  if (document.sections.length >= FLYBY_MAX_SECTIONS) {
    return document;
  }
  const at = after === undefined ? document.sections.length : Math.min(Math.max(after + 1, 0), document.sections.length);
  const before = at > 0 ? document.sections[at - 1].subject : document.start.subject;
  let subject: FlybySubjectRef = before;
  if (kind === 'hop' && document.subjects.length > 1) {
    subject = before === 'all' ? 0 : (before + 1) % document.subjects.length;
  }
  const defaults = sectionDefaults(kind);
  const section: FlybySection = { id: newSectionId(document, kind), kind, subject, ...defaults };
  return { ...document, sections: [...document.sections.slice(0, at), section, ...document.sections.slice(at)] };
}

/** Takes a section out, and with it the changes and the captions that belong to it. */
export function removeSection(document: FlybyDocument, id: string): FlybyDocument {
  const next: FlybyDocument = { ...document, sections: document.sections.filter((section) => section.id !== id) };
  if (document.changes) {
    next.changes = document.changes.filter((change) => change.section !== id);
  }
  if (document.texts?.captions) {
    next.texts = { ...document.texts, captions: document.texts.captions.filter((caption) => caption.section !== id) };
  }
  return next;
}

/** A copy of a section, straight after it, under a new id. What belongs to the original stays with it. */
export function duplicateSection(document: FlybyDocument, id: string): FlybyDocument {
  const index = document.sections.findIndex((section) => section.id === id);
  if (index < 0 || document.sections.length >= FLYBY_MAX_SECTIONS) {
    return document;
  }
  const original = document.sections[index];
  const copy: FlybySection = { ...original, id: newSectionId(document, original.kind), to: { ...original.to } };
  return {
    ...document,
    sections: [...document.sections.slice(0, index + 1), copy, ...document.sections.slice(index + 1)],
  };
}

/** Moves a section `by` places up (negative) or down the list; past either end it stops there. */
export function moveSection(document: FlybyDocument, id: string, by: number): FlybyDocument {
  const index = document.sections.findIndex((section) => section.id === id);
  if (index < 0) {
    return document;
  }
  const to = Math.min(Math.max(index + by, 0), document.sections.length - 1);
  if (to === index) {
    return document;
  }
  const sections = [...document.sections];
  const [moved] = sections.splice(index, 1);
  sections.splice(to, 0, moved);
  return { ...document, sections };
}

/** What can be changed of a section from the panel. */
export type FlybySectionPatch = Partial<Pick<FlybySection, 'kind' | 'subject' | 'seconds' | 'speed' | 'rest' | 'say'>> & {
  /** Members of the pose to set; a member given as undefined is taken out, so it carries over from before. */
  to?: { [K in keyof FlybyPose]?: FlybyPose[K] | undefined };
};

/**
 * Changes what one section does. A pose member set to undefined is removed — the section then says
 * nothing about it and the camera keeps what the section before left — which is different from a
 * clearance of null, which says the camera may go underground.
 */
export function updateSection(document: FlybyDocument, id: string, patch: FlybySectionPatch): FlybyDocument {
  return {
    ...document,
    sections: document.sections.map((section) => {
      if (section.id !== id) {
        return section;
      }
      const { to, ...rest } = patch;
      const next: FlybySection = { ...section, ...rest, to: { ...section.to } };
      for (const [key, value] of Object.entries(to ?? {}) as [keyof FlybyPose, FlybyPose[keyof FlybyPose] | undefined][]) {
        if (value === undefined) {
          delete next.to[key];
        } else {
          (next.to as Record<string, unknown>)[key] = value;
        }
      }
      // A turn is said one way or the other: a heading to face, or an amount to turn by.
      if (to && 'headingBy' in to && to.headingBy !== undefined) {
        delete next.to.heading;
      }
      if (to && 'heading' in to && to.heading !== undefined) {
        delete next.to.headingBy;
      }
      if (next.speed === 1) {
        delete next.speed;
      }
      if (!next.rest) {
        delete next.rest;
      }
      if (!next.say) {
        delete next.say;
      }
      return next;
    }),
  };
}

// ---- the look, by hand ----

/** An id for a new change of `kind` that no change of the list has: `walls-4`. */
export function newChangeId(changes: readonly FlybyChange[], kind: FlybyChangeKind): string {
  const taken = new Set(changes.map((change) => change.id));
  let next = 1 + Math.max(0, ...changes.map((change) => Number(/-(\d+)$/.exec(change.id)?.[1] ?? 0)));
  while (taken.has(`${kind}-${next}`)) {
    next += 1;
  }
  return `${kind}-${next}`;
}

/**
 * The opening look with one layer's word on it changed. A member given as undefined is taken out —
 * the layer is then as the viewer has it in that respect — and a layer with nothing left to say is
 * taken out of the look, and the list of layers with it when it is empty.
 */
export function withLayerLook(look: FlybyLook, key: FlybyLayerKey, patch: { visible?: boolean; opacity?: number }): FlybyLook {
  const layer: FlybyLayerLook = { ...look.layers?.[key] };
  for (const member of ['visible', 'opacity'] as const) {
    if (member in patch) {
      if (patch[member] === undefined) {
        delete layer[member];
      } else {
        (layer as Record<string, unknown>)[member] = patch[member];
      }
    }
  }
  const layers = { ...look.layers };
  if (Object.keys(layer).length === 0) {
    delete layers[key];
  } else {
    layers[key] = layer;
  }
  const next: FlybyLook = { ...look, layers };
  if (Object.keys(layers).length === 0) {
    delete next.layers;
  }
  return next;
}

// ---- the flyby's own layers of features ----

/**
 * A new layer of the flyby's own showing `source`: the next free id, and the first colour and
 * symbol no other of its layers has together — colours first, so the first six differ by colour.
 */
export function newFeatureLayer(
  features: readonly FlybyFeatureLayer[],
  source: FlybyFeatureLayer['source'],
): FlybyFeatureLayer {
  const taken = new Set(features.map((layer) => layer.id));
  let number = features.length + 1;
  while (taken.has(`f${number}`)) {
    number += 1;
  }
  const styles = FILTER_LAYER_SYMBOLS.flatMap((symbol) => FILTER_LAYER_COLORS.map((colour) => ({ colour, symbol })));
  const style =
    styles.find((candidate) => !features.some((layer) => layer.colour === candidate.colour && layer.symbol === candidate.symbol)) ??
    styles[features.length % styles.length];
  return { id: `f${number}`, source, ...style };
}

/**
 * The flyby without one of its own layers: out of the list, out of the opening look, and every
 * change that showed, hid or faded it taken out with it — none of them would have anything to change.
 */
export function withoutFeatureLayer(
  parts: { look: FlybyLook; changes: readonly FlybyChange[]; features: readonly FlybyFeatureLayer[] },
  id: string,
): { look: FlybyLook; changes: FlybyChange[]; features: FlybyFeatureLayer[] } {
  const key = flybyFeatureLayerKey(id);
  return {
    look: withLayerLook(parts.look, key, { visible: undefined, opacity: undefined }),
    changes: parts.changes.filter((change) => !((change.kind === 'layer' || change.kind === 'layerOpacity') && change.layer === key)),
    features: parts.features.filter((layer) => layer.id !== id),
  };
}
