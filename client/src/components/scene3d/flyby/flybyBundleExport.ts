// SPDX-License-Identifier: AGPL-3.0-or-later
import { downloadFilePostWithHeaders } from '../../../api/download.ts';
import {
  FLYBY_BUNDLE_MAX_FRAMES,
  flybyBundleOmissions,
  flybyBundleRequest,
  flybyBundleShows,
  type FlybyBundleOmission,
  type FlybyBundleRefusal,
} from '../../../flyby/flybyBlenderBundle.ts';
import { FLYBY_NAMED_LAYERS, type FlybyDocument, type FlybyLayerKey } from '../../../flyby/flybyDocument.ts';
import { flybyLookPlan, type FlybyLookBase } from '../../../flyby/flybyLook.ts';
import type { FlybyFrameSchedule } from '../../../flyby/flybyMovie.ts';
import type { FlybyTimeline } from '../../../flyby/flybyTimeline.ts';
import { flybyRouteCaves } from '../../../flyby/flybyWalls.ts';
import { ANCHORED_TO_SURFACE } from '../../../scene3d/altitude3d.ts';
import { CENTERLINE_DEPTH_BANDS } from '../../../scene3d/centerlines3d.ts';
import type { FlybyScene } from './useFlyby.ts';

// A flyby asked of the server as a render bundle: the view's half of it.
//
// What a bundle is made from is worked out without a view (`flybyBlenderBundle.ts`); here the view
// is asked the few things only it knows — what the viewer has switched on, where it draws the
// caves, which walls it is still waiting for a yes about — and the request is sent.

/** The route a bundle is asked from. */
export const FLYBY_BUNDLE_ROUTE = '/api/v1/flybys/blender-bundle';

/** What the server says of a bundle it made, beside the file. */
export interface FlybyBundleMade {
  /** Caves drawn with their walls. */
  walls: number;
  /** Caves drawn by their survey lines alone. */
  lines: number;
  /** Caves asked for and not drawn at all; the server gives no reason, and there is none to show. */
  notDrawn: number;
  /** Caves whose walls were left out, drawn by their lines instead. */
  wallsLeftOut: number;
}

/** What a view with no look to ask has: everything as a bare scene draws it. */
const BARE_LOOK: FlybyLookBase = { backgroundOpacity: 1, surface: 'overlay', layers: {} };

/** The listed layers the viewer has switched on, which a flyby leaves as it finds them. */
function layersShown(scene: FlybyScene): FlybyLayerKey[] {
  return scene.look?.viewerLayers?.() ?? [];
}

/** What the viewer has, for every layer a bundle has something to say about. */
function viewerLook(scene: FlybyScene, named: readonly FlybyLayerKey[], shown: readonly FlybyLayerKey[]): FlybyLookBase {
  return scene.look?.viewerLook([...new Set([...FLYBY_NAMED_LAYERS, ...named, ...shown])]) ?? BARE_LOOK;
}

/** What is known of a bundle before the flyby has been prepared: enough to say what it leaves out. */
export interface FlybyBundlePreview {
  omissions: FlybyBundleOmission[];
  refusal: FlybyBundleRefusal;
}

/**
 * What a bundle of this flyby would leave out and whether one can be made, from the document and
 * the view as they stand — before anything is prepared, so the panel can say it while the reader
 * is still choosing.
 */
export function flybyBundlePreview(
  document: FlybyDocument,
  scene: FlybyScene,
  schedule: Pick<FlybyFrameSchedule, 'from' | 'to' | 'count'>,
  wallsDeclined: boolean,
): FlybyBundlePreview {
  const plan = flybyLookPlan(document);
  const shown = layersShown(scene);
  const base = viewerLook(scene, plan.layers, shown);
  const shows = flybyBundleShows(plan, base, schedule);
  const omissions = flybyBundleOmissions(plan, base, schedule, shown);
  if (wallsDeclined && shows.walls) {
    omissions.push('wallsNotDownloaded');
  }
  // A saved filter stands for caves that are only found when the flyby is prepared.
  const aboutCaves = document.subjects.some((subject) => subject.kind === 'cave' || subject.kind === 'filter');
  const refusal: FlybyBundleRefusal =
    schedule.count > FLYBY_BUNDLE_MAX_FRAMES
      ? { kind: 'tooLong', frames: schedule.count, max: FLYBY_BUNDLE_MAX_FRAMES }
      : !aboutCaves
        ? { kind: 'noCaves' }
        : !shows.walls && !shows.lines
          ? { kind: 'nothingShown' }
          : null;
  return { omissions, refusal };
}

