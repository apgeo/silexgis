// SPDX-License-Identifier: AGPL-3.0-or-later
import { createContext, runInContext } from 'node:vm';
import { describe, expect, it } from 'vitest';
import { build } from 'vite';
import type { PublicPastTrack, PublicPastTrackFix } from '../api/hooks.ts';
import { placeLabelFor } from '../caveview/publicPlaces.ts';
import { publicTrackedCavers } from '../caveview/publicTrackedCavers.ts';
import { pastPicturesAt } from '../pages/public/pastTrackPictures.ts';
import {
  pastEnvelopeAt,
  pastReportMoments,
  type PastFollow,
} from '../pages/public/pastTrackReplay.ts';

/**
 * What holds the second compilation of the fold honest.
 *
 * <b>The claim this file exists to test.</b> The published-trip fold is compiled twice from one
 * source: once into this application, and once into a standalone file a club's own article loads
 * beside a plain-JavaScript cave viewer. The whole value of that arrangement is that there is one
 * rule rather than two — so the thing worth testing is not that the file is produced, but that
 * <em>what it computes is what this application computes</em>, and that nothing rode along with it
 * that a stranger's page should not have been handed.
 *
 * The build is run here rather than depending on an artefact left in the tree by an earlier
 * command. A test that reads whatever is lying in `dist-trips/` passes against yesterday's output,
 * which is precisely the failure it would be written to catch.
 */

const TEAM_A = '11111111-1111-1111-1111-111111111111';

/** The surface the article is promised. Adding a name here is a deliberate act; so is losing one. */
const PUBLISHED_NAMES = [
  'PAST_LINK_PARAMS',
  'drawableOn',
  'envelopeCrsLookup',
  'firstPlacedMoment',
  'followedName',
  'followedStation',
  'instantOf',
  'momentAfter',
  'momentBefore',
  'partyByTeam',
  'partyStandings',
  'pastEnvelopeAt',
  'pastPicturesAt',
  'pastReplayWindow',
  'pastReportMoments',
  'placeLabelFor',
  'placeOnModel',
  'plannedReturn',
  'publicPlaceReported',
  'publicTrackedCavers',
  'readPastLink',
  'sharedTeamTitle',
  'standingOf',
  'teamStation',
  'trackedCaverTeams',
  'undergroundFirst',
  'writePastLink',
] as const;

function fix(overrides: Partial<PublicPastTrackFix> = {}): PublicPastTrackFix {
  return {
    recordedAt: '2019-07-06T09:00:00Z',
    teamId: null,
    stationName: null,
    depthM: null,
    positionOnOtherModel: false,
    in: false,
    out: false,
    ...overrides,
  };
}

/**
 * A party with every case the drawing has to tell apart in it: somebody placed, somebody whose
 * place was measured on a survey this model is not, and somebody reported out. A fixture that
 * exercised only the happy path would let the two compilations disagree about exactly the rows
 * where disagreeing matters.
 */
