// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  keepPreviousData,
  queryOptions,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import { api, lastReadETag } from '../client.ts';
import type { components } from '../schema';
import {
  invalidateAfterDelete,
  unwrap,
  unwrapVoid,
  useInvalidateAttachments,
  useInvalidateHistory,
} from './shared.ts';
import type {
  ActivityState,
  AttachedEntityType,
  CaveDetail,
  MapLayerInfo,
  MyTripLogListParams,
  TripLogFacetParams,
  TripLogGroupingParams,
  TripLogListParams,
} from './types.ts';
import { queryKeys } from './queryKeys.ts';

export type TripLogFeatureCollection = components['schemas']['TripLogFeatureCollection'];

/** The narrowings the trip overlay may carry, spelled as the trip listing spells them. */
export interface TripLogMapFilter {
  from?: string;
  to?: string;
  types?: string[];
  states?: string[];
  visibilities?: string[];
  hadIncident?: boolean;
}

/**
 * Imperative fetch used by the OpenLayers trip overlay loader (not a hook).
 *
 * Every facet is sent as one comma-separated word list, which is how the trip listing spells the
 * same narrowings in its own address — so a filter carried from the list to the map arrives
 * unchanged rather than being translated into a second dialect on the way. An empty facet is
 * omitted entirely: an empty string would be a filter naming nothing, which the server is right
 * to refuse.
 */
export async function fetchTripLogFeatures(
  bbox: string,
  filter: TripLogMapFilter = {},
): Promise<TripLogFeatureCollection> {
  const list = (values?: string[]) => (values && values.length > 0 ? values.join(',') : undefined);
  return unwrap(api.GET('/api/v1/map/trip-logs', {
    params: {
      query: {
        bbox,
        from: filter.from,
        to: filter.to,
        types: list(filter.types),
        states: list(filter.states),
        visibilities: list(filter.visibilities),
        hadIncident: filter.hadIncident,
      },
    },
  }));
}
export type SearchTripItem = components['schemas']['SearchTripItemDto'];

export type TripLogInfo = components['schemas']['TripLogDto'];
export type TripLogWrite = components['schemas']['TripLogWriteRequest'];
export type TripType = components['schemas']['TripTypeDto'];
export type TripParticipant = components['schemas']['TripParticipantDto'];
export type TripParticipantRole = components['schemas']['TripParticipantRoleDto'];
export type TripParticipantWrite = components['schemas']['TripParticipantWrite'];
export type TagInfo = components['schemas']['TagDto'];
export type TaggingInfo = components['schemas']['TaggingDto'];

/** One option a filter panel may offer, and how many trips it would leave. */
export type TripFacetValue = components['schemas']['TripFacetValueDto'];

/** What every option in the trip listing's filter panel would leave, for this caller. */
export type TripListFacets = components['schemas']['TripListFacetsDto'];

