// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The arithmetic behind the passage rose, kept out of the drawing component.
 *
 * <p>
 * The rose is the one figure in this application drawn by hand rather than by the charting
 * library, because what makes it right or wrong is the treatment of angles rather than the
 * drawing. A passage running 010° and a passage running 190° are the same trend surveyed from
 * either end, so the diagram is <em>axial</em>: every measurement is folded onto the half-circle
 * 0–180° and then drawn twice, opposed. A rose that treated the two as different directions would
 * be wrong in a way that looks entirely plausible — half the passages of a cave would appear to
 * run one way and half the other, purely from the order the surveyor walked them.
 * </p>
 * <p>
 * Everything here is plain numbers in and plain numbers out, so the claims the diagram makes can
 * be asserted directly rather than inferred from rendered shapes.
 * </p>
 */

/** Which quantity a petal's length stands for. */
export type RoseWeighting = 'count' | 'length';

/**
 * One sector of the folded half-circle and what fell in it.
 *
 * Structural rather than imported: this module works for any source of sector counts, and keeping
 * it independent of the generated API types is what lets the arithmetic be tested on fixtures
 * written by hand.
 */
export interface RoseBin {
  fromDegrees: number;
  toDegrees: number;
  count: number;
  lengthM: number;
  countFraction: number;
  lengthFraction: number;
}

/** A point on the drawing surface, in the same units as the view box. */
export interface RosePoint {
  x: number;
  y: number;
}

/** One drawn wedge. Every measured sector produces two of these, half a turn apart. */
export interface RosePetal {
  /** Where the wedge starts, as a compass bearing on the drawing (0–360°). */
  fromDegrees: number;
  /** Where it ends, as a compass bearing on the drawing (0–360°). */
  toDegrees: number;
  /** The share of the whole this wedge stands for, 0–1. */
  fraction: number;
  /** The measured quantity itself — a count of legs, or a length in metres. */
  value: number;
  /** True for the copy drawn on the far side of the diagram. */
  mirrored: boolean;
}

/** The rings drawn behind the petals, and the value the outermost one stands for. */
export interface RoseScale {
  /** The fraction the outer ring stands for; every petal fits inside it. */
  max: number;
  /** Ring values, ascending, the last equal to `max`. */
  rings: number[];
}

/** What the diagram says, in numbers, so it can be said in words as well as drawn. */
export interface RoseSummary {
  sectorCount: number;
  sectorWidthDegrees: number;
  dominantFromDegrees: number;
  dominantToDegrees: number;
  /** The dominant sector's share of the whole, 0–1. */
  dominantFraction: number;
}

const HALF_TURN = 180;
const FULL_TURN = 360;

/**
 * A bearing reduced to the trend it describes, in [0, 180).
 *
 * This is the whole axial rule in one function: 010° and 190° both answer 10, because they are one
 * passage described from its two ends.
 */
export function foldAxisDegrees(azimuthDegrees: number): number {
  if (!Number.isFinite(azimuthDegrees)) return Number.NaN;
  const wrapped = ((azimuthDegrees % HALF_TURN) + HALF_TURN) % HALF_TURN;
  return wrapped;
}

/**
 * Which sector of the folded half-circle a bearing belongs to.
 *
 * Offered for callers that bin bearings themselves; the sectors this application draws are binned
 * by the server, and both must agree on where a boundary bearing falls — it goes into the sector
 * that starts there.
 */
export function axialBinIndex(azimuthDegrees: number, binCount: number): number {
  if (binCount < 1) return -1;
  const folded = foldAxisDegrees(azimuthDegrees);
  if (!Number.isFinite(folded)) return -1;
  const width = HALF_TURN / binCount;
  return Math.min(binCount - 1, Math.floor(folded / width));
}

function weightOf(bin: RoseBin, weighting: RoseWeighting): number {
  return weighting === 'length' ? bin.lengthM : bin.count;
}

function fractionOf(bin: RoseBin, weighting: RoseWeighting): number {
  const fraction = weighting === 'length' ? bin.lengthFraction : bin.countFraction;
  return Number.isFinite(fraction) && fraction > 0 ? fraction : 0;
}

/**
 * The wedges to draw, two per measured sector.
 *
 * A sector nothing fell in produces no wedge at all rather than one of zero length: an empty
 * sector is a real observation about a cave, and a sliver at the centre would read as a very small
 * one instead of as none.
 */
