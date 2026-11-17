// SPDX-License-Identifier: AGPL-3.0-or-later
import type {
  CavePotentialDefinition,
  CavePotentialMethod,
  CavePotentialModelInfo as ModelInfo,
  EvidenceLayerInfo,
  EvidenceTransform,
  EvidenceTransformKind,
} from '../../api/hooks/cavePotential.ts';
import type { SavedFilterAudience } from '../../filters/savedFilterAudience.ts';

export type { CavePotentialDefinition, CavePotentialMethod, EvidenceLayerInfo, EvidenceTransformKind };

/** One row of a classes table as it is being typed: either number may still be missing. */
export interface ClassDraft {
  upperBound: number | null;
  score: number | null;
}

/**
 * One evidence layer of a model as the form holds it.
 *
 * Every way of scoring keeps its own values while another is chosen, so switching from a ramp to a
 * table and back does not throw away what was typed; only the chosen one is written.
 */
export interface LayerDraft {
  layer: string;
  kind: EvidenceTransformKind;
  classes: ClassDraft[];
  low: number | null;
  high: number | null;
  /** The score of each class of a categorical layer, in the layer's own order; null is "not scored". */
  categories: Record<string, number | null>;
  /** The weight given outright. Kept while the model is weighed by comparisons, and not written then. */
  weight: number | null;
}

/** A model as the form holds it. */
export interface ModelDraft {
  name: string;
  description: string;
  method: CavePotentialMethod;
  /** Who the model is shared with; the same three audiences a saved filter has. */
  audience: SavedFilterAudience;
  layers: LayerDraft[];
  /**
   * The pairwise judgments, each under the two layer codes it compares (see {@link judgmentKey}).
   * Held by code and not by position so that taking a layer out leaves every other judgment where
   * it was; a pair with no entry is "equally important".
   */
  judgments: Record<string, number>;
}

/**
 * The scale pairwise judgments are made on: nine down to one ninth. A judgment is chosen from
 * these and never typed, because the method is defined on this scale and on nothing between.
 */
export const JUDGMENT_CODES = [
  '9', '8', '7', '6', '5', '4', '3', '2', '1', '1/2', '1/3', '1/4', '1/5', '1/6', '1/7', '1/8', '1/9',
] as const;
export type JudgmentCode = (typeof JUDGMENT_CODES)[number];

/** How many times more important the first of a pair is, for a step of the scale. */
export function judgmentValue(code: JudgmentCode): number {
  return code.startsWith('1/') ? 1 / Number(code.slice(2)) : Number(code);
}

/**
 * The step of the scale a stored judgment stands on. The nearest one, because a ninth is stored as
 * a decimal and comes back as 0.111…, which is no step's exact value.
 */
export function judgmentCode(value: number): JudgmentCode {
  let best: JudgmentCode = '1';
  let distance = Infinity;
  for (const code of JUDGMENT_CODES) {
    // Compared on a logarithmic scale, where a ninth is as far from one as nine is.
    const d = Math.abs(Math.log(judgmentValue(code)) - Math.log(value));
    if (d < distance) {
      best = code;
      distance = d;
    }
  }
  return best;
}

/** The entry a judgment between two layers is held under: the first compared with the second. */
export function judgmentKey(first: string, second: string): string {
  return `${first}|${second}`;
}

/** How many times more important `first` is than `second`; one where nothing was said. */
export function judgmentOf(judgments: Record<string, number>, first: string, second: string): number {
  const direct = judgments[judgmentKey(first, second)];
  if (direct !== undefined) {
    return direct;
  }
  const reverse = judgments[judgmentKey(second, first)];
  return reverse !== undefined && reverse > 0 ? 1 / reverse : 1;
}

/** The judgments with one changed. The pair's entry the other way round, if any, is dropped. */
export function withJudgment(
  judgments: Record<string, number>,
  first: string,
  second: string,
  value: number,
): Record<string, number> {
  const next = { ...judgments, [judgmentKey(first, second)]: value };
  delete next[judgmentKey(second, first)];
  return next;
}

/** A model nobody has written anything into yet. */
export function emptyDraft(): ModelDraft {
  return {
    name: '',
    description: '',
    method: 'directWeights',
    audience: { visibility: 'private' },
    layers: [],
    judgments: {},
  };
}

/** A layer newly added to a model, scored the first way the layer allows and otherwise blank. */
export function newLayerDraft(info: EvidenceLayerInfo): LayerDraft {
  return {
    layer: info.code,
    kind: info.transforms.includes('fuzzyLinear') ? 'fuzzyLinear' : (info.transforms[0] ?? 'classes'),
    // A table has at least two rows, and its last row has no upper bound.
    classes: [
      { upperBound: null, score: null },
      { upperBound: null, score: null },
    ],
    low: null,
    high: null,
    categories: Object.fromEntries(info.categories.map((code) => [code, null])),
    weight: 1,
  };
}

