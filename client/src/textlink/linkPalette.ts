// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * What colour a highlighted passage wears, and why it is decided here rather than stored.
 *
 * The relation vocabulary is already decorated on this side: seeded relations are translated by
 * code from the language files, and a custom relation shows the name its installation gave it.
 * Colour follows the same rule, which keeps the server out of a question about how a page looks
 * and means an installation that invents a relation gets a legible colour without anybody having
 * to pick one. If an installation ever wants to choose its own, that is one nullable column and
 * this becomes the fallback — the shape does not have to change for it.
 *
 * <b>Every colour is drawn as a wash, never as ink.</b> The words keep the reader's own text
 * colour, and the relation shows as a tint behind them and a line under them. That is what keeps
 * the text legible in both themes without the palette having to know which one is on: a low-alpha
 * wash darkens a light page and lightens a dark one, while coloured text has to be two different
 * colours and is a contrast failure in one of them the day somebody adds a hue.
 */

/** A hue, as the channels a wash is mixed from. */
type Rgb = readonly [number, number, number];

const HUES = {
  teal: [20, 98, 98],
  blue: [37, 99, 235],
  violet: [124, 58, 237],
  amber: [180, 120, 10],
  green: [22, 128, 62],
  rose: [190, 40, 70],
  cyan: [14, 116, 144],
  orange: [194, 89, 24],
  slate: [90, 100, 115],
} as const satisfies Record<string, Rgb>;

type HueName = keyof typeof HUES;

/**
 * The shipped relations, given colours that mean something next to each other rather than being
 * pretty on their own: the two that say "this is the same thing" are cool, the one that asks a
 * question is the only warm red on the page, and the ten trip roles share one hue because they
 * are one vocabulary and a reader tells them apart by reading, not by colour.
 */
const SEEDED: Record<string, HueName> = {
  'same-object': 'teal',
  'related-to': 'blue',
  contains: 'violet',
  documents: 'amber',
  'derived-from': 'cyan',
  'adjacent-to': 'green',
  'duplicate-of': 'slate',
  'needs-clarification': 'rose',
  'text-of': 'cyan',
};

const FALLBACK_ORDER: HueName[] = ['blue', 'violet', 'green', 'amber', 'cyan', 'orange', 'teal', 'rose'];

/** The wash and the line for one passage. */
export interface LinkColors {
  /** Behind the words. */
  background: string;
  /** Under them — what carries the colour when the wash is too faint to read as one. */
  underline: string;
  /** The wash while the pointer is on the passage. */
  hoverBackground: string;
}

/**
 * The colour for a relation code. `null` — an untyped association, which is a legitimate link —
 * gets the neutral hue rather than being given somebody else's.
 */
export function linkColors(relationCode: string | null | undefined, dark: boolean): LinkColors {
  return washOf(HUES[hueFor(relationCode)], dark);
}

function hueFor(relationCode: string | null | undefined): HueName {
  if (!relationCode) {
    return 'slate';
  }

  if (SEEDED[relationCode]) {
    return SEEDED[relationCode];
  }

  // Every trip role is one hue: they are one vocabulary, and ten shades of the same idea is a
  // legend nobody reads.
  if (relationCode.startsWith('trip-')) {
    return 'orange';
  }

  // A relation this installation invented. Hashed rather than assigned in arrival order so the
  // same relation is the same colour in every window and every session — an order-based colour
  // would change when somebody adds a relation above it in the list.
  let hash = 0;
  for (let i = 0; i < relationCode.length; i++) {
    hash = (hash * 31 + relationCode.charCodeAt(i)) | 0;
  }

  return FALLBACK_ORDER[Math.abs(hash) % FALLBACK_ORDER.length];
}

/**
 * A hue turned into a wash. The alphas differ by theme because the same wash over a dark page is
 * a much smaller step in luminance than over a light one, so matching numbers would give a dark
 * page highlights nobody can see.
 */
function washOf([r, g, b]: Rgb, dark: boolean): LinkColors {
  const tint = dark ? 0.34 : 0.16;
  const hover = dark ? 0.5 : 0.28;
  return {
    background: `rgba(${r}, ${g}, ${b}, ${tint})`,
    hoverBackground: `rgba(${r}, ${g}, ${b}, ${hover})`,
    underline: dark ? `rgba(${lift(r)}, ${lift(g)}, ${lift(b)}, 0.95)` : `rgba(${r}, ${g}, ${b}, 0.85)`,
  };
}

/** Brightens a channel for the dark theme, where the base hues are chosen against white. */
function lift(channel: number): number {
  return Math.round(channel + (255 - channel) * 0.45);
}

/**
 * The wash for a passage several links cover at once.
 *
 * Overlaps are drawn as one wash and a stack of lines rather than as a blend. Blending two hues
 * produces a third that is somebody else's relation colour, so a reader would see a passage
 * apparently marked with a relation that is not on it — the one reading of the page that is
 * actively false. A stack says "more than one thing is here", which is what is true, and the
 * hover card is where a reader finds out what they are.
 */
export function stackedColors(colors: readonly LinkColors[], dark: boolean): LinkColors | null {
  if (colors.length === 0) {
    return null;
  }

  if (colors.length === 1) {
    return colors[0];
  }

  return {
    background: washOf(HUES.slate, dark).background,
    hoverBackground: washOf(HUES.slate, dark).hoverBackground,
    underline: colors[0].underline,
  };
}
