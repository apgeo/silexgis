// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import { densityBboxParam, fetchSteppedDensity, type LonLatBox } from '../../map/densityCells.ts';
import type { components } from '../schema';
import { unwrap } from './shared.ts';
import { queryKeys } from './queryKeys.ts';

// ---------------------------------------------------------------------------
// Cave survey statistics
// ---------------------------------------------------------------------------

/** What a cave's line work measures, and how it compares with what the record claims. */
export type CaveSurveyStatistics = components['schemas']['CaveStatisticsDto'];

/** Which way and how steeply a cave's passages run. */
export type CaveOrientation = components['schemas']['CaveOrientationDto'];

/** One sector of a rose, or one band of a dip histogram. The range comes from the response. */
export type OrientationBin = components['schemas']['OrientationBin'];

/** How steep the passages are, or absent when the line work carries no altitudes. */
export type DipSummary = components['schemas']['DipSummary'];

/** A computed figure set against the one typed into the record. */
export type MorphometryComparison = components['schemas']['MorphometryComparison'];

/**
 * Which body of line work a survey statistic was measured from.
 *
 * This is not decoration. `surveyFlags` means the surveyor's own per-leg flags decided what
 * counts, which is what these statistics are defined as; `skeletonHeuristic` means the shape of a
 * stored centerline was used to guess the same thing, which keeps most of the length but is a
 * different measurement. Comparing one cave measured the first way against another measured the
 * second, as though they were the same figure, is the mistake this field exists to prevent —
 * so whatever renders these numbers has to say which one it got.
 */
export type SurveySegmentBasis = components['schemas']['SurveySegmentBasis'];

export function useCaveSurveyStatistics(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.caveSurveyStatistics(caveId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/caves/{id}/statistics', { params: { path: { id: caveId! } } })),
    enabled: !!caveId,
    // Recomputed per request from line work that changes only when a survey is uploaded.
    staleTime: 5 * 60_000,
    // A cave the caller may not read — or may read but not place exactly — is refused with the
    // same answer as a cave that does not exist, and asking again will not change it.
    retry: false,
  });
}

/** How big a cave's passages are, from the wall distances recorded at its stations. */
export type CaveCrossSection = components['schemas']['CaveCrossSectionDto'];

/** The sizes, distributions, volume and vertical slices of one cave's passages. */
export type CrossSectionSummary = components['schemas']['CrossSectionSummary'];

/** A five-number summary plus the mean, for one kind of measurement. */
export type CrossSectionDistribution = components['schemas']['CrossSectionDistribution'];

/** One vertical slice of a cave, with the passage sizes found in it. */
export type CrossSectionElevationBand = components['schemas']['CrossSectionElevationBand'];

/** What kind of cave a survey's shape suggests, with every rule applied to reach it. */
export type CavePattern = components['schemas']['CavePatternDto'];

/** The suggestion itself: the pattern, the scores, the rules and the caveats. */
export type PatternSuggestion = components['schemas']['PatternSuggestion'];

/** One rule, what it did, what it read and what it would have counted towards. */
export type PatternRuleTrace = components['schemas']['PatternRuleTrace'];

/** Which pattern a cave's measurements suggest. */
export type SpeleogeneticPatternKind = components['schemas']['SpeleogeneticPatternKind'];

/** Which rule a trace entry describes. */
export type PatternRule = components['schemas']['PatternRule'];

/** Which measured figure a rule read. */
export type PatternFigure = components['schemas']['PatternFigure'];

/** Whether a rule fired, stayed silent, or had nothing to read. */
export type PatternRuleOutcome = components['schemas']['PatternRuleOutcome'];

/** Something a reader has to know before using a pattern suggestion. */
export type PatternCaveat = components['schemas']['PatternCaveat'];