/** A stored model as the form holds it. A model whose definition cannot be read opens with no layers. */
export function draftOf(model: ModelInfo, infos: readonly EvidenceLayerInfo[]): ModelDraft {
  const stored = model.definition?.layers ?? [];
  const layers = stored.flatMap((setting): LayerDraft[] => {
    const info = infos.find((candidate) => candidate.code === setting.layer);
    if (info === undefined) {
      return [];
    }
    const blank = newLayerDraft(info);
    const scores = new Map((setting.transform.categories ?? []).map((row) => [row.code, row.score]));
    return [{
      ...blank,
      kind: setting.transform.kind,
      classes: setting.transform.classes?.map((row) => ({ upperBound: row.upperBound, score: row.score }))
        ?? blank.classes,
      low: setting.transform.low ?? null,
      high: setting.transform.high ?? null,
      categories: Object.fromEntries(info.categories.map((code) => [code, scores.get(code) ?? null])),
      weight: setting.weight ?? 1,
    }];
  });

  const judgments: Record<string, number> = {};
  // The stored rows are read against the stored order; a layer this version does not know is
  // skipped above, and its judgments with it.
  stored.forEach((first, i) => {
    stored.slice(i + 1).forEach((second, k) => {
      const value = model.definition?.pairwise?.[i]?.[k];
      if (value !== undefined && value > 0) {
        judgments[judgmentKey(first.layer, second.layer)] = value;
      }
    });
  });

  const audience: SavedFilterAudience = model.visibility === 'cavingGroup' && model.cavingGroupId
    ? { visibility: 'cavingGroup', cavingGroupId: model.cavingGroupId }
    : model.visibility === 'authenticated'
      ? { visibility: 'authenticated' }
      : { visibility: 'private' };

  return {
    name: model.name,
    description: model.description ?? '',
    method: model.method,
    audience,
    layers,
    judgments,
  };
}

function transformOf(layer: LayerDraft): EvidenceTransform {
  if (layer.kind === 'fuzzyLinear') {
    return { kind: 'fuzzyLinear', low: layer.low, high: layer.high };
  }
  if (layer.kind === 'categories') {
    return {
      kind: 'categories',
      // A class left without a score is left out, which the server reads as nought.
      categories: Object.entries(layer.categories)
        .filter(([, score]) => score !== null)
        .map(([code, score]) => ({ code, score })),
    };
  }
  return {
    kind: 'classes',
    // The last row takes everything above the one before, whatever was left in its bound.
    classes: layer.classes.map((row, i) => ({
      upperBound: i === layer.classes.length - 1 ? null : row.upperBound,
      score: row.score,
    })),
  };
}

/** The definition a draft is written as: weights for one method, comparisons for the other, never both. */
export function definitionOf(draft: ModelDraft): CavePotentialDefinition {
  const pairwise = draft.method === 'pairwiseComparison';
  return {
    layers: draft.layers.map((layer) => ({
      layer: layer.layer,
      transform: transformOf(layer),
      ...(pairwise ? {} : { weight: layer.weight }),
    })),
    ...(pairwise
      ? {
          pairwise: draft.layers.slice(0, -1).map((first, i) =>
            draft.layers.slice(i + 1).map((second) => judgmentOf(draft.judgments, first.layer, second.layer)),
          ),
        }
      : {}),
  };
}

const isNumber = (value: number | null): value is number => value !== null && Number.isFinite(value);
const isScore = (value: number | null) => isNumber(value) && value >= 0 && value <= 1;

function layerFilledIn(layer: LayerDraft, directWeights: boolean): boolean {
  if (directWeights && !(isNumber(layer.weight) && layer.weight > 0)) {
    return false;
  }
  if (layer.kind === 'fuzzyLinear') {
    return isNumber(layer.low) && isNumber(layer.high) && layer.low !== layer.high;
  }
  if (layer.kind === 'categories') {
    const scores = Object.values(layer.categories).filter((score) => score !== null);
    return scores.length > 0 && scores.every(isScore);
  }
  if (layer.classes.length < 2) {
    return false;
  }
  let previous = -Infinity;
  return layer.classes.every((row, i) => {
    if (!isScore(row.score)) {
      return false;
    }
    if (i === layer.classes.length - 1) {
      return true;
    }
    if (!isNumber(row.upperBound) || row.upperBound <= previous) {
      return false;
    }
    previous = row.upperBound;
    return true;
  });
}

/**
 * Whether every blank of the form has been filled in — which is when it is worth asking the server
 * what the model's weights come to, and when saving is offered.
 *
 * Not the rule for what a model may be: the server decides that when the model is saved and says
 * what is wrong in its own words. This only keeps a half-typed form from being sent.
 */
export function isFilledIn(draft: ModelDraft): boolean {
  return draft.layers.length > 0
    && draft.layers.every((layer) => layerFilledIn(layer, draft.method === 'directWeights'));
}

/**
 * The map's address looking at a result: its camera over the middle of the grid, far enough out to
 * hold all of it. The plain map for a result that has no grid yet.
 */
export function mapAddressOf(result: {
  west: number | null;
  south: number | null;
  east: number | null;
  north: number | null;
}): string {
  const { west, south, east, north } = result;
  if (west === null || south === null || east === null || north === null) {
    return '/map';
  }
  // The web map's own arithmetic: at zoom nought the world is 256 pixels across, and each step
  // doubles it. Six hundred pixels is a width every layout of the map has to spare.
  const span = Math.max(east - west, north - south, 1e-6);
  const zoom = Math.min(17, Math.max(3, Math.log2((600 * 360) / (256 * span))));
  const lat = (south + north) / 2;
  const lon = (west + east) / 2;
  return `/map#${zoom.toFixed(2)}/${lat.toFixed(5)}/${lon.toFixed(5)}`;
}