const TRACK: PublicPastTrack = {
  tripLogId: '0195f4a2-6c3e-7b10-9f21-ab44de77c001',
  expedition: null,
  title: 'Peștera Demo Mare, the 2019 push',
  tripDate: '2019-07-06',
  tripDateEnd: null,
  armedAt: '2019-07-06T08:00:00Z',
  closedAt: '2019-07-06T18:00:00Z',
  positionsWithheld: false,
  trackTruncated: false,
  model: null,
  pictures: [
    {
      at: '2019-07-06T10:00:00Z',
      ordinal: null,
      thumbnailUrl: '/api/v1/files/all/thumbnail?size=480&token=sig',
      caption: 'The whole party',
    },
    {
      at: '2019-07-06T10:00:00Z',
      ordinal: 1,
      thumbnailUrl: '/api/v1/files/ana/thumbnail?size=480&token=sig',
      caption: null,
    },
    {
      at: '2019-07-06T12:30:00Z',
      ordinal: 3,
      thumbnailUrl: '/api/v1/files/radu/thumbnail?size=480&token=sig',
      caption: null,
    },
  ],
  teams: [{ id: TEAM_A, title: 'Advance' }],
  participants: [
    {
      ordinal: 1,
      label: 'Ana',
      track: [
        fix({ recordedAt: '2019-07-06T09:00:00Z', in: true, teamId: TEAM_A }),
        fix({ recordedAt: '2019-07-06T10:00:00Z', stationName: 'p.g.7', in: true, teamId: TEAM_A }),
        fix({ recordedAt: '2019-07-06T12:00:00Z', stationName: 'p.g.19', in: true, teamId: TEAM_A }),
      ],
    },
    {
      ordinal: 2,
      label: null,
      track: [
        fix({ recordedAt: '2019-07-06T09:30:00Z', in: true }),
        fix({ recordedAt: '2019-07-06T11:00:00Z', positionOnOtherModel: true, in: true }),
      ],
    },
    {
      ordinal: 3,
      label: 'Radu',
      track: [
        fix({ recordedAt: '2019-07-06T09:00:00Z', stationName: 'p.g.1', in: true }),
        fix({ recordedAt: '2019-07-06T13:00:00Z', out: true }),
      ],
    },
  ],
};

const MOMENTS = [
  '2019-07-06T08:30:00Z',
  '2019-07-06T09:45:00Z',
  '2019-07-06T11:30:00Z',
  '2019-07-06T14:00:00Z',
  '2019-07-06T19:00:00Z',
].map((iso) => Date.parse(iso));

/** The words are the host's, so both sides are asked with the same ones. */
const unnamed = (ordinal: number) => `Caver ${ordinal}`;

interface LoadedBundle {
  code: string;
  api: Record<string, unknown>;
}

async function buildBundle(format: 'iife' | 'es'): Promise<LoadedBundle> {
  // `build()` is typed as returning a union covering watch mode, which this call cannot be in.
  // Narrowed by reading the shape rather than by naming a type: the bundler's own result types are
  // not re-exported by this Vite major, and importing them from its bundler directly would tie this
  // test to which bundler Vite ships.
  const result = (await build({
    configFile: false,
    logLevel: 'silent',
    define: { 'import.meta.env': '({})' },
    build: {
      write: false,
      target: 'es2020',
      lib: {
        entry: new URL('./index.ts', import.meta.url).pathname,
        name: 'SilexGisTrips',
        formats: [format],
        fileName: () => `bundle.${format}.js`,
      },
      rollupOptions: { external: [] },
    },
  })) as unknown as { output: { type: string; code: string }[] } | { output: { type: string; code: string }[] }[];

  const outputs = (Array.isArray(result) ? result : [result]).flatMap((one) => one.output);
  const chunks = outputs.filter((entry) => entry.type === 'chunk');
  expect(chunks, 'the library build emits exactly one chunk').toHaveLength(1);
  const code = chunks[0].code;

  if (format === 'es') {
    return { code, api: {} };
  }

  // Evaluated the way the page will evaluate it: as a script, in a context holding nothing. A
  // bundle that needed `window`, `document` or a module loader fails here rather than on somebody
  // else's website.
  const context: Record<string, unknown> = {};
  createContext(context);
  runInContext(code, context);
  return { code, api: context.SilexGisTrips as Record<string, unknown> };
}

describe('the build that produces it', () => {
  it('carries nothing but the fold to the page that loads it', async () => {
    // Guards a mistake already made once here: a library build inherits `publicDir`, so the
    // application's whole `public/` tree — icons, avatars, the web manifest, the cave viewer's
    // runtime files — was copied in beside the one script, ready to be carried onto somebody
    // else's web server by whoever deploys it. Read off the config rather than off a directory
    // listing, because the copy happens at write time and the bundle tests below do not write.
    const loaded = (await import('../../vite.trips.config.ts')) as {
      default: { publicDir?: unknown; build?: { rollupOptions?: { external?: unknown } } };
    };
    expect(loaded.default.publicDir, 'publicDir must stay off').toBe(false);
    expect(loaded.default.build?.rollupOptions?.external, 'nothing may be left unbundled').toEqual(
      [],
    );
  });
});

