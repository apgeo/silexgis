// SPDX-License-Identifier: AGPL-3.0-or-later
import type { CaveListItem, CaveListParams, RegistryClustering, RegistryClusteringParams } from '../../api/hooks.ts';

/**
 * Joining the server's grouping of caves to the points a cave list draws.
 *
 * <p>
 * Everything here is a reshaping and nothing here is a computation: the groups, the scaling, the
 * eligibility and the account of who was left out are all the server's, taken from one answer. A
 * second grouping worked out on this side would be a second answer to the same question, and the
 * two would disagree the moment either changed.
 * </p>
 * <p>
 * The one judgement this module makes is whether the grouping may be asked for at all — see
 * {@link clusteringScopeFor}.
 * </p>
 */

/**
 * The two measures the cross-cave scatter draws, and therefore the two the grouping is asked over.
 *
 * Grouping over exactly the measures on the axes is a deliberate restraint. A grouping over more
 * measures than are drawn produces colours whose arrangement on screen has no visible reason, and
 * a reader cannot tell such a picture from a broken one.
 */
export const scatterClusteringMeasures = 'surveyedLength,depth';

/**
 * Every narrowing a cave list can hold, and what the grouping route can do with it.
 *
 * <p>
 * This is a `Record` over the list's own parameter names rather than a comment, so a narrowing
 * added to the list later fails to compile until somebody has decided which of the three it is.
 * That matters more than it looks: the failure it prevents is silent. A grouping asked without a
 * narrowing the points were filtered by is a grouping of a larger set of caves, painted onto a
 * smaller one, and every colour on screen would look exactly as confident as a correct one.
 * </p>
 * <ul>
 *   <li><b>carried</b> — the grouping route accepts the same narrowing, so it is passed on.</li>
 *   <li><b>unaskable</b> — the grouping route has no such parameter. The grouping is not asked
 *       for at all while the narrowing is in force, and the scatter stays uncoloured.</li>
 *   <li><b>ordering</b> — decides which caves are drawn, not which caves exist. The grouping is
 *       over the whole narrowed set either way, and each drawn cave carries its own group.</li>
 * </ul>
 */
const listNarrowings: Record<keyof CaveListParams, 'carried' | 'unaskable' | 'ordering'> = {
  caveTypeId: 'carried',
  // Both routes take a region, and they do not mean the same thing by it: the list matches any
  // region containing the value, ignoring case, while the grouping matches the region equal to it.
  // Passing the value on would therefore group a subset — often an empty one, for a lowercase or
  // partial value — and paint it over a larger set of points without either side noticing. Until
  // the two agree on what a region value means, a region narrowing cannot be carried.
  region: 'unaskable',
  search: 'unaskable',
  tag: 'unaskable',
  minLength: 'unaskable',
  bbox: 'unaskable',
  page: 'ordering',
  pageSize: 'ordering',
  sort: 'ordering',
};

/** An empty string is how the list spells "no search", and it narrows nothing. */
function isSet(value: unknown): boolean {
  return value !== undefined && value !== null && value !== '';
}

/**
 * The grouping request that asks about exactly the caves this list is showing, or `null` when no
 * such request can be written.
 *
 * Returning `null` rather than a best effort is the whole point. The alternative — dropping the
 * narrowings the route cannot express and asking anyway — answers a different question in a way
 * neither the answer nor the picture would ever admit to.
 */
export function clusteringScopeFor(params: CaveListParams): RegistryClusteringParams | null {
  for (const [name, kind] of Object.entries(listNarrowings)) {
    if (kind === 'unaskable' && isSet(params[name as keyof CaveListParams])) return null;
  }
  return {
    measures: scatterClusteringMeasures,
    caveTypeId: params.caveTypeId,
  };
}