export function useTripLogs(params: TripLogListParams) {
  return useQuery({
    queryKey: queryKeys.tripLogs(params),
    queryFn: () => unwrap(api.GET('/api/v1/trip-logs', { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

/**
 * How many trips each filter option would leave, counted over the trips this caller may read.
 *
 * Its own request rather than a shape of the listing's, because it is a different question about
 * the same query and every caller who only wants rows would otherwise pay for the counts. Keeping
 * the last answer on screen matters more here than anywhere: the panel is what somebody is
 * clicking, and options that vanish and return under the cursor make it unusable.
 */
export function useTripLogFacets(params: TripLogFacetParams) {
  return useQuery({
    queryKey: queryKeys.tripLogFacets(params),
    queryFn: () => unwrap(api.GET('/api/v1/trip-logs/facets', { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

/** One slice of a trip listing, and what it holds. */
export type TripGroup = components['schemas']['TripGroupDto'];

/** A trip listing broken into slices, over the trips this caller may read. */
export type TripListGrouping = components['schemas']['TripListGroupingDto'];

/**
 * A trip listing broken into slices.
 *
 * Its own request rather than a shape of the page's, for the same reason the counts are: it is a
 * different question about the same query, and nobody who only wants rows should pay for it. The
 * previous answer is kept on screen while a new one is fetched, because the panel is what
 * somebody is clicking and slices that vanish and return under the cursor make it unusable.
 */
export function useTripLogGrouping(params: TripLogGroupingParams, enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.tripLogGrouping(params),
    queryFn: () => unwrap(api.GET('/api/v1/trip-logs/grouping', { params: { query: params } })),
    placeholderData: keepPreviousData,
    enabled,
  });
}

/** One year of a filtered trip listing, and how much ground had been covered by the end of it. */
export type TripStatsYear = components['schemas']['TripStatsYearDto'];

/** How the filtered trips break down along one dimension, longest first. */
export type TripStatsBreakdown = components['schemas']['TripStatsBreakdownDto'];

/** What a filtered trip listing adds up to, over the trips this caller may read. */
export type TripStats = components['schemas']['TripStatsDto'];

/**
 * What the filtered trips add up to.
 *
 * It takes the listing's own narrowings and nothing else, so "the current filter" means one thing
 * on the insights page and on the list. A figure worked out from a second, similar-looking filter
 * would disagree with the table under the one condition nobody checks by eye: a reader who may
 * open only part of the archive. The previous answer is kept while a new one loads, because the
 * scope toggle and the filter are things somebody is clicking and charts that blank out and
 * return under the cursor read as breakage.
 */
export function useTripLogStats(params: TripLogFacetParams, enabled = true) {
  return useQuery({
    queryKey: queryKeys.tripLogStats(params),
    queryFn: () => unwrap(api.GET('/api/v1/trip-logs/stats', { params: { query: params } })),
    placeholderData: keepPreviousData,
    enabled,
  });
}

/**
 * The trips the signed-in account is on, soonest first.
 *
 * Its own key rather than a shape of the trip list's, because it is a different question with a
 * different answer for every reader, and because it goes stale as dates pass rather than as
 * people edit. The key sits under the trip prefix so writing a trip re-reads it for free.
 */
export function useMyTripLogs(params: MyTripLogListParams) {
  return useQuery({
    queryKey: queryKeys.myTripLogs(params),
    queryFn: () => unwrap(api.GET('/api/v1/trip-logs/mine', { params: { query: params } })),
    // Paging or narrowing keeps the rows on screen while the next answer arrives, rather than
    // emptying the table under whoever is reading it.
    placeholderData: keepPreviousData,
  });
}

/** How one trip is read and under which key it is held. */
function tripLogQuery(id: string) {
  return queryOptions({
    queryKey: queryKeys.tripLog(id),
    queryFn: () => unwrap(api.GET('/api/v1/trip-logs/{id}', { params: { path: { id } } })),
  });
}

export function useTripLog(id: string | undefined) {
  return useQuery({ ...tripLogQuery(id ?? ''), enabled: !!id });
}

function useInvalidateTripLogs() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['trip-logs'] });
}

/**
 * What happens once a write that answers the trip has answered.
 *
 * A write on the trip is checked against the version the caller holds, and the server hands the
 * version a write produced back with the answer — as the entity tag the transport files under the
 * trip's path, exactly as it files the one a read carries. So the cache takes the answer as the
 * trip's current reading, every surface holding the trip redraws from it at once, and the next
 * write carries the right version without a read in between. Everything else held under the trip
 * prefix — the lists, the calendar readings, the tracking log — is marked stale and read again in
 * the background; the mutation does not wait for that, because nothing a second save needs
 * depends on it.
 */
export function useTripLogWritten() {
  const queryClient = useQueryClient();
  return (trip: TripLogInfo) => {
    queryClient.setQueryData(queryKeys.tripLog(trip.id), trip);
    void queryClient.invalidateQueries({
      queryKey: ['trip-logs'],
      // The trip itself was just taken from the answer; asking for it again would read back
      // what is already held.
      predicate: ({ queryKey }) => !(queryKey[1] === 'detail' && queryKey[2] === trip.id),
    });
  };
}

/**
 * The invalidation handed back so a caller can wait for the re-read it starts — for the one
 * write on a trip whose answer is not the trip.
 *
 * A write that answers the trip hands its version over with the answer and nothing waits on a
 * read. A write that answers something else leaves the caller holding a trip that really is
 * behind — the roster from before it was written in — and a save made from that copy would carry
 * the old roster over the new one. The server lets a save through only against the version the
 * write produced, and here only a read records it; so this write is not finished until the trip
 * has been read back, and the precondition keeps doing exactly its job.
 */
function useReadTripLogsBack() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: ['trip-logs'] });
}

export function useCreateTripLog() {
  const written = useTripLogWritten();
  return useMutation({
    mutationFn: (body: TripLogWrite) => unwrap(api.POST('/api/v1/trip-logs', { body })),
    onSuccess: written,
  });
}

/**
 * The second of the two doors a trip is created through. Same body as the report door above and
 * the same trip afterwards; the one difference is the audience a request that names none falls
 * back to — the author's caving group rather than private, because a proposal only its author
 * can read is a proposal to nobody. The rule itself lives on the server; a caller that wants it
 * applied leaves `visibility` null.
 */
export function useCreateTripPlan() {
  const invalidate = useInvalidateTripLogs();
  return useMutation({
    mutationFn: (body: TripLogWrite) => unwrap(api.POST('/api/v1/trip-logs/plans', { body })),
    onSuccess: () => invalidate(),
  });
}

export type TripPlanDefault = components['schemas']['TripPlanDefaultDto'];

/**
 * The audience a trip this caller plans would get if they named none — the plan door's own rule,
 * answered before the trip exists so a form can say who will see it instead of reciting the rule.
 * It names the caller's own membership and nothing else, and is only worth asking while a plan is
 * being written, which is what `enabled` says.
 */
export function useTripPlanDefault(enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.tripPlanDefault,
    queryFn: () => unwrap(api.GET('/api/v1/trip-logs/plan-default')),
    enabled,
  });
}

export function useUpdateTripLog() {
  const written = useTripLogWritten();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    // `ifMatch` is for a caller whose values were read some time before the save — the edit
    // form: it names the version those values came from, so a change somebody else saved in
    // between is refused instead of overwritten. Left out, the save carries the newest version
    // read, which is right for a write made from what the page shows now.
    mutationFn: ({ id, body, ifMatch }: { id: string; body: TripLogWrite; ifMatch?: string }) =>
      unwrap(
        api.PUT('/api/v1/trip-logs/{id}', {
          params: { path: { id } },
          headers: ifMatch ? { 'If-Match': ifMatch } : undefined,
          body,
        }),
      ),
    onSuccess: (trip) => {
      invalidateHistory();
      written(trip);
    },
  });
}

