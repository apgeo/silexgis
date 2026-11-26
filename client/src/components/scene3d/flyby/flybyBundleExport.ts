// SPDX-License-Identifier: AGPL-3.0-or-later
import { downloadFilePostWithHeaders } from '../../../api/download.ts';
import { fetchEntranceFeatures, fetchMapFeatures } from '../../../api/hooks.ts';
import { encodeFilter } from '../../../filters/url.ts';
import {
  FLYBY_BUNDLE_MAX_FRAMES,
  FLYBY_BUNDLE_MAX_MARKERS,
  flybyBundleOmissions,
  flybyBundleRequest,
  flybyBundleShows,
  type FlybyBundleMarkerAsk,
  type FlybyBundleMarkerLayerAsk,
  type FlybyBundleOmission,
  type FlybyBundleRefusal,
  type FlybyBundleRevealBy,
} from '../../../flyby/flybyBlenderBundle.ts';
import { FLYBY_NAMED_LAYERS, flybyFeatureLayerKey, type FlybyDocument, type FlybyLayerKey } from '../../../flyby/flybyDocument.ts';
import { featuresOfTypesDocument } from '../../../flyby/flybyFilters.ts';
import { flybyLayerPartsAt, flybyLookPlan, type FlybyLookBase, type FlybyLookPlan } from '../../../flyby/flybyLook.ts';
import type { FlybyMarkerSource } from '../../../flyby/flybyLookApply.ts';
import { filterLayerPalette } from '../../../map/markerPalette.ts';
import { featuresOf, pointPositions, propertiesOf } from '../../../scene3d/geoJson3d.ts';
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
  /** Entrances and features that stand in the scene as markers. */
  markers: number;
  /** Entrances and features asked for and not drawn; again no reason is given. */
  markersNotDrawn: number;
  /** Photographs the scene shows. */
  photos: number;
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
  /** The language the reader has the application in: the bundle's own notes are written in it where they exist in it. */
  language?: string;
  /** How the caves appear part by part; left out, they are there from the first frame. */
  reveal?: FlybyBundleRevealBy | null;
  signal?: AbortSignal;
}

/**
 * The zoom the entrances are asked at: well past any at which the server answers with clusters, so
 * every entrance comes back as itself.
 */
const MARKER_ZOOM = 18;

/** How far past the caves' own bounds markers are looked for, as a share of the bounds' larger side, and at least. */
const MARKER_MARGIN_SHARE = 0.5;
const MARKER_MARGIN_MIN_M = 250;

/**
 * The window markers are looked for in: the bounds of the caves the view is drawing, widened by
 * half their larger side and by a quarter of a kilometre at least. Undefined where the view draws
 * no cave: there is then nothing for a marker to stand beside.
 */
export function flybyBundleMarkerWindow(bounds: readonly (readonly [number, number, number, number])[]): string | undefined {
  if (bounds.length === 0) {
    return undefined;
  }
  const west = Math.min(...bounds.map((box) => box[0]));
  const south = Math.min(...bounds.map((box) => box[1]));
  const east = Math.max(...bounds.map((box) => box[2]));
  const north = Math.max(...bounds.map((box) => box[3]));
  const metresALatitude = 111_320;
  const metresALongitude = metresALatitude * Math.max(0.01, Math.cos((((south + north) / 2) * Math.PI) / 180));
  const margin = Math.max(
    MARKER_MARGIN_MIN_M,
    MARKER_MARGIN_SHARE * Math.max((east - west) * metresALongitude, (north - south) * metresALatitude),
  );
  const dLon = margin / metresALongitude;
  const dLat = margin / metresALatitude;
  return [west - dLon, Math.max(-90, south - dLat), east + dLon, Math.min(90, north + dLat)].map((value) => value.toFixed(7)).join(',');
}

/**
 * What a layer's answer holds that a bundle can stand a marker on: the entrances and features that
 * are single points the reader is shown exactly. A cluster is several things, a place shown
 * approximately is not where the thing is, and a line or an area is drawn on the ground.
 */
export function flybyBundleMarkersOf(
  collection: unknown,
  groundHeight: (longitude: number, latitude: number) => number,
  named: (properties: Record<string, unknown>) => boolean = () => false,
): FlybyBundleMarkerAsk[] {
  const markers: FlybyBundleMarkerAsk[] = [];
  for (const feature of featuresOf(collection)) {
    const properties = propertiesOf(feature);
    const id = properties.id;
    const [position] = pointPositions(feature);
    if (typeof id !== 'string' || properties.cluster === true || properties.approximate === true || position === undefined) {
      continue;
    }
    if ((feature as { geometry?: { type?: unknown } }).geometry?.type !== 'Point') {
      continue;
    }
    markers.push({
      featureId: id,
      groundM: groundHeight(position.longitude, position.latitude),
      ...(named(properties) ? { named: true } : {}),
    });
  }
  return markers;
}

