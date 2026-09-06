// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { TripLogFeatureCollection } from '../api/hooks.ts';
import { summariseTripFeatures, tripStyleFor } from './tripLayer.ts';
import { tripPalette } from './markerPalette.ts';

/** A stand-in for an OpenLayers feature: the style function only ever asks for a property. */
const feature = (properties: Record<string, unknown>) => ({
  get: (key: string) => properties[key],
});

/** The colour a style paints its symbol in, whichever symbol class it happens to be. */
const symbolColour = (style: ReturnType<typeof tripStyleFor>) =>
  // eslint-disable-next-line @typescript-eslint/no-explicit-any -- OL's image styles share no fill accessor type
  ((style.getImage() as any)?.getFill?.()?.getColor?.() ?? undefined) as string | undefined;

const strokeColour = (style: ReturnType<typeof tripStyleFor>) =>
  // eslint-disable-next-line @typescript-eslint/no-explicit-any -- as above
  ((style.getImage() as any)?.getStroke?.()?.getColor?.() ?? undefined) as string | undefined;

describe('the three things a trip dot can mean are drawn as three different things', () => {
  // The whole point of the discriminator: a sketch is where the party worked, a meeting point is
  // routinely a car park in a village, and a derived dot is a cave's own entrance standing in for
  // a trip that recorded no geometry. Drawing any two alike would put a car park where the reader
  // read a cave, so this asserts they are distinguishable rather than merely that each is drawn.
  it('gives the sketch, the meeting point and the derived position distinct symbols', () => {
    const sketch = tripStyleFor(feature({ kind: 'sketch' }));
    const meeting = tripStyleFor(feature({ kind: 'meeting' }));
    const derived = tripStyleFor(feature({ kind: 'cave' }));

    expect(sketch).not.toBe(meeting);
    expect(sketch).not.toBe(derived);
    expect(meeting).not.toBe(derived);

    // The sketch is a filled disc in the trip colour; the meeting point is a ring in its own
    // colour with a pale centre, so the two cannot be told apart by colour alone either.
    expect(symbolColour(sketch)).toBe(tripPalette.sketch);
    expect(strokeColour(meeting)).toBe(tripPalette.meeting);
    expect(symbolColour(meeting)).not.toBe(tripPalette.sketch);
    expect(symbolColour(derived)).toBe(tripPalette.derived);
  });

  it('reads the kind from the answer and never guesses it from the geometry', () => {
    // Both a meeting point and a sketch are routinely a single point, so geometry cannot decide
    // this. A feature carrying no word for itself falls to the sketch style — a dot in the wrong
    // shade is a defect somebody can see, and a silently undrawn trip is not.
    expect(tripStyleFor(feature({}))).toBe(tripStyleFor(feature({ kind: 'sketch' })));
    expect(tripStyleFor(feature({ kind: 'unheard-of' }))).toBe(tripStyleFor(feature({ kind: 'sketch' })));
  });
});

const collection = (
  features: { id: string; kind: string }[],
  extra: Partial<TripLogFeatureCollection> = {},
): TripLogFeatureCollection =>
  ({
    type: 'FeatureCollection',
    features: features.map((f) => ({
      type: 'Feature',
      geometry: { type: 'Point', coordinates: [25, 45] },
      properties: { id: f.id, kind: f.kind, title: 'a trip', tripDate: '2019-05-04' },
    })),
    truncated: false,
    unlocatedCount: 0,
    ...extra,
  }) as unknown as TripLogFeatureCollection;

describe('what an answer amounts to is counted in trips, never in shapes', () => {
  it('counts one trip once however many shapes it answered with', () => {
    // One trip can answer with its sketch, its meeting point and a cave it names. A reader
    // comparing this figure against the trip list is counting records, so three features from one
    // trip must read as one trip — otherwise the map and the list disagree about the same day.
    const state = summariseTripFeatures(collection([
      { id: 'trip-a', kind: 'sketch' },
      { id: 'trip-a', kind: 'meeting' },
      { id: 'trip-b', kind: 'cave' },
    ]));
    expect(state.shownTripCount).toBe(2);
    expect(state.status).toBe('ok');
  });

  it('carries the server truncation verdict rather than inferring one from the feature count', () => {
    // The cap is applied to trips and one trip answers with up to three shapes, so a feature
    // count compared against the cap is the wrong arithmetic in both directions. The server
    // counted the rows it capped; this only repeats what it said.
    expect(summariseTripFeatures(collection([{ id: 'a', kind: 'sketch' }], { truncated: true })).truncated).toBe(true);
    expect(summariseTripFeatures(collection([{ id: 'a', kind: 'sketch' }])).truncated).toBe(false);
  });

  it('reports the trips that have no position instead of losing them', () => {
    // The reason the layer exists for an imported archive: a trip whose only record of where it
    // went is a cave nobody may place has no dot, and a map silently short of it reads as though
    // the trip never happened.
    const state = summariseTripFeatures(collection([], { unlocatedCount: 7 }));
    expect(state.shownTripCount).toBe(0);
    expect(state.unlocatedCount).toBe(7);
    expect(state.status).toBe('ok');
  });
});