export function useDeleteTripLog() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => unwrapVoid(api.DELETE('/api/v1/trip-logs/{id}', { params: { path: { id } } })),
    onSuccess: (_, id) => void invalidateAfterDelete(queryClient, ['trip-logs'], id),
  });
}

export type TripLogConfig = components['schemas']['TripLogConfigDto'];
export type DeletedTripLog = components['schemas']['DeletedTripLogDto'];

/**
 * What this installation does with a deleted trip: how many days it can still be put back, or
 * null when it is kept until somebody says otherwise. Asked so that a confirmation can say what
 * a delete is about to do rather than recite a number the installation may have changed — which
 * is why it is the server's answer and not a constant here. It changes only when an operator
 * changes it, so it is held for a long while.
 */
export function useTripLogConfig(enabled = true) {
  return useQuery({
    queryKey: queryKeys.tripLogConfig,
    queryFn: () => unwrap(api.GET('/api/v1/trip-logs/config')),
    staleTime: 30 * 60_000,
    enabled,
  });
}

/**
 * The deleted trips this caller may put back, most recently deleted first. Which those are is
 * decided on the server row by row — the right that deletes a trip is the right that restores
 * it — so an account that may delete nothing is answered with an empty list rather than refused.
 */
export function useDeletedTripLogs(page = 1, enabled = true) {
  return useQuery({
    queryKey: queryKeys.deletedTripLogs(page),
    queryFn: () =>
      unwrap(api.GET('/api/v1/trip-logs/deleted', { params: { query: { page, pageSize: 50 } } })),
    placeholderData: keepPreviousData,
    enabled,
  });
}

/**
 * Puts a deleted trip back. A write that answers the trip, and finished the way every other one
 * is: the answer is the trip as it now stands, with the version the restore produced, so the
 * page it opens on is drawn from it and its first save needs no read in between. Everything
 * else under the trip prefix is read again — the list it returns to, and the deleted list it
 * has just left. An import's own page says which of its trips are deleted, so that is read
 * again too.
 *
 * A refusal is news about the list the button was on: the trip's time ran out, or somebody else
 * put it back or it has gone for good. So the deleted list is read again then as well, rather
 * than left offering a row that can only be refused a second time.
 */