/** Whether the flyby shows a layer at any moment of a clip. */
function shownEver(plan: FlybyLookPlan, base: FlybyLookBase, schedule: Pick<FlybyFrameSchedule, 'from' | 'to'>, key: FlybyLayerKey): boolean {
  const moments = [
    schedule.from,
    ...plan.changes
      .filter((timed) => timed.change.kind === 'layer' && timed.change.layer === key && timed.start > schedule.from && timed.start < schedule.to)
      .map((timed) => timed.start),
  ];
  return moments.some((t) => flybyLayerPartsAt(plan, base, key, t).visible);
}

/**
 * The entrances and features the view's layers hold about the caves it draws, read from the routes
 * those layers are drawn from — so what comes back is what this reader is shown, under the filter
 * each layer is under. Only layers the flyby shows in the clip are asked for. A layer that cannot
 * be read is left out, as the view leaves it out.
 */
async function markersInView(
  asked: FlybyBundleAsked,
  base: FlybyLookBase,
  bounds: readonly (readonly [number, number, number, number])[],
): Promise<FlybyBundleMarkerLayerAsk[]> {
  const bbox = flybyBundleMarkerWindow(bounds);
  if (bbox === undefined) {
    return [];
  }
  const { timeline, schedule, scene } = asked;
  const sources: FlybyMarkerSource[] = [
    ...(scene.look?.markerSources?.() ?? []),
    // The flyby's own layers of features, which are its to draw from start to end.
    ...timeline.look.features.map(
      (layer): FlybyMarkerSource => ({
        layer: flybyFeatureLayerKey(layer.id),
        kind: 'features',
        params:
          layer.source.kind === 'filter'
            ? { savedFilterId: layer.source.savedFilterId }
            : { filter: encodeFilter(featuresOfTypesDocument(layer.source.typeIds)) },
        colour: filterLayerPalette[layer.colour],
      }),
    ),
  ];
  const own = new Set(asked.routeCaves);
  const ofOwnCave = (properties: Record<string, unknown>) => typeof properties.caveId === 'string' && own.has(properties.caveId);
  const layers: FlybyBundleMarkerLayerAsk[] = [];
  for (const source of sources) {
    if (!shownEver(timeline.look, base, schedule, source.layer)) {
      continue;
    }
    // The entrances' own layer holds entrances; the features' own holds every other kind; a layer
    // drawn from a filter holds whichever the filter finds of both.
    const [entrances, features] = await Promise.allSettled([
      source.kind === 'surfaceFeatures' ? Promise.resolve(null) : fetchEntranceFeatures(bbox, MARKER_ZOOM, source.params),
      source.kind === 'entrances' ? Promise.resolve(null) : fetchMapFeatures(bbox, source.params),
    ]);
    asked.signal?.throwIfAborted();
    const markers = [
      ...(entrances.status === 'fulfilled' ? flybyBundleMarkersOf(entrances.value, asked.groundHeight, ofOwnCave) : []),
      ...(features.status === 'fulfilled' ? flybyBundleMarkersOf(features.value, asked.groundHeight) : []),
    ];
    // A thing both routes answer with is one marker. The entrances of the flyby's own caves come
    // first: a bundle takes only so many markers.
    const once = [...new Map(markers.map((marker) => [marker.featureId, marker])).values()];
    once.sort((a, b) => Number(b.named === true) - Number(a.named === true));
    if (once.length > 0) {
      layers.push({ layer: source.layer, kind: source.kind, colour: source.colour, markers: once.slice(0, FLYBY_BUNDLE_MAX_MARKERS + 1) });
    }
  }
  return layers;
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
  const markers = await markersInView(
    asked,
    base,
    inView.map((cave) => cave.bounds),
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
    language: asked.language,
    markers,
    reveal: asked.reveal ?? null,
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
    markers: count(headers, 'X-SilexGis-Bundle-Markers'),
    markersNotDrawn: count(headers, 'X-SilexGis-Bundle-Markers-Not-Drawn'),
    photos: count(headers, 'X-SilexGis-Bundle-Photos'),
  };
}