/**
 * The group labels the server published a summary for.
 *
 * A group holding fewer caves than the publishable floor keeps its label and its count so the
 * counts still reconcile with the eligible total, but its middle and its width are withheld —
 * a mean over two caves is those two caves' readings with one arithmetic step in front of it.
 * Such a group is not drawn as a group here either; its caves are drawn, uncoloured, among the
 * ones the grouping could not place.
 */
export function publishableClusters(clustering: RegistryClustering): Set<number> {
  const published = new Set<number>();
  for (const cluster of clustering.clusters) {
    if (cluster.count >= clustering.minimumPublishableClusterSize && cluster.centre !== null) {
      published.add(cluster.index);
    }
  }
  return published;
}

/** Which published group each cave fell in, by cave id. Caves in withheld groups are absent. */
export function clusterByCave(clustering: RegistryClustering): Map<string, number> {
  const published = publishableClusters(clustering);
  const byCave = new Map<string, number>();
  for (const assignment of clustering.assignments) {
    if (published.has(assignment.cluster)) byCave.set(assignment.caveId, assignment.cluster);
  }
  return byCave;
}

/** One point on the scatter, with the cave it came from kept so a group label can reach it. */
export interface ScatterPoint {
  caveId: string;
  point: [number, number];
}

/**
 * Length against depth for the caves that recorded both, keeping the cave id.
 *
 * A cave missing either measure is absent rather than plotted at zero, and a depth is taken as a
 * magnitude because the sign is a direction rather than a size.
 */
export function scatterPointsFor(caves: CaveListItem[]): ScatterPoint[] {
  const points: ScatterPoint[] = [];
  for (const cave of caves) {
    const x = cave.surveyedLength;
    const y = cave.depth;
    if (typeof x !== 'number' || typeof y !== 'number') continue;
    const depth = Math.abs(y);
    if (x <= 0 || depth <= 0) continue;
    points.push({ caveId: cave.id, point: [x, depth] });
  }
  return points;
}

/** A set of points drawn as one series, sharing a group label — or none, for the ungrouped. */
export interface ScatterGroup {
  /** The server's group label, or `null` for caves it placed in no published group. */
  cluster: number | null;
  points: Array<[number, number]>;
}

/**
 * The drawn points, split into one series per published group with the remainder in a series of
 * its own.
 *
 * <p>
 * The ungrouped series is not an afterthought: it holds every cave on this page that the grouping
 * could not take — for want of a measurement, or because the group it fell in was too small to
 * publish. Dropping those points would quietly shrink the picture to the caves the grouping
 * happened to like, which is the failure the whole population account exists to prevent.
 * </p>
 *
 * Returns `null` when there is no grouping to show, so the caller can draw the plain scatter it
 * drew before rather than a legend of one entry that explains nothing.
 */
export function clusteredScatter(
  caves: CaveListItem[],
  clustering: RegistryClustering | undefined,
): ScatterGroup[] | null {
  const points = scatterPointsFor(caves);
  if (points.length === 0 || clustering === undefined) return null;

  const byCave = clusterByCave(clustering);
  const grouped = new Map<number | null, Array<[number, number]>>();
  for (const { caveId, point } of points) {
    const cluster = byCave.get(caveId) ?? null;
    const bucket = grouped.get(cluster);
    if (bucket === undefined) grouped.set(cluster, [point]);
    else bucket.push(point);
  }

  // A picture where no drawn cave fell in a published group is not a coloured scatter with one
  // entry in its legend; it is the plain scatter, and saying otherwise would offer "not grouped"
  // as though it were a finding about these caves rather than the absence of one.
  if (![...grouped.keys()].some((cluster) => cluster !== null)) return null;

  // Groups in the server's own label order, and the ungrouped last — the labels carry no rank,
  // so the only ordering available is the one the answer arrived in.
  return [...grouped.entries()]
    .sort((a, b) => (a[0] ?? Number.MAX_SAFE_INTEGER) - (b[0] ?? Number.MAX_SAFE_INTEGER))
    .map(([cluster, groupPoints]) => ({ cluster, points: groupPoints }));
}