describe('the fold, compiled for a page that has no build step', () => {
  it('publishes exactly the promised names on one global, and every one of them is callable', async () => {
    const { api } = await buildBundle('iife');

    expect(api, 'the IIFE assigns its global').toBeTypeOf('object');
    expect(Object.keys(api).sort()).toEqual([...PUBLISHED_NAMES].sort());

    for (const name of PUBLISHED_NAMES) {
      if (name === 'PAST_LINK_PARAMS') {
        // `play` joined on purpose: the page answers to it in its address, so leaving a replay
        // has to clear it with the rest. An addition only — the four names before it are the
        // ones every address already written uses, in the order they were published.
        expect(api[name], name).toEqual(['past', 'team', 'caver', 'at', 'play']);
      } else {
        expect(api[name], name).toBeTypeOf('function');
      }
    }
  });

  it('answers what this application answers, at every kind of moment', async () => {
    const { api } = await buildBundle('iife');
    const foldedThere = api.pastEnvelopeAt as typeof pastEnvelopeAt;
    const partyThere = api.publicTrackedCavers as typeof publicTrackedCavers;

    for (const moment of MOMENTS) {
      // The envelope first: this is the shape the live page and the replay share, so if the two
      // compilations agree here they agree about the thing every drawing is built on.
      expect(foldedThere(TRACK, moment), `envelope at ${new Date(moment).toISOString()}`).toEqual(
        pastEnvelopeAt(TRACK, moment),
      );

      // Then the party as a viewer receives it — station, standing and team per person.
      expect(
        partyThere(foldedThere(TRACK, moment), unnamed),
        `party at ${new Date(moment).toISOString()}`,
      ).toEqual(publicTrackedCavers(pastEnvelopeAt(TRACK, moment), unnamed));
    }
  });

  it('names a station as this application does, and names nothing from an empty list', async () => {
    const { api } = await buildBundle('iife');
    const labelThere = api.placeLabelFor as typeof placeLabelFor;
    const places = [
      { station: 'cave.upper.2', depthM: 50, label: 'Sala Mare' },
      { station: 'cave.deep.3', depthM: 120, label: 'Sifonul' },
    ];

    for (const station of ['cave.upper.2', 'cave.deep.3', 'cave.mid.1', '', null]) {
      expect(labelThere(station, places), String(station)).toBe(placeLabelFor(station, places));
      // What every installation that publishes no names sends, and what an older answer lacks.
      expect(labelThere(station, []), String(station)).toBeNull();
      expect(labelThere(station, undefined), String(station)).toBeNull();
    }
    // Pinned to the words as well: two compilations that both answered null would agree.
    expect(labelThere('cave.deep.3', places)).toBe('Sifonul');
  });

  it('shows the photographs of a moment as this application does, and none from an empty list', async () => {
    const { api } = await buildBundle('iife');
    const picturesThere = api.pastPicturesAt as typeof pastPicturesAt;

    for (const moment of MOMENTS) {
      expect(picturesThere(TRACK, moment), new Date(moment).toISOString()).toEqual(
        pastPicturesAt(TRACK, moment),
      );
      // What every installation that publishes none sends, and what an older answer lacks.
      expect(picturesThere({ ...TRACK, pictures: [] }, moment)).toEqual([]);
      expect(picturesThere({}, moment)).toEqual([]);
    }
    // Pinned to the pictures themselves as well: two compilations that both answered nothing
    // would agree. Before the first photograph, on its moment's pair, and after the second.
    const shown = (clock: string) =>
      picturesThere(TRACK, Date.parse(`2019-07-06T${clock}:00Z`)).map((picture) => picture.ordinal);
    expect(shown('09:59')).toEqual([]);
    expect(shown('11:30')).toEqual([null, 1]);
    expect(shown('14:00')).toEqual([3]);
  });

  it('lists the reports of a followed team or caver as this application does', async () => {
    // The second argument is newer than pages already loading this file: left out it must answer
    // what it always answered, and given it must narrow by the same rule the application's own
    // arrows step by — the last team a person's reports named, and no team before any did.
    const { api } = await buildBundle('iife');
    const momentsThere = api.pastReportMoments as typeof pastReportMoments;
    const hour = (clock: string) => Date.parse(`2019-07-06T${clock}:00Z`);

    const everybody = ['09:00', '09:30', '10:00', '11:00', '12:00', '13:00'].map(hour);
    expect(momentsThere(TRACK)).toEqual(everybody);
    expect(momentsThere(TRACK, null)).toEqual(everybody);

    const follows: { follow: PastFollow; theirs: string[] }[] = [
      { follow: { kind: 'team', id: TEAM_A }, theirs: ['09:00', '10:00', '12:00'] },
      { follow: { kind: 'team', id: null }, theirs: ['09:00', '09:30', '11:00', '13:00'] },
      { follow: { kind: 'caver', id: '2' }, theirs: ['09:30', '11:00'] },
      { follow: { kind: 'caver', id: '99' }, theirs: [] },
    ];
    for (const { follow, theirs } of follows) {
      const said = `${follow.kind} ${String(follow.id)}`;
      // Pinned to the instants themselves as well as to this application's answer: two
      // compilations that both ignored the argument would agree with each other.
      expect(momentsThere(TRACK, follow), said).toEqual(theirs.map(hour));
      expect(momentsThere(TRACK, follow), said).toEqual(pastReportMoments(TRACK, follow));
    }
  });

  it('carries a party this fixture is only useful because it distinguishes', async () => {
    // Guards the test above: were every participant folded to the same answer, agreement would
    // prove nothing. Asserted on this application's own fold, which the bundle is then held to.
    const party = publicTrackedCavers(pastEnvelopeAt(TRACK, MOMENTS[2]), unnamed);
    expect(party.map((member) => member.position.kind)).toEqual([
      'station',
      'otherModel',
      'station',
    ]);
    expect(party.map((member) => member.out)).toEqual([false, false, false]);
    expect(party[1].name, 'an unlabelled participant is named by the host, not by the fold').toBe(
      'Caver 2',
    );
  });

  it('hands a stranger no framework, no module specifier and no opinion about language', async () => {
    for (const format of ['iife', 'es'] as const) {
      const { code } = await buildBundle(format);

      // React would mean the whole application had followed the fold onto somebody's article.
      expect(/\bcreateElement\b|\buseState\b|react\/jsx/.test(code), `${format}: no React`).toBe(
        false,
      );
      // A bare specifier cannot be resolved by a page with no import map and no bundler.
      expect(/\bfrom\s*["'][a-zA-Z@]/.test(code), `${format}: no bare imports`).toBe(false);
      expect(/\brequire\(/.test(code), `${format}: nothing asks for CommonJS`).toBe(false);
      // The movie's encoders and the viewer's capture session belong to the signed-in dialog alone.
      expect(/\bbeginCapture\b|\bVideoEncoder\b|gifWorker/.test(code), `${format}: no movie code`).toBe(false);
      // The two hosts do not share a language or a catalogue: words stay with whoever is speaking.
      expect(/Intl\.|toLocale/.test(code), `${format}: no locale formatting`).toBe(false);
      // `import.meta.env` would be this application's build talking to a page that never had one.
      expect(/import\.meta\.env/.test(code), `${format}: no build-time environment`).toBe(false);
    }
  });
});