/** The caves of a flyby's route as far as they are known: the document's own, and those found when it was prepared. */
export function flybyBundleRouteCaves(document: FlybyDocument | null, prepared: readonly string[]): string[] {
  return [...new Set([...(document ? flybyRouteCaves(document) : []), ...prepared])];
}

/** A bundle that cannot be asked for as the flyby stands. */
export class FlybyBundleRefused extends Error {
  readonly refusal: NonNullable<FlybyBundleRefusal>;

  constructor(refusal: NonNullable<FlybyBundleRefusal>) {
    super(`flyby bundle: ${refusal.kind}`);
    this.name = 'FlybyBundleRefused';
    this.refusal = refusal;
  }
}

export interface FlybyBundleAsked {
  name: string;
  /** The flyby, prepared: its route has been loaded, so the ground under the camera is the ground a movie would find. */
  timeline: FlybyTimeline;
  schedule: FlybyFrameSchedule;
  size: { width: number; height: number };
  quality: 'low' | 'medium' | 'high';
  scene: FlybyScene;
  groundHeight(longitude: number, latitude: number): number;
  /** The flyby's own caves, as prepared. */
  routeCaves: readonly string[];
  /** Whether walls the view is waiting for a yes about go into the bundle all the same. */
  includeWalls: boolean;
  reliefWords(factor: number): string;
  signal?: AbortSignal;
}

function count(headers: Headers, name: string): number {
  const value = Number(headers.get(name));
  return Number.isFinite(value) ? value : 0;
}

/**
 * Asks the server for the bundle and saves it; answers what the server said went into it.
 *
 * Throws {@link FlybyBundleRefused} when the flyby as prepared cannot be made a bundle of, and
 * whatever the download throws when the server refuses or cannot be reached.
 */
export async function downloadFlybyBundle(asked: FlybyBundleAsked): Promise<FlybyBundleMade> {
  const { scene, timeline, routeCaves } = asked;
  const shown = layersShown(scene);
  const base = viewerLook(scene, timeline.look.layers, shown);
  const inView = scene.cavesInView();
  const middleOf = new Map(
    inView.map((cave) => [cave.caveId, { longitude: (cave.bounds[0] + cave.bounds[2]) / 2, latitude: (cave.bounds[1] + cave.bounds[3]) / 2 }]),
  );
  const { request, refusal } = flybyBundleRequest({
    name: asked.name,
    timeline,
    schedule: asked.schedule,
    size: asked.size,
    quality: asked.quality,
    groundHeight: asked.groundHeight,
    base,
    shown,
    flybyRelief: scene.look?.showRelief !== undefined,
    placement: scene.altitudePlacement?.() ?? ANCHORED_TO_SURFACE,
    routeCaves,
    cavesInView: inView.map((cave) => cave.caveId),
    wallsDeclined: asked.includeWalls ? [] : (scene.walls?.pending(routeCaves) ?? []).map((walls) => walls.caveId),
    surveyTop: (caveId) => scene.caveSurveyTop?.(caveId),
    groundAt: (caveId) => {
      const middle = middleOf.get(caveId);
      return middle ? asked.groundHeight(middle.longitude, middle.latitude) : undefined;
    },
    depthBands: CENTERLINE_DEPTH_BANDS,
    reliefWords: asked.reliefWords,
  });
  if (refusal !== null) {
    throw new FlybyBundleRefused(refusal);
  }
  const headers = await downloadFilePostWithHeaders(FLYBY_BUNDLE_ROUTE, request, asked.signal);
  return {
    walls: count(headers, 'X-SilexGis-Bundle-Walls'),
    lines: count(headers, 'X-SilexGis-Bundle-Lines'),
    notDrawn: count(headers, 'X-SilexGis-Bundle-Not-Drawn'),
    wallsLeftOut: count(headers, 'X-SilexGis-Bundle-Walls-Left-Out'),
  };
}
