// SPDX-License-Identifier: AGPL-3.0-or-later

// A presentation scenario: what a scripted demonstration of the application does, and when.
//
// Plain data on purpose. The same scenario is played by a recorder outside the browser that takes
// one picture per movie frame, by the same recorder in real time on a machine that can draw fast
// enough, and in a person's own browser while they record the screen themselves — so it has to
// travel as JSON, and everything it can ask for is a named kind below rather than a function.
//
// Every item has a start (`at`, seconds from the start of the movie) and a duration. Items may
// overlap, which is how "the camera keeps turning while the basemap is changed" is written; two
// items that would steer the same thing at once (two camera moves, two pointer actions) are refused
// when the scenario is read, not discovered half-way through a long recording.

/**
 * How a change runs over its time. `smooth` is for camera moves: a run of `smooth` moves placed back
 * to back is flown as one curve through their poses, without slowing at the joins; it starts and
 * ends at rest. Anything else given `smooth` runs `inOut`.
 */
export type Ease = 'linear' | 'in' | 'out' | 'inOut' | 'smooth';

/**
 * Where the camera is, described around the loaded cave model rather than in coordinates, so a
 * scenario names no place.
 */
export interface OrbitPose {
  /** Compass direction the camera faces, degrees. */
  heading?: number;
  /** Turns the heading by this much from where the previous move left it, degrees. */
  headingBy?: number;
  /** Angle below the horizon, degrees (-90 looks straight down). */
  pitch?: number;
  /**
   * Distance from the point looked at, in radii of the model's bounding sphere. Changed in equal
   * proportions over equal times, so an approach from far away does not rush the last stretch.
   */
  range?: number;
  /** Raises the point looked at above the model's centre, in radii (negative lowers it). */
  lift?: number;
  /**
   * Keeps the eye at least this many metres above the ground, tilting down to keep looking at the
   * same point; null lifts the limit (needed to go underground). Carried on to later moves.
   */
  aboveGround?: number | null;
}

export type ScenarioAction =
  /** The camera moves from where the previous camera item left it to `to`; a zero duration jumps. */
  | { kind: 'camera'; to: OrbitPose; ease?: Ease }
  /** A line of text over the bottom of the picture. */
  | { kind: 'caption'; text: string }
  /** A card over the whole picture. */
  | { kind: 'title'; text: string; subtitle?: string }
  /** Fades the picture towards black (1) or back (0). */
  | { kind: 'fade'; to: number; ease?: Ease }
  /** Shown: the pointer opens or closes the 3D view's layer panel. */
  | { kind: 'layerPanel'; open: boolean }
  /** Shown: the pointer picks a basemap in the 3D layer panel, opening its group first. */
  | { kind: 'basemap'; name: string; group?: string }
  /** Shown: the pointer ticks (or clears) a layer whose row label matches. */
  | { kind: 'layer'; label: string; on: boolean }
  /** Shown: the pointer drags the active basemap's opacity slider through these percentages. */
  | { kind: 'basemapOpacity'; values: number[]; ease?: Ease }
  /**
   * Recolours the cave's walls from their present colour to `to` ('#rrggbb'), mixed into their own
   * colour by `strength` (0–1, default 0.8) so the shading still shows; null gives them back their
   * own colours.
   */
  | { kind: 'modelColor'; to: string | null; strength?: number; ease?: Ease };

export type ScenarioItem = ScenarioAction & { at: number; duration?: number };

/** What is arranged before the first frame, unfilmed; done by whatever plays the scenario. */
export interface ScenarioSetup {
  /** The cave whose 3D view is opened with its walls drawn, found by name. */
  cave?: string;
  /** The basemap chosen before the first frame. */
  basemap?: { name: string; group?: string };
  /** Layers (by their row label in the 3D layer panel) switched off before the first frame. */
  layersOff?: string[];
  /** Parts of the page left out of the picture, as CSS selectors. */
  hide?: string[];
}

export interface Scenario {
  name: string;
  /** Interface and caption language. */
  language?: 'en' | 'ro';
  setup?: ScenarioSetup;
  items: ScenarioItem[];
}

/** Which items steer the same thing, so must not overlap in time. */
export function channelOf(action: ScenarioAction): string | undefined {
  switch (action.kind) {
    case 'camera':
      return 'camera';
    case 'layerPanel':
    case 'basemap':
    case 'layer':
    case 'basemapOpacity':
      return 'pointer';
    case 'fade':
      return 'fade';
    case 'modelColor':
      return 'modelColor';
    case 'caption':
    case 'title':
      return undefined;
  }
}

export function durationOf(item: ScenarioItem): number {
  return Math.max(0, item.duration ?? 0);
}

/** Seconds from the start to the end of the last item. */
export function scenarioLength(scenario: Scenario): number {
  return scenario.items.reduce((end, item) => Math.max(end, item.at + durationOf(item)), 0);
}

/** Every reason the scenario cannot be played; empty when it can. */
export function scenarioProblems(scenario: Scenario): string[] {
  const problems: string[] = [];
  const byChannel = new Map<string, ScenarioItem[]>();
  scenario.items.forEach((item, index) => {
    if (!(item.at >= 0)) {
      problems.push(`item ${index} (${item.kind}) starts before the movie does`);
    }
    if (item.kind === 'basemapOpacity' && item.values.length < 2) {
      problems.push(`item ${index} drags the opacity slider through fewer than two values`);
    }
    if (item.kind === 'camera' && item.to.range !== undefined && !(item.to.range > 0)) {
      problems.push(`item ${index} puts the camera at a range that is not above zero`);
    }
    if (item.kind === 'modelColor' && item.to !== null && !/^#[0-9a-f]{6}$/i.test(item.to)) {
      problems.push(`item ${index} names the colour "${item.to}", which is not written #rrggbb`);
    }
    const channel = channelOf(item);
    if (channel) {
      byChannel.set(channel, [...(byChannel.get(channel) ?? []), item]);
    }
  });
  for (const [channel, items] of byChannel) {
    const sorted = [...items].sort((a, b) => a.at - b.at);
    for (let i = 1; i < sorted.length; i += 1) {
      const previous = sorted[i - 1];
      if (sorted[i].at < previous.at + durationOf(previous) - 1e-6) {
        problems.push(
          `${channel}: "${sorted[i].kind}" at ${sorted[i].at}s starts before "${previous.kind}" at ${previous.at}s ends`,
        );
      }
    }
  }
  if (!scenario.items.some((item) => item.kind === 'camera' && item.at === 0)) {
    problems.push('the first camera item must start at 0 s, so the opening view is defined');
  }
  return problems;
}

export function ease(kind: Ease | undefined, p: number): number {
  const x = Math.min(1, Math.max(0, p));
  switch (kind ?? 'inOut') {
    case 'smooth':
      return ease('inOut', x);
    case 'linear':
      return x;
    case 'in':
      return x * x * x;
    case 'out':
      return 1 - (1 - x) ** 3;
    case 'inOut':
      return x < 0.5 ? 4 * x * x * x : 1 - (-2 * x + 2) ** 3 / 2;
  }
}
