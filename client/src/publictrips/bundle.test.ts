// SPDX-License-Identifier: AGPL-3.0-or-later
import { createContext, runInContext } from 'node:vm';
import { describe, expect, it } from 'vitest';
import { build } from 'vite';
import type { PublicPastTrack, PublicPastTrackFix } from '../api/hooks.ts';
import { publicTrackedCavers } from '../caveview/publicTrackedCavers.ts';
import { pastEnvelopeAt } from '../pages/public/pastTrackReplay.ts';

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
  'pastReplayWindow',
  'pastReportMoments',
  'placeOnModel',
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
  title: 'Peștera Demo Mare, the 2019 push',
  tripDate: '2019-07-06',
  tripDateEnd: null,
  armedAt: '2019-07-06T08:00:00Z',
  closedAt: '2019-07-06T18:00:00Z',
  positionsWithheld: false,
  trackTruncated: false,
  model: null,
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
        expect(api[name], name).toEqual(['past', 'team', 'caver', 'at']);
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
      // The two hosts do not share a language or a catalogue: words stay with whoever is speaking.
      expect(/Intl\.|toLocale/.test(code), `${format}: no locale formatting`).toBe(false);
      // `import.meta.env` would be this application's build talking to a page that never had one.
      expect(/import\.meta\.env/.test(code), `${format}: no build-time environment`).toBe(false);
    }
  });
});