export function useCaveCrossSection(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.caveCrossSection(caveId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/caves/{id}/cross-section', { params: { path: { id: caveId! } } })),
    enabled: !!caveId,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

export function useCavePattern(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.cavePattern(caveId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/caves/{id}/pattern', { params: { path: { id: caveId! } } })),
    enabled: !!caveId,
    staleTime: 5 * 60_000,
  });
}

/**
 * The shape of a cave's passage network, as the figures the karst literature uses.
 *
 * Every figure is a number that was measured or it is null, and null is never a zero: a network of
 * two junctions has no connectivity ratio and a single branch has no spread of lengths, so
 * anything drawing these has to keep the two apart.
 */
export type CaveTopology = components['schemas']['CaveTopologyDto'];

export function useCaveTopology(caveId: string | undefined, hasMeasurableSurvey = true) {
  return useQuery({
    queryKey: queryKeys.caveTopology(caveId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/caves/{id}/topology', { params: { path: { id: caveId! } } })),
    // Asked only of a cave that has a read line plot behind it. The route answers "no such cave"
    // both to a caller who may not place the cave and to a cave whose network was never measured,
    // and the second of those is the ordinary state of nearly every cave — so asking unconditionally
    // would fail on almost every cave page and write an expected error to the browser console each
    // time. The caller decides, because it already holds the list of uploads.
    enabled: !!caveId && hasMeasurableSurvey,
    // Measured once when the survey file is read and stored beside it, so this changes only when a
    // file is uploaded or removed — both of which empty this key explicitly.
    staleTime: 5 * 60_000,
    // Two of the three answers this route gives are final: a cave the caller may not read or may
    // not place exactly, and a cave whose network has never been measured. Neither changes by
    // being asked again.
    retry: false,
  });
}

/** Where a cave's passage sits vertically, and the levels it appears to be cut at. */
export type CaveHypsometry = components['schemas']['CaveHypsometryDto'];

/** Where the entrances under an area sit vertically. */
export type AreaHypsometry = components['schemas']['AreaHypsometryDto'];

/** The histogram and the levels proposed from it. */
export type ElevationBandProposal = components['schemas']['ElevationBandProposal'];

/** One interval of height in an elevation histogram. */
export type ElevationBin = components['schemas']['ElevationBin'];

/** One proposed level. */
export type ElevationBand = components['schemas']['ElevationBand'];

/** What somebody decided one cave's levels are, or the fact that nobody has. */
export type CaveLevelBands = components['schemas']['CaveLevelBandsDto'];

/** One level of a saved reading, as a person wrote it down. */
export type SavedElevationBand = components['schemas']['SavedElevationBand'];

/** A cave's passage trends against the structure mapped around it. */
export type CaveStructureComparison = components['schemas']['CaveStructureComparisonDto'];

/** An area's depression alignments against the structure mapped in it. */
export type AreaStructureComparison = components['schemas']['AreaStructureComparisonDto'];

/** How far apart two roses are. */
export type RoseDivergence = components['schemas']['RoseDivergence'];

export function useCaveHypsometry(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.caveHypsometry(caveId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{id}/hypsometry', { params: { path: { id: caveId! } } })),
    enabled: !!caveId,
    staleTime: 5 * 60_000,
    // A cave this caller may read but not place exactly is refused with the same answer as one
    // that does not exist. Retrying asks the same question again.
    retry: false,
  });
}

/**
 * How much rock lies over a cave's passages, along their length.
 *
 * This is cave data rather than a terrain figure: the curve is the passage set against the surface
 * above it, so anything holding it can work out where the passage runs. A caller who may read the
 * cave but may not place it exactly is refused with the same "no such cave" a never-created cave
 * gets, which is why nothing here retries and why the panel drawing it renders nothing at all on an
 * error rather than an empty card.
 */
export type CaveOverburden = components['schemas']['CaveOverburdenDto'];

/** One reading along the passage: where it was taken, and what the ground was found to be there. */
export type CaveOverburdenSample = components['schemas']['CaveOverburdenSampleDto'];

/** Whether a ground height could be read at one place, and if not, why not. */
export type DemSampleOutcome = components['schemas']['DemSampleOutcome'];

export function useCaveOverburden(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.caveOverburden(caveId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{id}/overburden', { params: { path: { id: caveId! } } })),
    enabled: !!caveId,
    // Recomputed per request from line work and from prepared elevation data, neither of which
    // changes without somebody uploading or building something.
    staleTime: 5 * 60_000,
    // A cave this caller may read but not place exactly is refused with the same answer as one that
    // does not exist. Retrying asks the same question again.
    retry: false,
  });
}

export function useAreaHypsometry(areaId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.areaHypsometry(areaId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/features/{id}/entrance-hypsometry', {
          params: { path: { id: areaId! } },
        }),
      ),
    enabled: !!areaId && enabled,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

export function useCaveLevelBands(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.caveLevelBands(caveId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{id}/level-bands', { params: { path: { id: caveId! } } })),
    enabled: !!caveId,
    retry: false,
  });
}

export function useSaveCaveLevelBands(caveId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { bands: SavedElevationBand[]; note: string | null }) =>
      unwrap(
        api.PUT('/api/v1/caves/{id}/level-bands', {
          params: { path: { id: caveId } },
          body,
        }),
      ),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: queryKeys.caveLevelBands(caveId) }),
  });
}

export function useClearCaveLevelBands(caveId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () =>
      unwrap(api.DELETE('/api/v1/caves/{id}/level-bands', { params: { path: { id: caveId } } })),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: queryKeys.caveLevelBands(caveId) }),
  });
}