export function useRestoreTripLog() {
  const written = useTripLogWritten();
  const invalidateHistory = useInvalidateHistory();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/trip-logs/{id}/restore', { params: { path: { id } } })),
    onSuccess: (trip) => {
      invalidateHistory();
      void queryClient.invalidateQueries({ queryKey: ['import-batches'] });
      written(trip);
    },
    onError: () => void queryClient.invalidateQueries({ queryKey: ['trip-logs', 'deleted'] }),
  });
}

export type ReportTemplate = components['schemas']['ReportTemplateDto'];

/** The layouts a write-up may be built in. Every account may read them: choosing one is not editing one. */
export function useReportTemplates(enabled = true) {
  return useQuery({
    queryKey: queryKeys.reportTemplates,
    queryFn: () => unwrap(api.GET('/api/v1/report-templates')),
    enabled,
  });
}

/**
 * The layouts of one kind — a trip's or a camp's — for the chooser on that kind's write-up page.
 * Under the same key family as the full list, so a change to any layout refreshes both.
 */
export function useReportTemplatesOfKind(kind: 'trip' | 'expedition', enabled = true) {
  return useQuery({
    queryKey: [...queryKeys.reportTemplates, kind] as const,
    queryFn: () =>
      unwrap(api.GET('/api/v1/report-templates', { params: { query: { kind } } })),
    enabled,
  });
}

export type ReportTemplateWrite = components['schemas']['ReportTemplateRequest'];

function useInvalidateReportTemplates() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: queryKeys.reportTemplates });
}

/**
 * Stores a layout. A body whose lines cannot be read is refused by the server, which says which
 * line is at fault — the message is passed through rather than replaced, because the person
 * editing the layout is the only one who can act on it.
 */
export function useCreateReportTemplate() {
  const invalidate = useInvalidateReportTemplates();
  return useMutation({
    mutationFn: (body: ReportTemplateWrite) =>
      unwrap(api.POST('/api/v1/report-templates', { body })),
    onSuccess: invalidate,
  });
}

export function useUpdateReportTemplate() {
  const invalidate = useInvalidateReportTemplates();
  return useMutation({
    mutationFn: ({ id, ...body }: ReportTemplateWrite & { id: string }) =>
      unwrap(api.PUT('/api/v1/report-templates/{id}', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useDeleteReportTemplate() {
  const invalidate = useInvalidateReportTemplates();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/report-templates/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  });
}

/**
 * Writes the trip up and files the document in the trip's report slot.
 *
 * The document is built server-side, so nothing here hands it any of the trip's content — an
 * argument carrying what to write would be a second place the question of who may see what is
 * answered. What is filed is not this caller's own copy: a file attached to a trip is reachable
 * by everybody who may read that trip, so the server builds the filed one from the reading any
 * account has. The fuller copy is the download.
 */
export function useKeepTripReport() {
  const invalidateAttachments = useInvalidateAttachments();
  return useMutation({
    mutationFn: ({ id, templateId }: { id: string; templateId?: string }) =>
      unwrap(
        api.POST('/api/v1/trip-logs/{id}/report', {
          params: { path: { id }, query: templateId ? { templateId } : {} },
        }),
      ),
    onSuccess: () => invalidateAttachments(),
  });
}

/**
 * What the picture of a map in a trip's write-up is drawn from, handed over as it is already
 * held.
 *
 * Two things, and both are answers the write-up's page asks for anyway. The caves a trip names
 * are read one by one, by id, to print their names; each answer also says where that cave is as
 * far as this reader may know — exactly, approximately, or not at all — so the picture takes its
 * positions from those answers and from nowhere else. The catalogue of map backgrounds is what
 * every map in the application is built from.
 *
 * Nothing here is a new question. Each read goes by the key and the route the page's own reads
 * go by, so an answer already held is handed back as it is, and one still on its way is waited
 * for rather than asked for twice. That matters for more than thrift: a route of its own for
 * "where are this trip's caves" would be a second place the server decides what a reader may be
 * shown of them.
 *
 * A cave whose read fails — it stopped being readable between the trip's answer and this one —
 * is simply not among the answers, and a catalogue that cannot be read is no catalogue.
 */
export function useTripReportMapSources() {
  const queryClient = useQueryClient();
  return {
    caves: async (ids: readonly string[]): Promise<Map<string, CaveDetail>> => {
      const answers = await Promise.allSettled(
        ids.map((id) =>
          queryClient.ensureQueryData({
            queryKey: queryKeys.cave(id),
            queryFn: () => unwrap(api.GET('/api/v1/caves/{id}', { params: { path: { id } } })),
            retry: false,
          }),
        ),
      );
      const held = new Map<string, CaveDetail>();
      answers.forEach((answer, index) => {
        if (answer.status === 'fulfilled') {
          held.set(ids[index], answer.value);
        }
      });
      return held;
    },
    catalog: async (): Promise<MapLayerInfo[] | undefined> => {
      try {
        return await queryClient.ensureQueryData({
          queryKey: queryKeys.mapLayers,
          queryFn: () => unwrap(api.GET('/api/v1/map-layers')),
          staleTime: 5 * 60_000,
        });
      } catch {
        return undefined;
      }
    },
  };
}

/**
 * Moving a trip to another lifecycle state, through the one route that names the state it moves
 * to rather than a verb per move.
 *
 * It is a write on the trip and is checked against the version the user holds, so it carries
 * the precondition filed under the trip's own path — by the detail read or by the last write,
 * whichever came later. Which moves are legal from which state is the server's to decide — the
 * control only offers the ones a reader would expect, and a request the rules refuse comes back
 * as a conflict rather than being prevented here.
 */
export function useMoveTripLog() {
  const written = useTripLogWritten();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ id, state }: { id: string; state: ActivityState }) => {
      const etag = lastReadETag(`/api/v1/trip-logs/${id}`);
      return unwrap(
        api.POST('/api/v1/trip-logs/{id}/state', {
          params: { path: { id } },
          headers: etag ? { 'If-Match': etag } : undefined,
          body: { state },
        }),
      );
    },
    onSuccess: (trip) => {
      invalidateHistory();
      written(trip);
    },
  });
}

