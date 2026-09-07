// SPDX-License-Identifier: AGPL-3.0-or-later
import type {
  TripLogFacetParams,
  TripLogGroupingParams,
  TripLogListParams,
} from '../../api/hooks.ts';

/**
 * The trip listing's filter, and its translation to and from the address bar.
 *
 * The address is where the filter lives, not a copy of somewhere it lives properly: a narrowed
 * listing is a link somebody pastes into a message, and reloading it, opening it in a second tab
 * or walking back to it must all show the same rows. Keeping it in component state instead makes
 * every one of those a different listing that looks like the same one.
 *
 * So the address is read by people as well as by browsers. Each facet is one readable key whose
 * value is a comma-separated list, rather than a repeated key or an encoded blob, because a
 * filter somebody can check by looking at it is one they can correct by editing it. A default is
 * written as an absent key and never as an explicit one, so the unnarrowed listing has a bare
 * address and two ways of saying "nothing is chosen" cannot both exist.
 *
 * A word the address carries that this application does not know is not corrected here. The
 * server refuses it with a code naming which control is wrong, which is an answer a reader can
 * act on; silently dropping it would show a listing that is not the one the link asked for.
 */
export interface TripListFilter {
  page: number;
  pageSize: number;
  search: string;
  from?: string;
  to?: string;
  /** Trip type ids. Alternatives within the facet: a trip of any of them is kept. */
  types: string[];
  /** Lifecycle words, spelled the way the contract spells them. */
  states: string[];
  /** Audience words, spelled the way the contract spells them. */
  visibilities: string[];
  /** Undefined is no opinion, which is not the same as "nothing went wrong". */
  hadIncident?: boolean;
  /** People on the roster. Alternatives within the facet: a trip either was on is kept. */
  participantIds: string[];
  /**
   * Area features. Alternatives within the facet, and each one reaches everything the containment
   * hierarchy puts inside it, so a massif answers for the trips that named its valleys.
   */
  areaIds: string[];
  /** One cave the trips name. Carried through rather than offered: the cave page sets it. */
  caveId?: string;
  /** One camp the trips belong to. Carried through for the same reason. */
  expeditionId?: string;
  /** `date`, `title`, `created` or `updated`, a leading minus for descending. */
  sort?: string;
  /**
   * What the shape above the table is cut by, and then by. Carried in the address like the
   * narrowings, so a link hands over the shape somebody was looking at and not only the rows —
   * but not cleared by a reset, because how a listing is arranged is not a narrowing of it.
   */
  groupBy: string;
  thenBy: string;
}

export const DefaultTripPageSize = 20;

/** Not slicing the listing at all, which is the ordinary state and so the written-out default. */
export const NoGrouping = 'none';

/** The unnarrowed listing: every facet empty, which means unconstrained and not "match nothing". */
export const EmptyTripListFilter: TripListFilter = {
  page: 1,
  pageSize: DefaultTripPageSize,
  search: '',
  types: [],
  states: [],
  visibilities: [],
  participantIds: [],
  areaIds: [],
  groupBy: NoGrouping,
  thenBy: NoGrouping,
};

/**
 * The narrowings a reset clears. Which page somebody is on, how big it is and what order it is in
 * are not among them: they say where the reader is standing, not what they asked to see, and
 * throwing them away would be a second surprise on top of the one they asked for.
 */
export const clearedTripListFilter = (current: TripListFilter): TripListFilter => ({
  page: 1,
  pageSize: current.pageSize,
  sort: current.sort,
  search: '',
  types: [],
  states: [],
  visibilities: [],
  participantIds: [],
  areaIds: [],
  groupBy: current.groupBy,
  thenBy: current.thenBy,
});

/** Whether anything at all is narrowing the listing, which is what makes a reset offerable. */
export function isTripListNarrowed(filter: TripListFilter): boolean {
  return (
    filter.search.trim() !== '' ||
    filter.from !== undefined ||
    filter.to !== undefined ||
    filter.types.length > 0 ||
    filter.states.length > 0 ||
    filter.visibilities.length > 0 ||
    filter.hadIncident !== undefined ||
    filter.participantIds.length > 0 ||
    filter.areaIds.length > 0 ||
    filter.caveId !== undefined ||
    filter.expeditionId !== undefined
  );
}

const list = (raw: string | null): string[] =>
  raw === null
    ? []
    : raw
        .split(',')
        .map((word) => word.trim())
        .filter((word) => word !== '');

const one = (raw: string | null): string | undefined => (raw === null || raw === '' ? undefined : raw);

const positive = (raw: string | null, fallback: number): number => {
  const value = Number(raw);
  return Number.isInteger(value) && value > 0 ? value : fallback;
};