/**
 * A cave's passage rose against the structure mapped around it.
 *
 * `areaId` scopes the structure to an area of the containment hierarchy instead of a buffer; it is
 * part of the query key because the two scopes are two different answers to two different
 * questions, and caching them together would show one under the other's heading.
 */
export function useCaveStructureComparison(caveId: string | undefined, areaId?: string) {
  return useQuery({
    queryKey: queryKeys.caveStructureComparison(caveId ?? '', areaId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/caves/{id}/structure-comparison', {
          // The contract names its query parameters as the request record does; spelled here the
          // way the generated types spell them rather than lower-cased and left to the server's
          // case-insensitive binding to rescue.
          params: { path: { id: caveId! }, query: areaId ? { AreaId: areaId } : {} },
        }),
      ),
    enabled: !!caveId,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

export function useAreaStructureComparison(areaId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.areaStructureComparison(areaId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/features/{id}/structure-comparison', {
          params: { path: { id: areaId! } },
        }),
      ),
    enabled: !!areaId && enabled,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

/** What one karst area adds up to, over the caves declared to be in it. */
export type AreaKarstStatistics = components['schemas']['AreaKarstStatisticsDto'];

/** One reading behind the karstification index, present or absent. */
export type KarstificationComponent = components['schemas']['KarstificationComponentDto'];

/** One cave standing at an end of a range in an area. */
export type AreaCaveExtreme = components['schemas']['AreaCaveExtremeDto'];

/**
 * Counts, densities, totals and a classed index for one area.
 *
 * `retry: false` for the reason every location-protected statistic here has it: an area this
 * caller may not read answers exactly as one that is not there, so asking again asks the same
 * refused question and only delays the empty state.
 */
export function useAreaKarstStatistics(areaId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.areaKarstStatistics(areaId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/features/{id}/karst-statistics', {
          params: { path: { id: areaId! } },
        }),
      ),
    enabled: !!areaId && enabled,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

export type DensityGrid = components['schemas']['DensityGridDto'];
export type DensityCell = components['schemas']['DensityCellDto'];
export type PointPattern = components['schemas']['PointPatternDto'];

export interface DensityQuery {
  bbox: string;
  /**
   * Omitted on the first request on purpose. The finest cell an installation will publish is its
   * location-protection grid, which the client does not know and must not guess: asking without a
   * cell size gets the floor and the payload states what it was, so a control can offer multiples
   * of a real number instead of discovering the edge by being refused.
   */
  cellMetres?: number;
  bandwidthMetres?: number;
  areaId?: string;
}

/**
 * How thickly cave entrances sit over a window, as a grid and as a smoothed surface.
 *
 * <p>
 * `retry` is off on purpose. The two interesting failures here are refusals with a stable code —
 * a cell finer than the location-protection grid, and a window that would be more cells than one
 * answer holds — and neither becomes true on a second attempt. Retrying them only delays the
 * message the control needs to show.
 * </p>
 */
export function useMapDensity(query: DensityQuery | undefined) {
  return useQuery({
    queryKey: queryKeys.mapDensity(
      query?.bbox ?? '',
      query?.cellMetres ?? null,
      query?.bandwidthMetres ?? null,
      query?.areaId,
    ),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/map/density', {
          params: {
            query: {
              bbox: query!.bbox,
              cellMetres: query!.cellMetres,
              bandwidthMetres: query!.bandwidthMetres,
              areaId: query!.areaId,
            },
          },
        }),
      ),
    enabled: !!query,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

/**
 * Imperative fetch of the same grid, for the map's density overlay (not a hook).
 *
 * The overlay asks again on every pan, so it lives outside the query cache: a cache entry per
 * window would only grow, and an answer for a window the reader has already left is discarded
 * rather than kept. The signal lets a newer window abort the request it supersedes.
 */
export async function fetchMapDensity(query: DensityQuery, signal?: AbortSignal): Promise<DensityGrid> {
  return unwrap(
    api.GET('/api/v1/map/density', {
      params: {
        query: {
          bbox: query.bbox,
          cellMetres: query.cellMetres,
          bandwidthMetres: query.bandwidthMetres,
          areaId: query.areaId,
        },
      },
      signal,
    }),
  );
}

/**
 * A window's grid at the finest cell that fits it, found by the same step search the map overlay
 * uses: no cell on the first request, and when the floor makes the window too many cells, the
 * multiple the refusal's own numbers say fits. The answer is either the grid or the refusal's code,
 * so a caller can tell "too wide even when coarsened" from a grid with nothing in it.
 */