export type TripCalloutState = components['schemas']['TripCalloutState'];

export type TripCalloutArrangement = components['schemas']['TripCalloutRequest'];

/**
 * Arranges, changes or calls off the check that notices if a party does not come back.
 *
 * Part of planning the trip, so it is offered to whoever may change the trip and carries the
 * precondition header every other write to a trip carries — two people arranging different hours
 * is exactly the lost update it exists to catch. Clearing the alarm time is how the whole
 * arrangement is called off; there is no separate route for that, and none is wanted.
 *
 * The answer is the trip as it now stands, so the page redraws from it directly.
 */
export function useArrangeTripCallout() {
  const written = useTripLogWritten();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ id, ...body }: { id: string } & TripCalloutArrangement) => {
      const etag = lastReadETag(`/api/v1/trip-logs/${id}`);
      return unwrap(
        api.POST('/api/v1/trip-logs/{id}/callout', {
          params: { path: { id } },
          headers: etag ? { 'If-Match': etag } : undefined,
          body,
        }),
      );
    },
    onSuccess: (trip) => {
      invalidateHistory();
      written(trip);
    },
  });
}

/**
 * Says the party is out, which stops the overdue check.
 *
 * No precondition header, unlike every other write to a trip, and that is the server's rule
 * rather than an omission here: there is one value it can write, everybody entitled to call it is
 * saying the same thing, and a stale version would refuse the message that says people are safe.
 * The answer is the trip as it now stands, so the page redraws from it directly.
 */
export function useStandDownTripCallout() {
  const written = useTripLogWritten();
  return useMutation({
    mutationFn: ({ id }: { id: string }) =>
      unwrap(
        api.POST('/api/v1/trip-logs/{id}/callout/stand-down', { params: { path: { id } } }),
      ),
    onSuccess: written,
  });
}

export type TripInvitationInfo = components['schemas']['TripInvitationDto'];
export type TripInvitationList = components['schemas']['TripInvitationListDto'];
export type TripInvitationAnswer = components['schemas']['TripInvitationResponse'];

/**
 * Everybody on a trip's list and what each has said, in the order the server put them in.
 *
 * Three things on this answer are the server's conclusions and not raw rows: the place each
 * person holds in the order people answered in, whether they hold one of the trip's places or
 * are waiting for one, and whether this caller may write an answer for that person. Each has a
 * rule behind it that the client has no way to evaluate — the ordering skips anybody who has not
 * said yes, a hand-picked person keeps their place even past the limit, and answering for
 * somebody else depends on rights over the trip. Rendering them as they arrive is the whole
 * point; re-deriving any of them here would be a second copy of a rule free to drift from the
 * one that is enforced.
 *
 * The list is deliberately unpaged: a place in an order computed over some of the rows would not
 * be a place in the order at all.
 */
