// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { noStationsMissing, stationsNotOnModel } from './placedOnModel.ts';

/** A marker as the viewer describes one back, trimmed to the two fields this rule reads. */
const marker = (ref: string, resolved: boolean) => ({ ref, resolved });

describe('stationsNotOnModel', () => {
  it('names nothing when the viewer resolved every marker it is holding', () => {
    const answer = stationsNotOnModel(noStationsMissing, [
      marker('p.g.7', true),
      marker('p.s.4', true),
    ]);

    expect([...answer]).toEqual([]);
  });

  it('names the station of a marker the viewer is holding unresolved', () => {
    // The whole failure this exists for: the marker was accepted, it is held, and the loaded
    // model has no station of that name — so it is drawn nowhere and nothing says so.
    const answer = stationsNotOnModel(noStationsMissing, [
      marker('p.g.7', false),
      marker('p.s.4', true),
    ]);

    expect([...answer]).toEqual(['p.g.7']);
  });

  it('keeps what it was told before, because a marker taken off takes its evidence with it', () => {
    // The replay case, in one line. The panel's whole party is replaced by the party of an earlier
    // moment, so the viewer stops holding the marker that revealed the station — while the table
    // reading this answer goes on printing that same station as a place somebody is.
    const before = stationsNotOnModel(noStationsMissing, [marker('p.g.7', false)]);

    const after = stationsNotOnModel(before, [marker('p.s.4', true)]);

    expect([...after]).toEqual(['p.g.7']);
  });

  it('answers the very set it was given when it learned nothing', () => {
    // Asked twice a minute while a party is underground, and every fresh identity re-renders the
    // list beside the model, the table above it and a published page.
    const before = stationsNotOnModel(noStationsMissing, [marker('p.g.7', false)]);

    expect(stationsNotOnModel(before, [marker('p.g.7', false)])).toBe(before);
    expect(stationsNotOnModel(before, [marker('p.s.4', true)])).toBe(before);
    expect(stationsNotOnModel(noStationsMissing, [])).toBe(noStationsMissing);
  });

  it('learns nothing from a marker the viewer is not holding', () => {
    // A marker the panel asked for and the viewer does not have is the two of them disagreeing
    // about what was asked for, which is a different failure and a recoverable one. Recorded here
    // it would condemn a station this drawing holds perfectly well, for as long as it is loaded.
    expect([...stationsNotOnModel(noStationsMissing, [])]).toEqual([]);
  });

  it('records only a reference the viewer can be asked about again', () => {
    // A reference given as split components would have to be joined to be compared against a
    // station name, and a dotted path is ambiguous when a name inside it contains a dot — so
    // joining one would invent a station name rather than report one.
    const answer = stationsNotOnModel(noStationsMissing, [
      { ref: ['p', 'g.7'], resolved: false },
      marker('p.s.4', false),
    ]);

    expect([...answer]).toEqual(['p.s.4']);
  });
});