/** The filter an address describes. An absent key is the default, never an error. */
export function readTripListFilter(params: URLSearchParams): TripListFilter {
  const incident = one(params.get('hadIncident'));
  return {
    page: positive(params.get('page'), 1),
    pageSize: positive(params.get('pageSize'), DefaultTripPageSize),
    search: params.get('q') ?? '',
    from: one(params.get('from')),
    to: one(params.get('to')),
    types: list(params.get('types')),
    states: list(params.get('states')),
    visibilities: list(params.get('visibilities')),
    // Only the two words mean anything. Anything else is read as no opinion rather than as
    // "false", so a mistyped address shows more than was asked for and never less.
    hadIncident: incident === 'true' ? true : incident === 'false' ? false : undefined,
    participantIds: list(params.get('participantIds')),
    areaIds: list(params.get('areaIds')),
    caveId: one(params.get('caveId')),
    expeditionId: one(params.get('expeditionId')),
    sort: one(params.get('sort')),
    // An unrecognised word is left as it arrived rather than corrected: the server refuses it
    // with a code naming the control, which is an answer a reader can act on.
    groupBy: params.get('groupBy') ?? NoGrouping,
    thenBy: params.get('thenBy') ?? NoGrouping,
  };
}

/** The address a filter describes: every default absent, so the plain listing has a bare one. */
export function writeTripListFilter(filter: TripListFilter): URLSearchParams {
  const params = new URLSearchParams();
  const set = (key: string, value: string | undefined) => {
    if (value !== undefined && value !== '') {
      params.set(key, value);
    }
  };
  set('q', filter.search.trim());
  set('from', filter.from);
  set('to', filter.to);
  set('types', filter.types.join(','));
  set('states', filter.states.join(','));
  set('visibilities', filter.visibilities.join(','));
  set('hadIncident', filter.hadIncident === undefined ? undefined : String(filter.hadIncident));
  set('participantIds', filter.participantIds.join(','));
  set('areaIds', filter.areaIds.join(','));
  set('caveId', filter.caveId);
  set('expeditionId', filter.expeditionId);
  set('sort', filter.sort);
  if (filter.groupBy !== NoGrouping) {
    params.set('groupBy', filter.groupBy);
    if (filter.thenBy !== NoGrouping) {
      params.set('thenBy', filter.thenBy);
    }
  }
  if (filter.page > 1) {
    params.set('page', String(filter.page));
  }
  if (filter.pageSize !== DefaultTripPageSize) {
    params.set('pageSize', String(filter.pageSize));
  }
  return params;
}

/**
 * What the counts are asked about: the narrowings, and nothing that only decides how the rows are
 * handed over. Built from the same filter the page is built from, so the number beside an option
 * and the rows that option produces are two readings of one request.
 */
export function tripFacetQuery(filter: TripListFilter): TripLogFacetParams {
  const chosen = (values: string[]) => (values.length > 0 ? values.join(',') : undefined);
  return {
    search: filter.search.trim() || undefined,
    from: filter.from,
    to: filter.to,
    types: chosen(filter.types),
    states: chosen(filter.states),
    visibilities: chosen(filter.visibilities),
    hadIncident: filter.hadIncident,
    participantIds: chosen(filter.participantIds),
    areaIds: chosen(filter.areaIds),
    caveId: filter.caveId,
    expeditionId: filter.expeditionId,
  };
}

/** The page request: the same narrowings, plus where in the answer to stand and in what order. */
export function tripListQuery(filter: TripListFilter): TripLogListParams {
  return {
    ...tripFacetQuery(filter),
    page: filter.page,
    pageSize: filter.pageSize,
    sort: filter.sort,
  };
}

/**
 * What the slices are cut from and cut by: the same narrowings the page is built from, so the
 * shape above the table and the table itself are demonstrably about one set of trips.
 */
export function tripGroupingQuery(filter: TripListFilter): TripLogGroupingParams {
  return {
    ...tripFacetQuery(filter),
    groupBy: filter.groupBy,
    thenBy: filter.thenBy,
  };
}

/**
 * The address that hands the current filter to the map.
 *
 * The narrowings travel and nothing else does: which page somebody was on and what order the
 * table was in say where a reader was standing in a list, and a map has neither. The layer is
 * asked for by name in the same breath, because carrying a filter to a map showing no trips would
 * be a button that appears to do nothing.
 */
export function tripMapSearch(filter: TripListFilter): string {
  const params = writeTripListFilter({ ...filter, page: 1, sort: undefined });
  params.delete('groupBy');
  params.delete('thenBy');
  params.delete('pageSize');
  params.set('trips', '1');
  return params.toString();
}