export function useTripInvitations(tripLogId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.tripInvitations(tripLogId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/trip-logs/{tripLogId}/invitations', {
          params: { path: { tripLogId: tripLogId! } },
        }),
      ),
    enabled: !!tripLogId && enabled,
  });
}

function useInvalidateTripInvitations() {
  const queryClient = useQueryClient();
  const invalidateHistory = useInvalidateHistory();
  return (tripLogId: string) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.tripInvitations(tripLogId) });
    // A row here is an audit child of the trip, so writing one moves the trip's own timeline.
    invalidateHistory();
  };
}

/**
 * Puts somebody on the trip's list. The person is named by their entry in the club's directory
 * and never by a bare name — a list of people to be told about a trip that could hold text
 * nobody can resolve would be a list nobody can act on — so an id the directory does not know
 * is refused rather than created.
 */
export function useInviteToTrip() {
  const invalidate = useInvalidateTripInvitations();
  return useMutation({
    mutationFn: ({ tripLogId, caverId }: { tripLogId: string; caverId: string }) =>
      unwrap(
        api.POST('/api/v1/trip-logs/{tripLogId}/invitations', {
          params: { path: { tripLogId } },
          body: { caverId },
        }),
      ),
    onSuccess: (_data, { tripLogId }) => invalidate(tripLogId),
  });
}

/**
 * Records what one person says about coming.
 *
 * The note travels with the answer and is replaced by it: an answer given without words is an
 * answer without words, not an answer still wearing the previous one's. So a cleared note is
 * sent as an explicit absence rather than omitted.
 */
export function useAnswerTripInvitation() {
  const invalidate = useInvalidateTripInvitations();
  return useMutation({
    mutationFn: ({
      tripLogId,
      caverId,
      response,
      note,
    }: {
      tripLogId: string;
      caverId: string;
      response: TripInvitationAnswer;
      note: string | null;
    }) =>
      unwrap(
        api.PUT('/api/v1/trip-logs/{tripLogId}/invitations/{caverId}/response', {
          params: { path: { tripLogId, caverId } },
          body: { response, note },
        }),
      ),
    onSuccess: (_data, { tripLogId }) => invalidate(tripLogId),
  });
}

/** Picks one person out for the trip, or puts them back in the order. The order itself is unchanged. */
export function useSelectForTrip() {
  const invalidate = useInvalidateTripInvitations();
  return useMutation({
    mutationFn: ({
      tripLogId,
      caverId,
      selected,
    }: {
      tripLogId: string;
      caverId: string;
      selected: boolean;
    }) =>
      unwrap(
        api.PUT('/api/v1/trip-logs/{tripLogId}/invitations/{caverId}/selection', {
          params: { path: { tripLogId, caverId } },
          body: { selected },
        }),
      ),
    onSuccess: (_data, { tripLogId }) => invalidate(tripLogId),
  });
}

/**
 * Takes somebody off the list entirely, answer and all. For a person put on it by mistake —
 * recording a "no" in their name instead would be writing down words they never said.
 */
export function useRemoveTripInvitation() {
  const invalidate = useInvalidateTripInvitations();
  return useMutation({
    mutationFn: ({ tripLogId, caverId }: { tripLogId: string; caverId: string }) =>
      unwrapVoid(
        api.DELETE('/api/v1/trip-logs/{tripLogId}/invitations/{caverId}', {
          params: { path: { tripLogId, caverId } },
        }),
      ),
    onSuccess: (_data, { tripLogId }) => invalidate(tripLogId),
  });
}

export type TripPromotion = components['schemas']['TripPromotionDto'];

/**
 * Writes everybody holding a place on the trip into the trip's own list of people.
 *
 * A deliberate act and not a consequence of the trip having happened: who turned up is not who
 * said they would, and a roster nobody wrote is one an audit trail cannot account for.
 *
 * It moves the trip itself — its people change and its version with them — so the whole trip
 * prefix is invalidated rather than the list alone, and the re-read is waited for. Leaving the
 * trip as it was read would let a later save carry the roster somebody saw before this ran, pass
 * its precondition, and quietly undo every row written here; not waiting would leave the same
 * window open for as long as the re-read takes, with a confirmation already on screen.
 */
