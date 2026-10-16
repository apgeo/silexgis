// SPDX-License-Identifier: AGPL-3.0-or-later
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components, paths } from '../schema';
import { unwrap } from './shared.ts';
import { queryKeys } from './queryKeys.ts';

export type DashboardSummary = components['schemas']['DashboardSummaryDto'];
export type DashboardActivityItem = components['schemas']['DashboardActivityItemDto'];
export type DashboardActivityKind = components['schemas']['DashboardActivityKind'];

export function useDashboardSummary() {
  return useQuery({
    queryKey: queryKeys.dashboardSummary,
    queryFn: () => unwrap(api.GET('/api/v1/dashboard/summary')),
    // Counts and the activity feed move as other people edit; a short window keeps the
    // page from refetching on every visit without going stale over a working session.
    staleTime: 60_000,
  });
}

// ---------------------------------------------------------------------------
// Trip statistics
// ---------------------------------------------------------------------------

/**
 * What a person, a cave, a club or a camp adds up to across trips.
 *
 * Every figure is counted over the trips the caller may read, so two people legitimately see
 * different totals for the same subject. That is a fact about the answer, not about the request —
 * whatever shows these has to say so on the screen beside them, or the difference is reported as a
 * bug and repaired by removing the filter.
 */
export type TripStatistics = components['schemas']['TripStatisticsDto'];

/** Which thing is being added up. */
export type StatisticsSubject = 'caver' | 'cave' | 'cavingGroup' | 'expedition';

export function useTripStatistics(
  subject: StatisticsSubject,
  id: string | undefined,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.tripStatistics(subject, id ?? ''),
    queryFn: () => {
      const path = { id: id! };
      // A named branch per subject rather than a trailing fallthrough: a subject added to the
      // union without a branch of its own would otherwise be asked about as a club, and answered.
      switch (subject) {
        case 'caver':
          return unwrap(api.GET('/api/v1/stats/cavers/{id}', { params: { path } }));
        case 'cave':
          return unwrap(api.GET('/api/v1/stats/caves/{id}', { params: { path } }));
        case 'cavingGroup':
          return unwrap(api.GET('/api/v1/stats/caving-groups/{id}', { params: { path } }));
        case 'expedition':
          return unwrap(api.GET('/api/v1/stats/expeditions/{id}', { params: { path } }));
      }
    },
    enabled: enabled && !!id,
    // Derived on every request from trips that change slowly; a page revisited within the minute
    // does not need to ask again.
    staleTime: 30_000,
    // A caller who may not read the subject is refused, and the surface simply does not appear.
    retry: false,
  });
}

/**
 * One measured column of the registry, as the caller may read it: the intervals it falls into,
 * the percentiles asked for, and the two fits where the sample supports them.
 *
 * The parameters are handed on exactly as the screen holds them, unclamped and uncorrected. A bin
 * count outside what the registry will publish, or a word that names no measurement, comes back
 * as a refusal saying which control is wrong — which is the answer the reader needs. Quietly
 * substituting a legal value would draw a different distribution under the same address.
 */
export type RegistryMeasure = components['schemas']['RegistryMeasure'];

/** One interval of a distribution, and whether it is several of them joined together. */
export type RegistryDistributionBin = components['schemas']['DistributionBin'];

/** One requested fraction, and the value at it — null when there was nothing to take it from. */
export type RegistryPercentileRow = components['schemas']['RegistryPercentileRow'];

/** A fitted lognormal, present only when the sample was large enough to mean anything. */
export type RegistryLognormalFit = components['schemas']['LognormalFit'];

/** A fitted upper tail, present only when the tail was long enough to mean anything. */
export type RegistryParetoTailFit = components['schemas']['ParetoTailFit'];

/** How one measured column is distributed over the caves the caller may read. */
export type RegistryDistribution = components['schemas']['RegistryDistribution'];

/** Everything a distribution can be asked, scope included, spelled as the route spells it. */
export type RegistryDistributionParams = NonNullable<
  paths['/api/v1/stats/registry/distribution']['get']['parameters']['query']
>;

/**
 * The distribution of one measurement over the caves this caller may read.
 *
 * The previous answer is kept while a new one loads, because re-binning and re-filtering are
 * things somebody is clicking: a chart that blanks out and returns under the cursor reads as
 * breakage rather than as an answer.
 */