export function useSteppedMapDensity(query: { box: LonLatBox; areaId?: string } | undefined) {
  return useQuery({
    queryKey: queryKeys.mapDensityStepped(query ? densityBboxParam(query.box) : '', query?.areaId),
    queryFn: ({ signal }) =>
      fetchSteppedDensity(
        query!.box,
        null,
        (request, requestSignal) => fetchMapDensity({ ...request, areaId: query!.areaId }, requestSignal),
        signal,
      ),
    enabled: !!query,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

export type KarstIndexWindow = components['schemas']['KarstIndexWindowDto'];
export type KarstIndexArea = components['schemas']['KarstIndexAreaDto'];
export type KarstificationClass = components['schemas']['KarstificationClass'];

export interface KarstIndexQuery {
  /** The window, `west,south,east,north` in degrees. */
  bbox: string;
  /** The zoom the outlines will be drawn at; the server simplifies them to about one pixel. */
  zoom: number;
}

/**
 * Imperative fetch of the karstification index over the areas in a window, for the map's
 * choropleth (not a hook, for the same reason as the density grid: it is asked again on every
 * pan, and a superseded window is aborted rather than cached).
 */
export async function fetchMapKarstIndex(query: KarstIndexQuery, signal?: AbortSignal): Promise<KarstIndexWindow> {
  return unwrap(
    api.GET('/api/v1/map/karst-index', {
      params: { query: { bbox: query.bbox, zoom: query.zoom } },
      signal,
    }),
  );
}

export interface PointPatternQuery {
  bbox: string;
  simulations: number;
  seed: number;
  areaId?: string;
}

/**
 * Whether those entrances are arranged more thickly, more evenly, or more directionally than
 * chance would arrange them. The seed travels in the query key as well as in the request, so two
 * readers looking at the same window and the same seed are looking at the same band.
 */
export function useMapPointPattern(query: PointPatternQuery | undefined) {
  return useQuery({
    queryKey: queryKeys.mapPointPattern(
      query?.bbox ?? '',
      query?.simulations ?? 0,
      query?.seed ?? 0,
      query?.areaId,
    ),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/map/point-pattern', {
          params: {
            query: {
              bbox: query!.bbox,
              simulations: query!.simulations,
              seed: query!.seed,
              areaId: query!.areaId,
            },
          },
        }),
      ),
    enabled: !!query,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

export function useCaveOrientation(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.caveOrientation(caveId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/caves/{id}/orientation', { params: { path: { id: caveId! } } })),
    enabled: !!caveId,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

/**
 * The measured shape of a drawn outline — a doline's area, how round it is, how long and how wide,
 * which way it lies, and where its middle is.
 *
 * Every length is metres and every area square metres. They are measured in the installation's
 * working coordinate system rather than in the degrees the outline is stored in, because a degree
 * is not a unit of length and its size on the ground changes with latitude.
 */
export type FeatureMorphometry = components['schemas']['FeatureMorphometryDto'];

/**
 * Asks for one outline's measurements.
 *
 * A reader who may see the feature but may not be told where it is gets no answer at all — the
 * server spells that as "no such feature", because a shape and a bearing place a doline as surely
 * as a coordinate does. So a failure here means the card simply does not appear; it is never an
 * empty set of figures, which would read as "this doline has no size".
 */
export function useFeatureMorphometry(id: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.featureMorphometry(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/features/{id}/morphometry', { params: { path: { id: id! } } })),
    enabled: enabled && !!id,
    // Measured from geometry that only changes when somebody redraws the outline.
    staleTime: 60_000,
    retry: false,
  });
}

/**
 * How close two caves come to each other: the shortest line between their line work in three
 * dimensions, split into its horizontal and vertical parts, with a bearing.
 *
 * `absence` says why there is no measurement when there is none, and is never blank — a cave with
 * no line work and a cave whose line work was drawn in plan with no depths are different answers,
 * and both are different from "these two have not been compared".
 */
export type ClosestApproach = components['schemas']['ClosestApproachDto'];

/**
 * Asks how close two caves come.
 *
 * Withheld entirely unless this caller may place both caves exactly — not rounded, not snapped.
 * The refusal is spelled "no such cave", so a guarded cave and a cave that never existed answer
 * alike and the query simply fails; whatever shows this shows nothing rather than a blank number.
 */
export function useClosestApproach(id: string | undefined, other: string | undefined) {
  return useQuery({
    queryKey: queryKeys.closestApproach(id ?? '', other ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/caves/{id}/closest-approach/{other}', {
          params: { path: { id: id!, other: other! } },
        }),
      ),
    enabled: !!id && !!other && id !== other,
    staleTime: 60_000,
  });
}