export function usePromoteTripInvitations() {
  const readBack = useReadTripLogsBack();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ tripLogId }: { tripLogId: string }) =>
      unwrap(
        api.POST('/api/v1/trip-logs/{tripLogId}/invitations/promote', {
          params: { path: { tripLogId } },
        }),
      ),
    onSuccess: () => {
      invalidateHistory();
      // Waited for, unlike the trip's own writes, which hand their version over with the trip:
      // this stamps the trip row and answers a count, so the trip this page holds is behind
      // until it is read back, and a save sent meanwhile would carry the roster from before.
      return readBack();
    },
  });
}

export function useTags(search: string) {
  return useQuery({
    queryKey: queryKeys.tags(search),
    queryFn: () => unwrap(api.GET('/api/v1/tags', { params: { query: { search: search || undefined } } })),
    staleTime: 60_000,
  });
}

/**
 * The tags named by these slugs, which is how a stored tag condition is put back into words. The
 * name listing answers only its first hundred, so a chosen tag is looked up by itself rather than
 * expected among them; a slug missing from the answer is one this reader cannot resolve.
 */
export function useTagsBySlug(slugs: readonly string[]) {
  return useQuery({
    queryKey: queryKeys.tagsBySlug(slugs),
    queryFn: () => unwrap(api.GET('/api/v1/tags', { params: { query: { slug: [...slugs] } } })),
    enabled: slugs.length > 0,
    staleTime: 60_000,
  });
}

/** @param enabled false keeps the request from being made — see {@link useAttachments}. */
export function useTaggings(
  entityType: AttachedEntityType,
  entityId: string | undefined,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.taggings(entityType, entityId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/taggings', { params: { query: { entityType, entityId: entityId! } } })),
    enabled: enabled && !!entityId,
  });
}

function useInvalidateTaggings() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['taggings'] });
    void queryClient.invalidateQueries({ queryKey: ['tags'] });
    // Taggings are audited children of the tagged entity — refresh its timeline too.
    void queryClient.invalidateQueries({ queryKey: ['history'] });
  };
}