export function useRegistryDistribution(params: RegistryDistributionParams) {
  return useQuery({
    queryKey: queryKeys.registryDistribution(params),
    queryFn: () =>
      unwrap(api.GET('/api/v1/stats/registry/distribution', { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

/** How two measured columns move together, over the caves recording both. */
export type RegistryCorrelation = components['schemas']['RegistryCorrelationDto'];

/** Everything a correlation can be asked, scope included, spelled as the route spells it. */
export type RegistryCorrelationParams = NonNullable<
  paths['/api/v1/stats/registry/correlation']['get']['parameters']['query']
>;

/**
 * How two measurements of a cave move together, over the caves this caller may read.
 *
 * The answer is the relationship and not the caves behind it: a slope, an intercept, a goodness
 * figure and the number of pairs they were taken over. Every one of those but the count is null
 * when fewer than two caves recorded both, and they are handed on null rather than turned into
 * zeros — a zero slope is a claim that the two measurements are unrelated, which is a different
 * statement from having nothing to say.
 */
export function useRegistryCorrelation(params: RegistryCorrelationParams) {
  return useQuery({
    queryKey: queryKeys.registryCorrelation(params),
    queryFn: () =>
      unwrap(api.GET('/api/v1/stats/registry/correlation', { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

/** One region and how many caves in scope stand under it; a null region is a row of its own. */
export type RegistryRegionRow = components['schemas']['RegistryRegionRow'];

/** How the caves in scope divide between regions, and the total they were taken from. */
export type RegistryRegionBreakdown = components['schemas']['RegistryRegionBreakdownDto'];

/** Everything a regional breakdown can be asked, which is the scope and nothing else. */
export type RegistryRegionsParams = NonNullable<
  paths['/api/v1/stats/registry/regions']['get']['parameters']['query']
>;

/**
 * What a narrowed set of the registry adds up to per region, for the caves this caller may read.
 *
 * The rows and the total are counted under different rules and are not expected to reconcile: the
 * total counts every cave in scope the caller may read, the rows only those they may also place.
 * The difference is a fact about the answer rather than a cave that went missing, so neither
 * figure is adjusted here to make the other look right.
 */
export function useRegistryRegions(params: RegistryRegionsParams) {
  return useQuery({
    queryKey: queryKeys.registryRegions(params),
    queryFn: () => unwrap(api.GET('/api/v1/stats/registry/regions', { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

/** How many caves recorded one named measure, and how many were left out for want of it alone. */
export type RegistryClusterCoverage = components['schemas']['RegistryClusterCoverageDto'];

/** Who the grouping was offered, who it could take, and who it had to leave out. */
export type RegistryClusterPopulation = components['schemas']['RegistryClusterPopulationDto'];

/** The middle and the spread one measure was standardised by before distances were taken. */
export type RegistryClusterScaling = components['schemas']['RegistryClusterScalingDto'];

/** One group: its label, how many caves fell in it, and its middle when it is large enough to publish one. */
export type RegistryCluster = components['schemas']['RegistryClusterDto'];

/** Which group one cave fell in, and how far from that group's middle it sits. */
export type RegistryClusterAssignment = components['schemas']['RegistryClusterAssignmentDto'];

/** How wide the groups are against how far apart they are — the only figure that can contradict them. */
export type RegistryClusterSeparation = components['schemas']['RegistryClusterSeparationDto'];

/** Which caves resemble each other over the measures asked for, and the account of who was left out. */
export type RegistryClustering = components['schemas']['RegistryClusteringDto'];

/** Everything a grouping can be asked, scope included, spelled as the route spells it. */
export type RegistryClusteringParams = NonNullable<
  paths['/api/v1/stats/registry/clustering']['get']['parameters']['query']
>;

/**
 * Which caves resemble each other over the measures asked for.
 *
 * The answer is not only the groups. It carries the account of who could be grouped and who could
 * not, and for want of which measurement — which is the half a reader has to see first, because a
 * grouping over the best-surveyed tenth of a registry is internally consistent and reads exactly
 * like a grouping over the registry.
 *
 * `enabled` is a parameter rather than an internal guess because the caller is the only one that
 * knows whether the set it is drawing and the set it would be asking about are the same set. A
 * grouping fetched for a wider set than the points on screen colours them with a claim about
 * caves nobody is looking at, and nothing in the answer would reveal the mismatch.
 */
export function useRegistryClustering(params: RegistryClusteringParams, enabled = true) {
  return useQuery({
    queryKey: queryKeys.registryClustering(params),
    queryFn: () =>
      unwrap(api.GET('/api/v1/stats/registry/clustering', { params: { query: params } })),
    // Deliberately no carrying of the previous answer across a change of scope. Keeping it is the
    // right trade for a page of rows, where the shape barely moves and a blank table flickers; it
    // is the wrong one here, because the previous answer is a grouping of a different set of caves
    // and the list it would be painted over refetches faster than a grouping does. For the length
    // of that window the colours and the population sentence would both describe caves nobody is
    // looking at, and nothing in either would admit it. An uncoloured scatter for a moment is the
    // honest state.
    enabled,
  });
}