export function rosePetals(bins: RoseBin[], weighting: RoseWeighting): RosePetal[] {
  const petals: RosePetal[] = [];
  for (const bin of bins) {
    const fraction = fractionOf(bin, weighting);
    if (fraction <= 0) continue;
    const value = weightOf(bin, weighting);
    petals.push({
      fromDegrees: bin.fromDegrees,
      toDegrees: bin.toDegrees,
      fraction,
      value,
      mirrored: false,
    });
    petals.push({
      fromDegrees: (bin.fromDegrees + HALF_TURN) % FULL_TURN,
      toDegrees: bin.toDegrees + HALF_TURN,
      fraction,
      value,
      mirrored: true,
    });
  }
  return petals;
}

/** The next round number at or above a value: 1, 2 or 5 times a power of ten. */
function niceStep(value: number): number {
  if (!(value > 0)) return 0;
  const magnitude = 10 ** Math.floor(Math.log10(value));
  const scaled = value / magnitude;
  const step = scaled <= 1 ? 1 : scaled <= 2 ? 2 : scaled <= 5 ? 5 : 10;
  return step * magnitude;
}

/**
 * The rings the petals are measured against.
 *
 * The outermost ring is a round number at or above the largest sector, so the ring a petal reaches
 * can be read off and named. Without that the diagram shows which direction is strongest but not
 * by how much, which is the question a rose is usually asked.
 */
export function roseScale(bins: RoseBin[], weighting: RoseWeighting): RoseScale {
  let max = 0;
  for (const bin of bins) max = Math.max(max, fractionOf(bin, weighting));
  if (max <= 0) return { max: 0, rings: [] };

  // Three rings is enough to read a proportion against and few enough to stay out of the way.
  const step = niceStep(max / 3);
  const rings: number[] = [];
  // Counted rather than accumulated, so the ring values are exact multiples of the step instead of
  // drifting by a floating-point crumb per ring and printing as 0.30000000000000004.
  for (let i = 1; rings.length === 0 || rings[rings.length - 1] < max; i += 1) {
    rings.push(step * i);
    // A malformed step would loop for ever; a step of zero is already excluded above, and this
    // guards the case where max is not finite.
    if (i > 1000) break;
  }
  return { max: rings[rings.length - 1], rings };
}

/**
 * Where a compass bearing lands on the drawing, north up and bearings increasing clockwise.
 *
 * The y axis of the drawing surface grows downwards, which is why north is a subtraction: writing
 * this the way the trigonometry is usually written produces a diagram that is correct but upside
 * down, and nothing about the result looks wrong until a cave with a known trend is put in it.
 */
export function bearingPoint(degrees: number, radius: number, centre: RosePoint): RosePoint {
  const radians = (degrees * Math.PI) / HALF_TURN;
  return {
    x: centre.x + radius * Math.sin(radians),
    y: centre.y - radius * Math.cos(radians),
  };
}

/** A wedge from the centre out to `radius`, spanning the bearings given. */
export function wedgePath(fromDegrees: number, toDegrees: number, radius: number, centre: RosePoint): string {
  const start = bearingPoint(fromDegrees, radius, centre);
  const end = bearingPoint(toDegrees, radius, centre);
  const largeArc = Math.abs(toDegrees - fromDegrees) > HALF_TURN ? 1 : 0;
  const n = (value: number) => value.toFixed(2);
  // Sweep 1 is clockwise on a surface whose y grows downwards, which is the direction bearings run.
  return [
    `M ${n(centre.x)} ${n(centre.y)}`,
    `L ${n(start.x)} ${n(start.y)}`,
    `A ${n(radius)} ${n(radius)} 0 ${largeArc} 1 ${n(end.x)} ${n(end.y)}`,
    'Z',
  ].join(' ');
}

/**
 * What the diagram amounts to, or nothing at all when no direction was measured.
 *
 * A caller that gets `null` here has a cave whose line work told it nothing about direction, and
 * must say so rather than draw an empty set of rings.
 */
export function roseSummary(bins: RoseBin[], weighting: RoseWeighting): RoseSummary | null {
  let dominant: RoseBin | null = null;
  let dominantFraction = 0;
  for (const bin of bins) {
    const fraction = fractionOf(bin, weighting);
    if (fraction > dominantFraction) {
      dominant = bin;
      dominantFraction = fraction;
    }
  }
  if (!dominant) return null;

  return {
    sectorCount: bins.length,
    sectorWidthDegrees: dominant.toDegrees - dominant.fromDegrees,
    dominantFromDegrees: dominant.fromDegrees,
    dominantToDegrees: dominant.toDegrees,
    dominantFraction,
  };
}