export function useCreateTagging() {
  const invalidate = useInvalidateTaggings();
  return useMutation({
    mutationFn: (body: { tagName: string; entityType: AttachedEntityType; entityId: string }) =>
      unwrap(api.POST('/api/v1/taggings', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteTagging() {
  const invalidate = useInvalidateTaggings();
  return useMutation({
    mutationFn: async (id: number) => {
      const { error, response } = await api.DELETE('/api/v1/taggings/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

export type ChecklistInfo = components['schemas']['ChecklistDto'];
export type ChecklistItemInfo = components['schemas']['ChecklistItemDto'];
export type ChecklistWrite = components['schemas']['ChecklistWriteRequest'];
export type TripChecklistInfo = components['schemas']['TripChecklistDto'];
export type TripChecklistItemInfo = components['schemas']['TripChecklistItemDto'];

/**
 * The lists this caller may read, lines included.
 *
 * There is one kind of list. A list an administrator publishes for the whole installation
 * arrives here beside a caver's own — it is the same row with a wider audience — so nothing
 * here sorts them into two groups or asks which is "the default".
 */
export function useChecklists() {
  return useQuery({
    queryKey: queryKeys.checklists,
    queryFn: () => unwrap(api.GET('/api/v1/checklists')),
  });
}

function useInvalidateChecklists() {
  const queryClient = useQueryClient();
  return (id?: string) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.checklists });
    if (id) {
      void queryClient.invalidateQueries({ queryKey: queryKeys.checklist(id) });
    }
    // A trip's reading of how settled it is comes from these rows, so changing a list moves it.
    void queryClient.invalidateQueries({ queryKey: ['trip-logs'] });
  };
}

export function useCreateChecklist() {
  const invalidate = useInvalidateChecklists();
  return useMutation({
    mutationFn: (body: ChecklistWrite) => unwrap(api.POST('/api/v1/checklists', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateChecklist() {
  const invalidate = useInvalidateChecklists();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: ChecklistWrite }) =>
      unwrap(api.PUT('/api/v1/checklists/{id}', { params: { path: { id } }, body })),
    onSuccess: (_data, variables) => invalidate(variables.id),
  });
}

export function useDeleteChecklist() {
  const invalidate = useInvalidateChecklists();
  return useMutation({
    // The refusal travels whole — its code, its sentence and its members — as every other
    // write's does.
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/checklists/{id}', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

/**
 * The list one trip works through, its lines, who has confirmed each and when, and how much of
 * it is settled.
 *
 * The count comes back on the answer rather than being worked out here. It is the server's
 * reading of the lines and the confirmations, and a second count computed in the browser would
 * be a copy free to disagree with it — over a list whose lines this caller may not even have
 * been sent all of.
 *
 * A trip whose purpose names no list, and a trip naming one this caller may not read, answer the
 * same way: no list. That is deliberate on the server, and nothing here tries to tell them apart.
 */
export function useTripChecklist(tripLogId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.tripChecklist(tripLogId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/trip-logs/{tripLogId}/checklist', {
          params: { path: { tripLogId: tripLogId! } },
        }),
      ),
    enabled: !!tripLogId && enabled,
  });
}

function useInvalidateTripChecklist() {
  const queryClient = useQueryClient();
  return (tripLogId: string) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.tripChecklist(tripLogId) });
    // The trip's own row carries the same figure, so a confirmation moves both.
    void queryClient.invalidateQueries({ queryKey: queryKeys.tripLog(tripLogId) });
  };
}

/**
 * Confirms one line as settled for this trip, or takes the confirmation back.
 *
 * Confirming what is already confirmed changes nothing: the record is of who first said so and
 * when, and an answer that moved every time somebody reopened the page would answer a different
 * question.
 */
export function useSetTripChecklistItem() {
  const invalidate = useInvalidateTripChecklist();
  return useMutation({
    mutationFn: async ({
      tripLogId,
      itemId,
      ticked,
    }: {
      tripLogId: string;
      itemId: string;
      ticked: boolean;
    }) => {
      const params = { path: { tripLogId, itemId } };
      if (!ticked) {
        await unwrapVoid(
          api.DELETE('/api/v1/trip-logs/{tripLogId}/checklist/items/{itemId}', { params }),
        );
        return;
      }

      await unwrap(api.PUT('/api/v1/trip-logs/{tripLogId}/checklist/items/{itemId}', { params }));
    },
    onSuccess: (_data, variables) => invalidate(variables.tripLogId),
  });
}
export type TripPositionEventKind = components['schemas']['TripPositionEventKind'];

/**
 * Where the trips in a window of days went, for one rectangle of the world.
 *
 * **Two answers, correlated here rather than joined on the server, and that is a decision.** The
 * calendar answers which dated records fall in a window and carries no position of any kind; this
 * answers which trip shapes fall inside a rectangle over the same days. Asking one endpoint for
 * both would put a third expression of who may read what beside the two that already exist — the
 * calendar's merge and this map's own filter — and the two would then have to be kept saying the
 * same thing forever. They already agree by construction: both walk the same visibility rule over
 * the same table, so nothing can arrive on one side that the other would have refused. Matching
 * them on the identifier each carries is therefore exact, and costs one request that was going to
 * be made anyway.
 *
 * The rectangle is the caller's own view. There is deliberately no way to ask for the whole world:
 * the answer is capped, and a capped answer over the world is a scatter of whichever rows sorted
 * first, drawn as though it were everything in view.
 */
export function useTripLogMap(
  bbox: string | undefined,
  from: string,
  to: string,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.tripLogMap(bbox ?? '', from, to),
    queryFn: () =>
      unwrap(api.GET('/api/v1/map/trip-logs', { params: { query: { bbox: bbox!, from, to } } })),
    enabled: enabled && !!bbox,
    retry: false,
    // Panning keeps the shapes already drawn on screen while the next rectangle is answered,
    // rather than blanking the map under whoever is moving it.
    placeholderData: keepPreviousData,
  });
}

/**
 * The working areas of the camps in a window that fall inside a rectangle — the shape drawn on
 * each camp's plan and nothing a camp's trips or caves would add, read through the caller's own
 * visibility. Asked beside the trip-log read by the calendar's map, under the same rectangle.
 */
export function useCampAreasMap(
  bbox: string | undefined,
  from: string,
  to: string,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.campAreasMap(bbox ?? '', from, to),
    queryFn: () =>
      unwrap(api.GET('/api/v1/map/expeditions', { params: { query: { bbox: bbox!, from, to } } })),
    enabled: enabled && !!bbox,
    retry: false,
    placeholderData: keepPreviousData,
  });
}
