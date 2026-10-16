// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback } from 'react';
import {
  keepPreviousData,
  queryOptions,
  useMutation,
  useQueries,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import type { QueryClient } from '@tanstack/react-query';
import { api, isSettledRefusal, lastReadETag } from '../client.ts';
import type { components } from '../schema';
import { unwrap, unwrapVoid } from './shared.ts';
import type {
  PublishedLinksParams,
  ResLink,
  ResLinkPageParams,
  TrackingEventListParams,
} from './types.ts';
import { queryKeys } from './queryKeys.ts';
import type { TripPositionEventKind } from './trips.ts';
import { RESLINK_PAGE_MAX, useResLinksForTarget } from './resLinks.ts';

export type TripTrackingState = components['schemas']['TripTrackingState'];
export type TrackingState = components['schemas']['TrackingStateDto'];
export type TrackingParticipant = components['schemas']['TrackingParticipantDto'];
export type TrackingTeam = components['schemas']['TrackingTeamDto'];
export type TrackingPlace = components['schemas']['TrackingPlaceDto'];
export type TrackingCsvOptions = components['schemas']['TrackingCsvImportOptionsDto'];
export type TrackingCsvPreview = components['schemas']['TrackingCsvPreviewDto'];
export type TrackingCsvPreviewRow = components['schemas']['TrackingCsvPreviewRowDto'];
export type TrackingCsvDiagnostic = components['schemas']['TrackingCsvDiagnosticDto'];
export type TrackingRosterTimes = components['schemas']['TrackingRosterTimesDto'];
export type TrackingRosterTimesPerson = components['schemas']['TrackingRosterTimesPersonDto'];
export type TrackingRosterTimesProblem = NonNullable<TrackingRosterTimesPerson['problem']>;
export type TrackingRosterTimesTake = components['schemas']['TrackingRosterTimesTakeDto'];
export type TrackingEvent = components['schemas']['TrackingEventDto'];
export type TrackingRemovedEvent = components['schemas']['TrackingRemovedEventDto'];
export type TrackedTrip = components['schemas']['TrackedTripDto'];
export type TrackingEventWrite = components['schemas']['TrackingEventRequest'];
export type TrackingConfigWrite = components['schemas']['TrackingConfigRequest'];
export type TrackingDepthCandidate = components['schemas']['TrackingDepthCandidateDto'];
export type TrackingPictureInput = components['schemas']['TrackingPictureInput'];
export type TrackingPictureResult = components['schemas']['TrackingPictureResultDto'];

/**
 * What a report can say, in the order somebody underground would say it: in, then where, then
 * anything else, then out. The order is this application's; the set is the server's.
 */
export const TRACKING_EVENT_KINDS: readonly TripPositionEventKind[] = [
  'entered',
  'atStation',
  'atDepth',
  'note',
  'exited',
];

/** How often a live watch is re-read while a party is underground. */
const TRACKING_POLL_MS = 30_000;

/**
 * The one condition every tracking read is kept fresh on.
 *
 * Shared rather than repeated because the folded state and the log are two views of the same
 * arriving reports, drawn one above the other. Polling one and not the other puts two tables on
 * one screen disagreeing about the same events — the upper one advancing as word comes in, the
 * lower one stopped at whatever it held when the tab was opened — and the log is also the only
 * surface a correction acts on, so it would be a list somebody deletes from while it no longer
 * matches the server.
 */
function trackingPollInterval(state: TripTrackingState | undefined) {
  return state === 'armed' ? TRACKING_POLL_MS : (false as const);
}

/**
 * How this trip is being watched: whether tracking is armed, what it resolves positions against,
 * its teams, and where each person on the roster was last reported.
 *
 * Two things on the answer are the server's conclusions and must be drawn as they arrive. Each
 * participant's *position* is their latest report that claimed a place, while their *last kind* is
 * their latest report of any kind — a note or an exit says something happened, not where, so the
 * two deliberately disagree. And `positionsWithheld` says at least one position was kept from this
 * reader: the position fields then arrive null, exactly as they do for somebody nobody has
 * reported yet, and telling those two apart is not something a client may attempt.
 *
 * Kept fresh while a party is underground. The rows change because somebody radios a position in,
 * not because this browser did anything, so a panel that only re-read on a write would show a
 * watch that has stopped moving — which on this surface reads as a party that has stopped moving.
 * Nothing is asked for once tracking is closed or was never armed.
 */
export function useTripTracking(tripLogId: string | undefined, enabled = true) {
  return useQuery({ ...tripTrackingQuery(tripLogId ?? ''), enabled: !!tripLogId && enabled });
}

/** The tracking-state read of one trip: its key, its fetch and its polling. */
function tripTrackingQuery(tripLogId: string) {
  return queryOptions({
    queryKey: queryKeys.tripTracking(tripLogId),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/trip-logs/{tripLogId}/tracking', {
          params: { path: { tripLogId } },
        }),
      ),
    refetchInterval: (query) => trackingPollInterval(query.state.data?.state),
  });
}

export type ExpeditionSurfaceLog = components['schemas']['ExpeditionSurfaceLogDto'];
export type ExpeditionSurfaceLogTrip = components['schemas']['ExpeditionSurfaceLogTripDto'];
export type ExpeditionSurfaceLogPerson = components['schemas']['ExpeditionSurfaceLogPersonDto'];

/**
 * How often a camp's head count is asked for again, or `false` for not at all.
 *
 * <b>One condition: somebody is looking.</b> The camp page keeps a section mounted once it has been
 * opened, so a count that polled whenever it was mounted would go on asking every half minute from
 * behind the map for as long as the camp stayed open. That is the only reason to stop.
 *
 * <b>What the last answer held is deliberately not one.</b> A single trip's watch can be left alone
 * once it is closed, because only a report moves it and a closed watch takes none. This answer is
 * a list, and the list changes from outside it: a party arms its watch from a phone at the cave,
 * a watch that was closed is armed again because somebody went back in, a finished trip passes out
 * of the window it is kept for. A count that stopped asking on an empty list would go on showing a
 * coordinator "nobody is being followed" over a camp with a party underground, for as long as the
 * tab stayed in front — and nothing on the screen would say it had stopped. The same holds for a
 * read that failed: it is asked again at the next interval rather than left as the error it was.
 *
 * The interval is the trip's own, on purpose: a camp's count that ran behind the trip it links to
 * would show a party underground on one screen and out on the next.
 */
export function expeditionSurfaceLogPollInterval(shown: boolean) {
  return shown ? TRACKING_POLL_MS : (false as const);
}

/**
 * Who is underground on one camp's trips: every member trip this caller may read whose watch is
 * running or was closed recently, each with its party counted and named.
 *
 * <b>No place is on this answer, and none may be added to it here.</b> It carries who is in, who is
 * out, who has not been heard from and when each was last heard — not a station, a depth, a survey
 * or a cave — which is what lets it count a party in a cave whose position this reader may not be
 * told. Anything a caller wants to know about *where* is the trip's own watch, behind the trip's
 * own rules.
 *
 * Read only while its section is the one on screen, and read afresh each time it becomes so: a
 * head count somebody comes back to after an hour on another tab must not be the hour-old one
 * with nothing to say it is. And kept fresh for as long as it stays on screen, by the rule above.
 */
export function useExpeditionSurfaceLog(expeditionId: string | undefined, shown = true) {
  return useQuery({
    queryKey: queryKeys.expeditionSurfaceLog(expeditionId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/expeditions/{id}/surface-log', {
          params: { path: { id: expeditionId! } },
        }),
      ),
    enabled: !!expeditionId && shown,
    staleTime: 0,
    retry: false,
    refetchInterval: () => expeditionSurfaceLogPollInterval(shown),
  });
}

/**
 * The trip's reports, newest first.
 *
 * One page of them, optionally one person's: a wrong report is corrected in place or deleted
 * from the row it is read on, so being able to reach every page is what makes every report
 * correctable. Position fields are withheld here under exactly the same rule as on the state read.
 *
 * Kept fresh on the same condition as the folded state, and deliberately from the same cache entry
 * rather than from a flag this caller passes: two coordinators with the tab open is the designed
 * case, and one of them recording what the radio said has to reach the other's log as well as
 * their table. Reading the watch out of the cache is what makes the two impossible to drift apart
 * — nothing else on this screen knows whether a watch is armed.
 */
export function useTripTrackingEvents(
  tripLogId: string | undefined,
  params: TrackingEventListParams = {},
  enabled = true,
) {
  const queryClient = useQueryClient();
  return useQuery({
    queryKey: queryKeys.tripTrackingEvents(tripLogId ?? '', params),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/trip-logs/{tripLogId}/tracking/events', {
          params: { path: { tripLogId: tripLogId! }, query: params },
        }),
      ),
    enabled: !!tripLogId && enabled,
    refetchInterval: () =>
      trackingPollInterval(
        queryClient.getQueryData<TrackingState>(queryKeys.tripTracking(tripLogId ?? ''))?.state,
      ),
    // Paging keeps the rows on screen while the next answer arrives, rather than emptying the
    // list under whoever is reading it.
    placeholderData: keepPreviousData,
  });
}

/** Asked for in few large pages: this is one read of a whole log, not a list somebody scrolls. */
const TRACKING_LOG_PAGE_SIZE = 200;

/**
 * How many pages are followed before the read is called off. A trip's log runs to tens of reports
 * and a long expedition day to hundreds; ten thousand is far past anything a party can radio out,
 * and a log that somehow exceeds it is refused rather than truncated.
 */
const TRACKING_LOG_MAX_PAGES = 50;

/**
 * One trip's whole log, newest first — every page of it, followed until there are no more.
 *
 * <b>Partial is worse than absent here.</b> This exists for the replay, which reconstructs where
 * everybody was at a moment by reading the reports up to it; the pages are ordered newest first, so
 * a first page alone is the *recent* reports, and a replay built on one would show a party
 * materialising out of nowhere part-way through the trip and would say so with a moving marker. So
 * the pages are followed to the end, and a read that cannot reach the end fails loudly instead of
 * answering with what it got.
 *
 * Deliberately not polled. The surfaces that have to keep up with the radio — the folded watch and
 * the recent reports — are polled where they are declared; this is a read of history, taken when
 * somebody asks to replay it. A report recorded or deleted in this browser still refreshes it: it
 * is held under the same key prefix everything about a trip's reports is invalidated by.
 */
export function useTripTrackingEventLog(tripLogId: string | undefined, enabled = true) {
  return useQuery({
    ...tripTrackingEventLogQuery(tripLogId ?? ''),
    enabled: !!tripLogId && enabled,
  });
}

/** How one trip's whole log is read and under which key it is held. */
function tripTrackingEventLogQuery(tripLogId: string) {
  return queryOptions({
    queryKey: queryKeys.tripTrackingEventLog(tripLogId),
    queryFn: async () => {
      const items: TrackingEvent[] = [];
      for (let page = 1; page <= TRACKING_LOG_MAX_PAGES; page++) {
        const answer = await unwrap(
          api.GET('/api/v1/trip-logs/{tripLogId}/tracking/events', {
            params: {
              path: { tripLogId },
              query: { page, pageSize: TRACKING_LOG_PAGE_SIZE },
            },
          }),
        );
        items.push(...answer.items);
        // An empty page ends the log whatever the count says: a total that disagrees with the pages
        // would otherwise spin this until the page limit stopped it.
        if (answer.items.length === 0 || items.length >= answer.totalItems) {
          return items;
        }
      }
      throw new Error('the tracking log has more pages than one read follows');
    },
  });
}

/**
 * The trips tracked on one survey model that this reader may read: those whose watch points at it
 * now, and those with reports recorded against it — a watch re-pointed at a newer survey keeps its
 * earlier reports on the older one. Latest activity first.
 *
 * The report counts and first/last instants are over the reports whose place this reader may see,
 * under the same withholding the trip's own log applies; a trip whose only tie to the model is
 * withheld is not listed. A model this reader may not see answers not-found, as the model's own
 * read does.
 */
export function useSurveyModelTrackedTrips(surveyModelId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.surveyModelTrackedTrips(surveyModelId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/survey-models/{surveyModelId}/tracked-trips', {
          params: { path: { surveyModelId: surveyModelId! } },
        }),
      ),
    enabled: !!surveyModelId && enabled,
  });
}

export type TrackedTripReplay = components['schemas']['TrackedTripReplayDto'];

/** How many trips one replay read may name; a longer list is asked for in several reads. */
const TRACKED_TRIP_REPLAY_MAX_TRIPS = 50;

/**
 * The one definition of the several-trip replay read — its key and its fetch — shared by the hook
 * that holds it and the callback that reads it again.
 *
 * Held under the tracked-trips list of the model, so everything that already says "what was tracked
 * on this model may have changed" — a report recorded, corrected or removed in this browser, a
 * watch re-pointed — reaches it too.
 */
function trackedTripReplaysQuery(surveyModelId: string, tripLogIds: readonly string[]) {
  return queryOptions({
    queryKey: [...queryKeys.surveyModelTrackedTrips(surveyModelId), 'replay', [...tripLogIds]] as const,
    queryFn: async () => {
      const trips: TrackedTripReplay[] = [];
      for (let from = 0; from < tripLogIds.length; from += TRACKED_TRIP_REPLAY_MAX_TRIPS) {
        trips.push(
          ...(await unwrap(
            api.GET('/api/v1/survey-models/{surveyModelId}/tracked-trips/replay', {
              params: {
                path: { surveyModelId },
                query: { tripLogIds: tripLogIds.slice(from, from + TRACKED_TRIP_REPLAY_MAX_TRIPS) },
              },
            }),
          )),
        );
      }
      return trips;
    },
    // Never served from an earlier visit without asking: a trip under way has moved on since.
    staleTime: 0,
  });
}

/**
 * Several trips as a replay of them together needs each: its title, its roster's names, its
 * tracking state and its whole log, newest first — in one read, in the order asked.
 *
 * Each trip is answered exactly as its own tracking reads answer this reader, positions withheld
 * as they withhold them. <b>A trip this reader may not read is simply not in the answer</b>, and a
 * trip whose log is longer than one answer carries arrives with `eventsComplete` false and only the
 * newest part of it: neither is a trip to replay.
 *
 * Kept fresh while any of the trips is under way, on the condition every tracking read is kept
 * fresh on, unless `polled` is false. While a different set of trips is being read the previous
 * answer stays in hand as a placeholder, so ticking one more trip does not empty the screen.
 */
export function useTrackedTripReplays(surveyModelId: string, tripLogIds: readonly string[], polled = true) {
  return useQuery({
    ...trackedTripReplaysQuery(surveyModelId, tripLogIds),
    enabled: surveyModelId !== '' && tripLogIds.length > 0,
    placeholderData: keepPreviousData,
    refetchInterval: (query) =>
      polled && (query.state.data ?? []).some((trip) => trackingPollInterval(trip.tracking.state) !== false)
        ? TRACKING_POLL_MS
        : false,
  });
}

/**
 * Reads several trips' replays again, now, and answers with them — whatever is already held.
 *
 * For a surface about to commit to what the logs say of trips still under way — a file made from
 * them, which outlives the screen. The answer replaces what is held, so everything else drawn
 * from it follows. A read already on its way is waited for rather than started again.
 */
export function useRereadTrackedTripReplays() {
  const queryClient = useQueryClient();
  return useCallback(
    (surveyModelId: string, tripLogIds: readonly string[]) =>
      queryClient.fetchQuery({ ...trackedTripReplaysQuery(surveyModelId, tripLogIds), staleTime: 0 }),
    [queryClient],
  );
}

/**
 * Every held answer about one trip's tracking, whatever narrowing it was asked under.
 *
 * A write to a report changes the folded state as well as the log — recording a position moves
 * where somebody is — so the two are always invalidated together, and the event list is reached
 * by its prefix because it is held once per narrowing.
 */
function invalidateTripTracking(queryClient: QueryClient, tripLogId: string) {
  void queryClient.invalidateQueries({ queryKey: queryKeys.tripTracking(tripLogId) });
  void queryClient.invalidateQueries({ queryKey: ['trip-logs', 'tracking-events', tripLogId] });
  // A report or a re-pointed watch changes which trips a model lists and what each counts.
  void queryClient.invalidateQueries({ queryKey: ['trip-logs', 'tracked-on-model'] });
}

function useInvalidateTripTracking() {
  const queryClient = useQueryClient();
  return (tripLogId: string) => invalidateTripTracking(queryClient, tripLogId);
}

/**
 * Arms, closes or reconfigures the watch.
 *
 * **Merge semantics, and they matter.** A field left out keeps what is stored, so closing a watch
 * cannot silently rewrite the configuration the reports already on the log were resolved under.
 * Clearing is therefore explicit and is not the same as leaving something out: an empty string
 * clears the reference station, an empty list clears the depth filter. The state is always stated.
 *
 * The write is checked against the trip's own version, which the tracking read is what emits — two
 * coordinators can both have this panel open, and arming against a configuration somebody else has
 * already changed is exactly the lost update the precondition exists to catch. The version is
 * looked for under the tracking path first and the trip's own path second, so a panel opened
 * without the trip page ever having been read still carries one; a write with no version at all is
 * refused by the server rather than quietly overwriting.
 */
export function useSetTripTracking() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      tripLogId,
      state,
      surveyModelId = null,
      referenceStationName = null,
      depthFilter = null,
    }: {
      tripLogId: string;
      state: TripTrackingState;
      /** Null keeps the stored model; a model id changes it and resets the reference and filter. */
      surveyModelId?: string | null;
      /** Null keeps the stored reference station; an empty string clears it. */
      referenceStationName?: string | null;
      /** Null keeps the stored filter; an empty list clears it. */
      depthFilter?: string[] | null;
    }) => {
      const etag =
        lastReadETag(`/api/v1/trip-logs/${tripLogId}/tracking`) ??
        lastReadETag(`/api/v1/trip-logs/${tripLogId}`);
      return unwrap(
        api.PUT('/api/v1/trip-logs/{tripLogId}/tracking', {
          params: { path: { tripLogId } },
          headers: etag ? { 'If-Match': etag } : undefined,
          body: { state, surveyModelId, referenceStationName, depthFilter },
        }),
      );
    },
    onSuccess: (_data, variables) => {
      void queryClient.invalidateQueries({
        queryKey: ['trip-logs', 'tracking-events', variables.tripLogId],
      });
      // And every held answer about what a depth means in this cave, because this write is the one
      // act that changes it. The model, the datum and the filter are all settled here, and each of
      // them moves every station's depth at once: an answer worked out under the old configuration
      // and still drawn under the new one is a distance measured somewhere else, on the surface
      // that says where a party is. Reached by prefix so every depth anybody has asked about goes,
      // not merely the one in front of them.
      void queryClient.invalidateQueries({
        queryKey: ['trip-logs', 'tracking-depth', variables.tripLogId],
      });
      // And the places the cave has declared, held under this trip. They are checked against the
      // watch's model, and a watch that names no model answers that question with a refusal — a
      // settled one, held like any answer. Arming with a model is the act that changes the answer,
      // so the held refusal goes with the depths: otherwise the report card, which stays mounted
      // across the arming, goes on drawing no chooser until something else happens to ask again.
      void queryClient.invalidateQueries({
        queryKey: queryKeys.tripTrackingPlaces(variables.tripLogId),
      });
      // Handed back rather than started and forgotten, as on the trip's own writes: the next
      // configuration change is checked against the version this one produced, and only a read
      // records a version. Without the wait, a second save a moment later is refused as a
      // conflict the person saving has no way to understand.
      return queryClient.invalidateQueries({
        queryKey: queryKeys.tripTracking(variables.tripLogId),
      });
    },
  });
}

/** Adds a titled team to the trip. Teams label reports; they are never a second roster. */
export function useCreateTrackingTeam() {
  const invalidate = useInvalidateTripTracking();
  return useMutation({
    mutationFn: ({ tripLogId, title }: { tripLogId: string; title: string }) =>
      unwrap(
        api.POST('/api/v1/trip-logs/{tripLogId}/tracking/teams', {
          params: { path: { tripLogId } },
          body: { title },
        }),
      ),
    onSuccess: (_data, variables) => invalidate(variables.tripLogId),
  });
}

export function useRenameTrackingTeam() {
  const invalidate = useInvalidateTripTracking();
  return useMutation({
    mutationFn: ({
      tripLogId,
      teamId,
      title,
    }: {
      tripLogId: string;
      teamId: string;
      title: string;
    }) =>
      unwrap(
        api.PUT('/api/v1/trip-logs/{tripLogId}/tracking/teams/{teamId}', {
          params: { path: { tripLogId, teamId } },
          body: { title },
        }),
      ),
    onSuccess: (_data, variables) => invalidate(variables.tripLogId),
  });
}

/** Removes a team. The reports it labelled keep their caver and lose only the label. */
export function useDeleteTrackingTeam() {
  const invalidate = useInvalidateTripTracking();
  return useMutation({
    mutationFn: ({ tripLogId, teamId }: { tripLogId: string; teamId: string }) =>
      unwrapVoid(
        api.DELETE('/api/v1/trip-logs/{tripLogId}/tracking/teams/{teamId}', {
          params: { path: { tripLogId, teamId } },
        }),
      ),
    onSuccess: (_data, variables) => invalidate(variables.tripLogId),
  });
}

/**
 * What the trip's published page calls one member of the party, or nothing in particular.
 *
 * <b>This is one person's only way off a public page, and it is the reason the route exists.</b>
 * Where an installation publishes real names, everybody on a followed trip is named by the name the
 * roster holds for them — so a caver who does not want their name in front of the internet has
 * exactly one thing that can be done about it, and this is it. A caption set here outranks the
 * installation's setting **in both directions**: it names somebody on a page that would otherwise
 * number them, and it keeps somebody off a page that would otherwise name them. It has to outrank
 * it, because a setting somewhere else being flipped must not undo a person's request.
 *
 * <b>Empty clears, and there is no second route that clears.</b> "Call them nothing in particular"
 * is a value this field holds rather than an absence to be repaired, so an empty or blank caption
 * is sent as the write it is; the server drops the row and the page goes back to whatever the
 * installation's setting says. A caller that tried to clear by *not* sending the field would be
 * asking for the one thing this route does not offer.
 *
 * Only somebody the trip's roster names can be captioned — a caption for anybody else would be a
 * person the published page invents — and the server refuses the rest.
 */
export function useSetTrackingParticipantLabel() {
  const invalidate = useInvalidateTripTracking();
  return useMutation({
    mutationFn: ({
      tripLogId,
      caverId,
      label,
    }: {
      tripLogId: string;
      caverId: string;
      /** The caption to show, or null/empty to take the caption off again. */
      label: string | null;
    }) =>
      unwrap(
        api.PUT('/api/v1/trip-logs/{tripLogId}/tracking/participants/{caverId}', {
          params: { path: { tripLogId, caverId } },
          body: { label },
        }),
      ),
    // The caption rides on the watch's own read, beside the person it belongs to, so the surface
    // that shows what the published page will call somebody is refreshed by the same invalidation
    // every other tracking write uses.
    onSuccess: (_data, variables) => invalidate(variables.tripLogId),
  });
}

/** One report as it is asked of the server, whoever is asking and for whichever time. */
export interface TrackingReportRequest {
  tripLogId: string;
  caverIds: string[];
  kind: TripPositionEventKind;
  stationName?: string | null;
  /**
   * The far end of a stretch, on a station report that says somebody was between two stations of
   * the survey; null or absent on every other report.
   */
  toStationName?: string | null;
  depthM?: number | null;
  teamId?: string | null;
  note?: string | null;
  /** What the people were doing, in the party's own words; null or absent says nothing. */
  activity?: string | null;
  /** Null means now, on the server's clock — the ordinary case of a report made as it happens. */
  recordedAt?: string | null;
  /**
   * The key of this act of reporting, minted once by whoever may have to send it again. A send
   * repeated under the same key is answered with what the first one wrote and writes nothing.
   * Null is a send that is an act of its own and can never be recognised as a repeat.
   */
  clientKey?: string | null;
}

/** The one request a report makes: its first send and every later one are this same call. */
function postTrackingReport({
  tripLogId,
  caverIds,
  kind,
  stationName = null,
  toStationName = null,
  depthM = null,
  teamId = null,
  note = null,
  activity = null,
  recordedAt = null,
  clientKey = null,
}: TrackingReportRequest) {
  return unwrap(
    api.POST('/api/v1/trip-logs/{tripLogId}/tracking/events', {
      params: { path: { tripLogId } },
      // The phrase goes only where there is one: a report that says none is the request it has
      // always been, and the server reads its absence as none.
      body: {
        caverIds, kind, stationName, toStationName, depthM, teamId, note, recordedAt, clientKey,
        ...(activity ? { activity } : {}),
      },
    }),
  );
}

/**
 * Sends again a report this browser kept because its first send was never answered.
 *
 * <b>A plain request and not a mutation, on purpose.</b> A mutation belongs to the surface that
 * made it: its pending state turns that surface's button, and its failure is announced by whoever
 * awaited it. A kept report is sent from the signed-in shell, with no surface of its own — possibly
 * while its trip is not on screen at all — and what becomes of it is decided by the queue it came
 * from, which must see the failure itself: whether the server answered, with what status, and what
 * wait it named. So the answer and the error are handed back exactly as the transport produced
 * them, and the only thing done here is what any write to a log owes the screen: the trip's
 * tracking reads are asked again once the report is on it.
 */
export function useSendHeldTrackingReport() {
  const queryClient = useQueryClient();
  return useCallback(
    async (report: TrackingReportRequest) => {
      const written = await postTrackingReport(report);
      invalidateTripTracking(queryClient, report.tripLogId);
      return written;
    },
    [queryClient],
  );
}

/**
 * Records one report about one or many cavers at once.
 *
 * One request, whatever the number of people it is about: a party that came to a station together
 * was at that station at one moment, and a report per person would put the same moment on the log
 * several times with nothing tying the rows together. The answer is the rows that were created,
 * one per caver.
 *
 * Reports land on a watch that has been started — armed, or closed since — and on no other; that
 * is the server's rule, and the form is gated on the same state rather than trusting the refusal to
 * be readable.
 */
export function useRecordTrackingEvents() {
  const invalidate = useInvalidateTripTracking();
  return useMutation({
    mutationFn: postTrackingReport,
    // Asked whatever the browser believes about its connection. Left to the default, a write made
    // while the browser knows it is offline is neither sent nor failed: it waits, unseen, for the
    // connection, and a reload in the meantime loses it — and when it does go, a report made "now"
    // is stamped with the minute the signal came back. Sent regardless, it fails at once with no
    // answer, and its caller keeps it under its key with the moment it was about.
    networkMode: 'always',
    onSuccess: (_data, variables) => invalidate(variables.tripLogId),
  });
}

/**
 * Corrects one report already on the log, keeping the row it is written on.
 *
 * <b>It names no caver, and that is not an omission.</b> A report about a different person is a
 * different report: what somebody means by changing its subject is that this one should not exist
 * and another should, which is a deletion and a new report. What this changes is everything about
 * one person's report that a relayed phone call can be written down wrongly — the hour, the place,
 * the party they were with, the words.
 *
 * Every field is sent on every correction, including the ones that are not changing, because the
 * server measures the whole report again: a kind without the place that belongs to it is refused,
 * so a partial write would be a request that cannot be validated as a report at all.
 */
export function useUpdateTrackingEvent() {
  const invalidate = useInvalidateTripTracking();
  return useMutation({
    mutationFn: ({
      tripLogId,
      eventId,
      kind,
      stationName = null,
      toStationName = null,
      depthM = null,
      teamId = null,
      note = null,
      activity = null,
      recordedAt = null,
    }: {
      tripLogId: string;
      eventId: string;
      kind: TripPositionEventKind;
      stationName?: string | null;
      /** The far end of a stretch; null says the report is at one station, or names none. */
      toStationName?: string | null;
      depthM?: number | null;
      teamId?: string | null;
      note?: string | null;
      /**
       * What the person was doing. A correction says the whole report again, so leaving this out
       * leaves the report with none: whoever corrects a report sends back what it showed them.
       */
      activity?: string | null;
      /** Null leaves the moment the report already claims, which is the ordinary case. */
      recordedAt?: string | null;
    }) =>
      unwrap(
        api.PUT('/api/v1/trip-logs/{tripLogId}/tracking/events/{eventId}', {
          params: { path: { tripLogId, eventId } },
          body: { kind, stationName, toStationName, depthM, teamId, note, recordedAt, ...(activity ? { activity } : {}) },
        }),
      ),
    onSuccess: (_data, variables) => invalidate(variables.tripLogId),
  });
}

/**
 * The places the watch's cave has declared: a depth, the station it means, and its name.
 *
 * <b>Asked of the trip, not of the cave.</b> The surface that needs this list is the report card,
 * which knows which watch it is drawn on and is deliberately not told which cave the watch is
 * anchored to — so the question is asked where the answer is already guarded, and a caller who may
 * not place the cave is refused rather than handed an empty list they would read as "no places
 * declared".
 *
 * Shallowest first, decided by the server, so every surface showing the list agrees about the
 * order — and because that is the order a party passes the places in.
 */
export function useTrackingPlaces(tripLogId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.tripTrackingPlaces(tripLogId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/trip-logs/{tripLogId}/tracking/places', {
          params: { path: { tripLogId: tripLogId! } },
        }),
      ),
    enabled: !!tripLogId && enabled,
    // A watch with no usable model answers a conflict rather than a list, which is a settled
    // answer and not a failure worth retrying: the model does not appear between two attempts.
    retry: false,
  });
}

/**
 * What reading a sheet of tracking reports onto this trip would do — nothing is written.
 *
 * A mutation rather than a query although it changes nothing, because the sheet is the question:
 * it arrives in the body, it changes every time somebody edits the column mapping, and caching an
 * answer under it would mean keying a cache on a spreadsheet.
 */
export function useTrackingCsvPreview() {
  return useMutation({
    mutationFn: ({
      tripLogId,
      text,
      options = null,
    }: {
      tripLogId: string;
      text: string;
      options?: TrackingCsvOptions | null;
    }) =>
      unwrap(
        api.POST('/api/v1/trip-logs/{tripLogId}/tracking/csv-import/preview', {
          params: { path: { tripLogId } },
          body: { text, options },
        }),
      ),
  });
}

/**
 * Records the sheet's rows as reports.
 *
 * <b>Overwriting is asked for rather than assumed.</b> The key is the person and the instant, so
 * re-importing a corrected sheet is the intended use — and it is also the one act that silently
 * rewrites history, which is why the reviewer says so each time rather than a default saying it
 * for them.
 */
export function useTrackingCsvCommit() {
  const invalidate = useInvalidateTripTracking();
  return useMutation({
    mutationFn: ({
      tripLogId,
      text,
      options = null,
      replaceExisting = false,
      lines = null,
      planDigest = null,
    }: {
      tripLogId: string;
      text: string;
      options?: TrackingCsvOptions | null;
      replaceExisting?: boolean;
      /** The physical lines to commit, or null for every importable one. */
      lines?: number[] | null;
      /**
       * The name the preview gave for what importing would write. The server reads the sheet again
       * for the write and refuses (`tracking_csv.plan_changed`) when that reading would no longer
       * write what the preview named; left out, the sheet is committed as it reads by then.
       */
      planDigest?: string | null;
    }) =>
      unwrap(
        api.POST('/api/v1/trip-logs/{tripLogId}/tracking/csv-import/commit', {
          params: { path: { tripLogId } },
          body: { text, options, replaceExisting, lines, planDigest },
        }),
      ),
    onSuccess: (_data, variables) => invalidate(variables.tripLogId),
  });
}

/**
 * The column roles a tracking sheet can carry, and the header spellings each is detected under.
 *
 * Read from the server rather than written down here, so a mapping screen offers exactly what the
 * reader recognises. Two lists that drift apart is a screen offering a column the parser ignores.
 */
export function useTrackingCsvFields() {
  return useQuery({
    queryKey: ['tracking-csv-import', 'fields'],
    queryFn: () => unwrap(api.GET('/api/v1/tracking-csv-import/fields', {})),
    staleTime: Infinity,
  });
}

/**
 * Takes a report off the log, for when what it recorded never happened.
 *
 * Distinct from correcting one, which keeps the row: this is for a report that should not be there
 * at all — somebody else's name typed, a call that turned out to be about another trip. A report
 * that happened differently is corrected rather than removed and re-entered, so that anything
 * hanging off it survives the fix.
 *
 * The report is kept, out of every read but the list of removed ones, and can be put back as it
 * was; destroying it is a second act with a hook of its own.
 */
export function useDeleteTrackingEvent() {
  const invalidate = useInvalidateTripTracking();
  return useMutation({
    mutationFn: ({ tripLogId, eventId }: { tripLogId: string; eventId: string }) =>
      unwrapVoid(
        api.DELETE('/api/v1/trip-logs/{tripLogId}/tracking/events/{eventId}', {
          params: { path: { tripLogId, eventId } },
        }),
      ),
    onSuccess: (_data, variables) => invalidate(variables.tripLogId),
  });
}

/** How many removed reports one page of their list holds. */
export const TRACKING_REMOVED_PAGE_SIZE = 20;

/**
 * The reports taken off one trip's log, the latest removal first, a page at a time.
 *
 * Answered only to those who may write the log — anybody else is refused, so the caller asks only
 * where the log's own controls are offered. The position fields of each report follow the same
 * per-row withholding as the log: a place this reader may not be told is not told here either.
 *
 * Not polled. A removed report is nowhere on the watch, so nothing that has to keep up with the
 * radio is drawn from this; it is read again when a report is taken off, put back or destroyed in
 * this browser, and when somebody opens the list.
 */
export function useTripTrackingRemovedEvents(
  tripLogId: string | undefined,
  page = 1,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.tripTrackingRemovedEvents(tripLogId ?? '', page),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/trip-logs/{tripLogId}/tracking/events/removed', {
          params: {
            path: { tripLogId: tripLogId! },
            query: { page, pageSize: TRACKING_REMOVED_PAGE_SIZE },
          },
        }),
      ),
    enabled: !!tripLogId && enabled,
    // The rows stay on screen while the next page arrives, as on the log itself.
    placeholderData: keepPreviousData,
  });
}

/**
 * Puts a report taken off the log back on it: the same report under the same id, saying what it
 * said — its place, its moment, who recorded it and everything pinned to it.
 *
 * Asked of a report that is on the log already, it answers with that report and changes nothing,
 * so an undo pressed twice, or pressed after somebody else put the report back, is not an error.
 */
export function useRestoreTrackingEvent() {
  const invalidate = useInvalidateTripTracking();
  return useMutation({
    mutationFn: ({ tripLogId, eventId }: { tripLogId: string; eventId: string }) =>
      unwrap(
        api.POST('/api/v1/trip-logs/{tripLogId}/tracking/events/{eventId}/restore', {
          params: { path: { tripLogId, eventId } },
        }),
      ),
    onSuccess: (_data, variables) => invalidate(variables.tripLogId),
  });
}

/**
 * Destroys a report already taken off the log. Nothing brings it back afterwards.
 *
 * Only a removed report accepts this: one still on the log is refused, so the loss of what may be
 * the only record of where somebody was always stands behind two deliberate acts.
 */
export function useDestroyTrackingEvent() {
  const invalidate = useInvalidateTripTracking();
  return useMutation({
    mutationFn: ({ tripLogId, eventId }: { tripLogId: string; eventId: string }) =>
      unwrapVoid(
        api.DELETE('/api/v1/trip-logs/{tripLogId}/tracking/events/{eventId}', {
          params: { path: { tripLogId, eventId }, query: { permanent: true } },
        }),
      ),
    onSuccess: (_data, variables) => invalidate(variables.tripLogId),
  });
}

/**
 * How many reports of one trip are about one person: the ones on its log and the ones taken off it
 * and kept, which is what removing them for good destroys.
 *
 * Asked of the two lists themselves, each narrowed to the person and read for its total alone, so
 * that the number said before the act is counted by the same reads the act's own guard trusts.
 * For whoever may write the log — the kept reports are listed to nobody else.
 */
export function useTrackingReportsHeldOf(
  tripLogId: string | undefined,
  caverId: string | undefined,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.tripTrackingReportsHeldOf(tripLogId ?? '', caverId ?? ''),
    queryFn: async () => {
      const path = { tripLogId: tripLogId! };
      const query = { caverId: caverId!, page: 1, pageSize: 1 };
      const [onTheLog, kept] = await Promise.all([
        unwrap(api.GET('/api/v1/trip-logs/{tripLogId}/tracking/events', { params: { path, query } })),
        unwrap(
          api.GET('/api/v1/trip-logs/{tripLogId}/tracking/events/removed', { params: { path, query } }),
        ),
      ]);
      return onTheLog.totalItems + kept.totalItems;
    },
    enabled: !!tripLogId && !!caverId && enabled,
    // Never a held answer: it is read to be said in a confirmation of something irreversible.
    staleTime: 0,
    gcTime: 0,
  });
}

/**
 * Removes, for good, every report of one trip about one person — the ones on its log and the ones
 * already taken off it and kept. Answers how many are gone.
 *
 * For somebody who asks to be removed: a report that is merely off the log can be put back, which
 * is not what they asked for. Nothing here touches the trip's roster.
 */
export function useRemoveTrackingReportsOf() {
  const invalidate = useInvalidateTripTracking();
  return useMutation({
    mutationFn: ({ tripLogId, caverId }: { tripLogId: string; caverId: string }) =>
      unwrap(
        api.DELETE('/api/v1/trip-logs/{tripLogId}/tracking/participants/{caverId}/events', {
          params: { path: { tripLogId, caverId } },
        }),
      ),
    onSuccess: (_data, variables) => invalidate(variables.tripLogId),
  });
}

/**
 * What the trip's log says about when each person went in and came out, on the clocks of the zone
 * named, beside what the roster holds now. A proposal to review; reading it writes nothing.
 *
 * The answer carries the trip's version, which the write that follows is checked against — so a
 * trip somebody else saved between the review and the press is a refusal. That version does not
 * move for a time typed on the roster and saved with nothing else, which is why the write also
 * repeats what the roster was shown to hold for each person.
 */
export function useTrackingRosterTimes(
  tripLogId: string | undefined,
  timeZone: string,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.tripTrackingRosterTimes(tripLogId ?? '', timeZone),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/trip-logs/{tripLogId}/tracking/roster-times', {
          params: { path: { tripLogId: tripLogId! }, query: { timeZone } },
        }),
      ),
    enabled: !!tripLogId && enabled,
    // Never a held answer: the dialog that asks is opened to act on what the log and the roster
    // say now, and the version beside the answer has to be the current one.
    staleTime: 0,
    gcTime: 0,
  });
}

/**
 * Writes the reviewed times of the people named to the trip's roster — every roster row of each.
 *
 * Each person is sent with what they were reviewed with — the pair the log gave, and what the
 * roster was shown to hold — and the server refuses the whole write when either is no longer so
 * for one of them.
 */
export function useTakeTrackingRosterTimes() {
  const queryClient = useQueryClient();
  const invalidateTracking = useInvalidateTripTracking();
  return useMutation({
    mutationFn: ({
      tripLogId,
      timeZone,
      people,
    }: {
      tripLogId: string;
      timeZone: string;
      people: TrackingRosterTimesTake[];
    }) => {
      // The version the review itself was read under, and nothing older or newer standing in for
      // it: the review is what showed the roster's present times.
      const etag = lastReadETag(`/api/v1/trip-logs/${tripLogId}/tracking/roster-times`);
      return unwrap(
        api.POST('/api/v1/trip-logs/{tripLogId}/tracking/roster-times', {
          params: { path: { tripLogId } },
          headers: etag ? { 'If-Match': etag } : undefined,
          body: { timeZone, people },
        }),
      );
    },
    onSuccess: (_data, variables) => {
      // The trip itself: its roster is what changed, and its version moved with it, so a trip
      // page or form still holding the old one has to read again before it can save.
      void queryClient.invalidateQueries({ queryKey: queryKeys.tripLog(variables.tripLogId) });
      // Hours underground are read off the roster wherever they are totalled.
      void queryClient.invalidateQueries({ queryKey: ['stats'] });
      // And the watch, whose read is what carries the trip's version for the next change to it.
      invalidateTracking(variables.tripLogId);
    },
  });
}

/**
 * The photographs hung on this trip's moments.
 *
 * <b>Read through the general link route, not through a route of its own, and that is the design
 * rather than a shortcut.</b> What is stored is an ordinary link relating the trip <em>at an
 * instant</em> to a photograph, so the one read that already resolves members, withholds the
 * unreadable ones and mints a renderings-only URL for the rest answers this too. A second read
 * would be a second protection surface to keep in step with the first.
 *
 * Narrowed to the documenting relation because that is the relation these are written under; the
 * derivation then keeps only the members anchored to a moment, so a document linked to the trip as
 * a whole — a report, a permit — is not mistaken for a picture of a moment.
 */
export function useTripMomentPictureLinks(tripLogId: string | undefined, enabled = true) {
  return useResLinksForTarget(
    'tripLog',
    tripLogId ?? '',
    { relation: 'documents', pageSize: RESLINK_PAGE_MAX },
    enabled && tripLogId !== undefined,
  );
}

/**
 * The same links for several trips at once, in the order the trips were given: what a movie of
 * more than one trip reads its photographs from. Each trip's own query, under the key the one-trip
 * hook uses, so a trip whose strip is open elsewhere is not asked twice. A trip whose links have
 * not arrived, or could not be read, answers an empty list.
 */
export function useTripsMomentPictureLinks(tripLogIds: readonly string[], enabled = true): ResLink[][] {
  const params: ResLinkPageParams = { relation: 'documents', pageSize: RESLINK_PAGE_MAX };
  return useQueries({
    queries: tripLogIds.map((tripLogId) => ({
      queryKey: queryKeys.resLinksForTarget('tripLog', tripLogId, params),
      queryFn: () =>
        unwrap(
          api.GET('/api/v1/reslinks/for-target', {
            params: { query: { type: 'tripLog', id: tripLogId, ...params } },
          }),
        ),
      enabled: enabled && tripLogId.length > 0,
    })),
    combine: (results) => results.map((result) => result.data?.items ?? []),
  });
}

/**
 * Hangs photographs on the moments they were taken at — a memory card at a time.
 *
 * Bulk because that is the act. Nobody uploads from underground, so these arrive days later when
 * somebody empties a card, which is thirty pictures at thirty instants read off the files
 * themselves. The answer names what landed and what was refused; a photograph the caller may not
 * read is in neither list, because a refusal naming it would confirm it exists.
 */
export function useAttachTrackingPictures() {
  const invalidate = useInvalidateTripMomentPictures();
  return useMutation({
    mutationFn: ({ tripLogId, items }: { tripLogId: string; items: TrackingPictureInput[] }) =>
      unwrap(
        api.POST('/api/v1/trip-logs/{tripLogId}/tracking/pictures', {
          params: { path: { tripLogId } },
          body: { items },
        }),
      ),
    onSuccess: (_data, variables) => invalidate(variables.tripLogId),
  });
}

/** Takes one photograph off the moment it was hung on. */
export function useDetachTrackingPicture() {
  const invalidate = useInvalidateTripMomentPictures();
  return useMutation({
    mutationFn: ({ tripLogId, memberId }: { tripLogId: string; memberId: string }) =>
      unwrapVoid(
        api.DELETE('/api/v1/trip-logs/{tripLogId}/tracking/pictures/{memberId}', {
          params: { path: { tripLogId, memberId } },
        }),
      ),
    onSuccess: (_data, variables) => invalidate(variables.tripLogId),
  });
}

/**
 * What a write of these has to refresh: the trip's links, under every page shape anybody asked for
 * them with. The prefix is the entity, so a panel reading them with a different page size is
 * refreshed too — the alternative is a strip that keeps showing a photograph somebody has just
 * taken off it.
 */
function useInvalidateTripMomentPictures() {
  const queryClient = useQueryClient();
  return (tripLogId: string) =>
    void queryClient.invalidateQueries({ queryKey: ['reslinks', 'for-target', 'tripLog', tripLogId] });
}

/**
 * How long an answer about what a depth means is worth keeping.
 *
 * <b>Long, and kept honest by being thrown away on purpose rather than by expiring.</b> What the
 * answer depends on is the trip's model, datum and filter, and the one thing that changes those is
 * a configuration write — which this client makes, and which drops every held answer for the trip
 * as it lands. A clock is the wrong instrument for that: short enough to catch a change promptly
 * and it re-asks constantly for nothing, long enough to be quiet and it is still a window in which
 * a stale answer is drawn. Invalidation closes the window outright, so the clock is left only to
 * bound how long a change made by *another* coordinator's browser goes unnoticed here.
 *
 * Five minutes is that bound. It matters on a remount — somebody leaving this tab and coming back —
 * and nowhere else, because a mounted screen does not re-ask an unchanged question.
 */
const TRACKING_DEPTH_STALE_MS = 5 * 60_000;

/**
 * Everything the two readings below have in common, written once because they are one question
 * asked from two places and must not drift into two answers.
 *
 * <b>Never re-asked because a window regained focus.</b> TanStack's default is to refetch on focus,
 * which is right for a figure that moves on its own and wrong for this one: what a depth means in a
 * cave does not change because somebody alt-tabbed back to the watch. On a live callout this screen
 * is left and returned to constantly, and with a log of twenty distinct depths the default would
 * fire twenty full station-table reads every time — on a cave that may hold tens of thousands of
 * stations, and for an answer that is already correct. What genuinely changes the answer is the
 * configuration write, and that is handled where it happens.
 *
 * <b>And no `retry` of its own, which is the fix rather than an omission.</b> These reads used to
 * override the shared policy with `retry: false`, on the reasoning that the expected failure is a
 * settled one — no model, no datum, a cave this account may not be told the position of — that
 * repeating would only delay. True of those, and wrong about the failure that matters: a 500 or a
 * connection dropped mid-callout is the case where one unanswered question leaves a coordinator
 * looking at a card that cannot warn them, and refusing to ask again turns a blip into a silence
 * that lasts as long as the held answer does. The shared policy already draws exactly that
 * distinction — settled refusals are not repeated, everything else is — so the right thing here is
 * to stop overriding it.
 */
const trackingDepthQueryOptions = {
  staleTime: TRACKING_DEPTH_STALE_MS,
  refetchOnWindowFocus: false,
} as const;

/**
 * Which stations a depth could mean, best match first, under the trip's own filter and datum.
 *
 * <b>This is the only place the client learns what a depth resolves to, and it is deliberately not
 * a rule written here.</b> What "nearest" means — which altitude is the datum, which spellings of a
 * station name a filter matches, how the ties break — lives in one place on the server, and a
 * second opinion computed in a browser would drift from it silently and be believed. So everything
 * on this side that needs a candidate or the distance to it asks this.
 *
 * A read in everything but its HTTP method: it writes nothing, and is a POST only because the
 * question travels in a body. Held under a key so that the warning drawn while somebody types a
 * depth, the list of stations that depth could mean, and the log rows already carrying that depth
 * are one answer rather than three requests. The whole list is asked for rather than only the
 * nearest, because the same answer serves the warning, which needs the first of them, and the
 * list somebody picks a station out of, which needs all — one question, not two.
 *
 * Worth asking before recording rather than after: a depth resolves to whichever station is
 * nearest with no tolerance under it at all, so a depth nothing in the cave is near still lands
 * somewhere, and "nearest" over a filter somebody set a week ago is exactly the decision a person
 * wants to see made before it becomes the position on the log.
 */
export function useTrackingDepthReading(
  tripLogId: string | undefined,
  depthM: number | null | undefined,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.tripTrackingDepth(tripLogId ?? '', depthM ?? 0),
    queryFn: () =>
      unwrap(
        api.POST('/api/v1/trip-logs/{tripLogId}/tracking/resolve-depth', {
          params: { path: { tripLogId: tripLogId! } },
          body: { depthM: depthM!, take: null },
        }),
      ),
    enabled: !!tripLogId && depthM !== null && depthM !== undefined && enabled,
    ...trackingDepthQueryOptions,
  });
}

/**
 * The same question asked about every depth already on the log, so a row can say how far the
 * station it records sits from the depth that was reported.
 *
 * <b>One query per distinct depth rather than one per row.</b> A party reported at 120 m is several
 * rows carrying one number, and the answer is a property of the number and the trip, not of the
 * row — so the rows share a cache entry and a long log of one depth costs one request. Depths
 * nobody typed are never asked about: only a depth report carries one, and the caller narrows even
 * those to the ones it could honestly measure.
 *
 * <b>Sharing a key with the reading above is the point of the two being written this way.</b> The
 * depth somebody is typing into the report card is usually a depth already on the log — a party
 * moves together and is reported together — so the card's warning and the rows' marks come out of
 * one held answer rather than one request each.
 */
export function useTrackingDepthReadings(
  tripLogId: string | undefined,
  depths: readonly number[],
  enabled = true,
) {
  return useQueries({
    queries: depths.map((depthM) => ({
      queryKey: queryKeys.tripTrackingDepth(tripLogId ?? '', depthM),
      queryFn: () =>
        unwrap(
          api.POST('/api/v1/trip-logs/{tripLogId}/tracking/resolve-depth', {
            params: { path: { tripLogId: tripLogId! } },
            body: { depthM, take: null },
          }),
        ),
      enabled: !!tripLogId && enabled,
      ...trackingDepthQueryOptions,
    })),
    combine: (results) =>
      new Map(
        results.flatMap((result, index) =>
          result.data ? ([[depths[index], result.data]] as [number, TrackingDepthCandidate[]][]) : [],
        ),
      ),
  });
}

// ---- publishing a tracked trip, and following one ----

export type TripTrackingShare = components['schemas']['TripTrackingShareDto'];
export type TripTrackingShareCreated = components['schemas']['TripTrackingShareCreatedDto'];
export type PublicTripEnvelope = components['schemas']['PublicTripTrackingEnvelopeDto'];
export type PublicTripParticipant = components['schemas']['PublicTripParticipantDto'];
export type PublicTripTeam = components['schemas']['PublicTripTeamDto'];
export type PublicTripModel = components['schemas']['PublicTripSurveyModelDto'];

/**
 * One photograph a published trip hangs on a station of its drawing.
 *
 * The envelope is the whole of what a visitor holding a follow link can read — every other address
 * in this installation refuses them — so this is the only shape a station picture reaches a public
 * surface in. What it carries is a station, a URL good for a rendering, and a caption; there is
 * deliberately no document id and no way from here to the photograph's own page.
 */
export type PublicTripStationPicture = components['schemas']['PublicTripStationPictureDto'];

/**
 * One scanned map sheet a published trip draws its party on, points resolved and all.
 *
 * Like the pictures above, this is the only shape a map reaches a public surface in: the
 * envelope carries the rendering's signed URL and the station points already filtered to that
 * very rendering, because the routes the signed-in folds read links from answer only to an
 * account. There is deliberately no document id, no link id and no version history here — a
 * public page draws what it was sent and derives nothing further.
 */
export type PublicTripRasterMap = components['schemas']['PublicTripRasterMapDto'];

/**
 * One place a cave has named, as a published survey comes with it: a station of that survey, the
 * depth the cave declared it at — a magnitude, never negative — and what people call it.
 *
 * The list is on the model and is empty on every installation that has not chosen to publish
 * these names, which is how one is installed. Read through `placeLabelFor`; there is deliberately
 * no declaration id and nothing about who declared it.
 */
export type PublicTripPlace = components['schemas']['PublicTripPlaceDto'];

/**
 * The trip's follow links — when each was minted, by whom, and whether it has been taken back.
 *
 * Deliberately carries no token. A token exists in one response, the one that minted it, and is
 * stored only as a hash; a list that could hand one back would be a list somebody could read a
 * live capability out of long after the person who minted it had gone.
 */
export function useTripTrackingShares(tripLogId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.tripTrackingShares(tripLogId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/trip-logs/{tripLogId}/tracking/shares', {
          params: { path: { tripLogId: tripLogId! } },
        }),
      ),
    enabled: !!tripLogId && enabled,
    // Requires write on the trip; a 403 is a settled answer, not worth three attempts.
    retry: false,
  });
}

/** The prefix every page of the administrators' list of published links is held under. */
const publishedLinksPrefix = ['trip-logs', 'published-links'] as const;

function useInvalidateTripTrackingShares() {
  const queryClient = useQueryClient();
  return (tripLogId: string) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.tripTrackingShares(tripLogId) });
    // The installation's own list is made of these same links, so a link handed out, taken back
    // or exchanged on a trip is a change to it too.
    void queryClient.invalidateQueries({ queryKey: publishedLinksPrefix });
  };
}

/**
 * Publishes the trip: mints a link somebody without an account can follow the party on.
 *
 * <b>The answer carries the only copy of the token there will ever be.</b> The server keeps its
 * hash and nothing else, so whatever the caller does with this answer is the whole of what the
 * token is ever used for — which is why the surface that calls this shows the address, and the
 * embed snippet built from it, in the same breath rather than offering them from the list
 * afterwards.
 */
export function useMintTripTrackingShare() {
  const invalidate = useInvalidateTripTrackingShares();
  return useMutation({
    mutationFn: ({ tripLogId }: { tripLogId: string }) =>
      unwrap(
        api.POST('/api/v1/trip-logs/{tripLogId}/tracking/shares', {
          params: { path: { tripLogId } },
        }),
      ),
    onSuccess: (_data, variables) => invalidate(variables.tripLogId),
  });
}

/** Takes a link back. Revoking one already revoked is the state asked for, not an error. */
export function useRevokeTripTrackingShare() {
  const invalidate = useInvalidateTripTrackingShares();
  return useMutation({
    mutationFn: ({ tripLogId, shareId }: { tripLogId: string; shareId: string }) =>
      unwrapVoid(
        api.DELETE('/api/v1/trip-logs/{tripLogId}/tracking/shares/{shareId}', {
          params: { path: { tripLogId, shareId } },
        }),
      ),
    onSuccess: (_data, variables) => invalidate(variables.tripLogId),
  });
}

/**
 * Exchanges one link for a fresh one in a single act: the old address stops answering and a new
 * one is handed back.
 *
 * <b>As with publishing, the answer is the only copy of the new token there will ever be</b>, so
 * whatever calls this shows the address at once. The fresh link runs out when the old one would
 * have, and nobody on the trip is told, because nothing more is published than was a moment ago.
 */
export function useReplaceTripTrackingShare() {
  const invalidate = useInvalidateTripTrackingShares();
  return useMutation({
    mutationFn: ({ tripLogId, shareId }: { tripLogId: string; shareId: string }) =>
      unwrap(
        api.POST('/api/v1/trip-logs/{tripLogId}/tracking/shares/{shareId}/replace', {
          params: { path: { tripLogId, shareId } },
        }),
      ),
    onSuccess: (_data, variables) => invalidate(variables.tripLogId),
  });
}

// ---- everything this installation has published, for its administrators ----

export type PublishedLink = components['schemas']['PublishedLinkDto'];
export type PublishedLinks = components['schemas']['PublishedLinksDto'];
export type PublishedLinksWithdrawn = components['schemas']['PublishedLinksWithdrawnDto'];

/**
 * The word the server asks for before it takes back every link of the installation.
 *
 * Not a secret and not what a person types — the page asks for a word in the reader's own
 * language and sends this one. Its whole purpose is that a request built for something else
 * cannot perform this act by accident.
 */
const REVOKE_EVERYTHING_WORD = 'revoke-everything';

/**
 * Every published link of the installation, with what each does for its holder at one instant.
 *
 * For full administrators; anybody else is refused, and the refusal is a settled answer rather
 * than something a second attempt changes, so it is not retried.
 */
export function usePublishedLinks(params: PublishedLinksParams) {
  return useQuery({
    queryKey: queryKeys.publishedLinks(params),
    queryFn: () =>
      unwrap(api.GET('/api/v1/admin/published-trips', { params: { query: { ...params } } })),
    // The page before stays on screen while the next is read, so paging and re-ordering do not
    // blank a list somebody is in the middle of acting on.
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/**
 * What a withdrawal reaches in this browser's cache: every trip's own list of links, and with it
 * every read of a trip's watch, since "is this trip published" is part of that answer.
 */
function useInvalidateAfterWithdrawal() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['trip-logs'] });
}

/** Takes back every link of one trip in one act. Cannot be undone. */
export function useRevokeTripPublishedLinks() {
  const invalidate = useInvalidateAfterWithdrawal();
  return useMutation({
    mutationFn: ({ tripLogId }: { tripLogId: string }) =>
      unwrap(
        api.POST('/api/v1/admin/published-trips/{tripLogId}/revoke-all', {
          params: { path: { tripLogId } },
        }),
      ),
    onSuccess: invalidate,
  });
}

/** Takes back every link of the installation in one act. Cannot be undone. */
export function useRevokeEveryPublishedLink() {
  const invalidate = useInvalidateAfterWithdrawal();
  return useMutation({
    mutationFn: () =>
      unwrap(
        api.POST('/api/v1/admin/published-trips/revoke-everything', {
          body: { confirm: REVOKE_EVERYTHING_WORD },
        }),
      ),
    onSuccess: invalidate,
  });
}

/**
 * How often a published trip is re-read while the party is underground.
 *
 * Gentler than the signed-in watch on purpose. That one is read by a co-ordinator with the tab
 * open and a session behind every request; this one is read by however many families, friends and
 * club pages somebody handed the link to, by callers the server knows nothing about and can lean
 * on nothing about. A minute is still well inside the rhythm reports actually arrive at — they
 * come by relayed word, minutes apart — so what the slower interval costs a follower is at most
 * half a minute of a position that was already several minutes old.
 */
const PUBLIC_TRACKING_POLL_MS = 60_000;

/**
 * How often a finished trip that publishes photographs is re-read, and why one is at all.
 *
 * <b>Nothing on such a page changes any more; what changes is what its URLs open.</b> Every picture
 * the envelope carries is a signed URL good for about ten minutes, and those are spent lazily — a
 * thumbnail is fetched when a finger lands on the station it hangs at, not when the page loads. The
 * page most likely to be read that way is the one this matters most for: an embed inside an article
 * about a trip that came out months ago, where a reader gets to the drawing when they get to it. A
 * page that read once would offer them broken pictures, on somebody's website, with no way back
 * except reloading a frame they cannot see the edges of.
 *
 * Comfortably inside the ten minutes, and seven times gentler than the live watch. Two other things
 * keep it modest: the query only asks this of a trip whose envelope actually carried photographs,
 * and an interval does not fire for a tab nobody is looking at — a backgrounded article costs
 * nothing until it is looked at again, which is itself a moment that re-reads.
 *
 * The map sheets ride the same interval for the same reason: a sheet's image URL is the same
 * kind of signed address, spent even later — a map tab's picture is fetched when the tab is
 * first opened, which on an article about a finished trip can be an hour after the page was.
 * One interval, not a second scheme; a trip carrying either kind of URL keeps reading.
 */
const PUBLIC_PICTURE_REFRESH_MS = 7 * 60_000;

/**
 * The conditions a published read is kept fresh on — said here rather than shared with the
 * signed-in reads, because the two answers carry different shapes of state and neither interval is
 * the other's number.
 *
 * Exported so the conditions can be checked directly. This is the whole of what stops a link handed
 * round a club from being a hundred tabs asking a server about a finished trip forever, and it is
 * worth a test that does not have to stand a query client up to state it.
 */
export function publicTripPollInterval(trip: PublicTripEnvelope | undefined) {
  if (trip?.state === 'armed') {
    return PUBLIC_TRACKING_POLL_MS;
  }
  // A finished trip with nothing to keep alive is asked about no more, which is the ordinary case:
  // most installations publish no photographs and declare no maps, and for those this page reads
  // exactly once. Either kind of signed URL in the envelope is something that goes stale on its
  // own, and both are refreshed by the one re-read.
  return (trip?.model?.pictures.length ?? 0) > 0 || (trip?.model?.rasterMaps?.length ?? 0) > 0
    ? PUBLIC_PICTURE_REFRESH_MS
    : (false as const);
}

/**
 * The interval above, silenced for good once the server has answered that the link is done.
 *
 * <b>A published trip ends by answering 404, and the last envelope stays in hand when it does.</b>
 * Every publication lapses — a share's grace after the watch closes runs out, or the link is taken
 * back — and from then on the route answers every read with the same refusal it gives an unknown
 * token, while the query keeps the envelope it read last. Decided on the envelope alone, the poll
 * would go on for as long as that envelope says it should: a closed trip carrying sheets or
 * photographs would be re-read every few minutes from every tab that was ever opened on it, for a
 * link that will never answer again. So the refusal is consulted first, and a settled one stops the
 * clock. A network fault or a 5xx is not settled and keeps the interval, which is how the page comes
 * back on its own once the phone has signal again.
 */
export function publicTripRefetchInterval(trip: PublicTripEnvelope | undefined, error: unknown) {
  return isSettledRefusal(error) ? (false as const) : publicTripPollInterval(trip);
}

/**
 * Whether a reader coming back to the tab — or a phone finding its signal again — re-reads the
 * published trip.
 *
 * <b>The interval is only half of what a tab costs.</b> TanStack re-reads every query on each
 * return to its window and each reconnection unless told otherwise, and a finished trip whose
 * interval has stopped would still be asked about on every glance at the tab, by every reader who
 * was ever handed the link. So a return re-reads exactly when the interval would have kept reading:
 * a party still underground, whose family wants where they are now rather than in a minute; a
 * finished trip carrying signed addresses, which a backgrounded tab's interval never refreshed; and
 * a page that has no envelope at all yet, because a first read that failed for want of signal is
 * what a return is for. A settled refusal is never asked again.
 */
export function publicTripRefetchOnReturn(trip: PublicTripEnvelope | undefined, error: unknown) {
  if (isSettledRefusal(error)) {
    return false;
  }
  return trip === undefined || publicTripPollInterval(trip) !== false;
}

/**
 * A published trip as somebody holding its link sees it.
 *
 * Every unusable token — malformed, unknown, revoked, lapsed, belonging to a watch that has been
 * closed, a cave that has since been protected, a trip that is gone — answers one identical 404,
 * so there is exactly one failure to render and no second reading of it to attempt. `retryQuery`
 * already declines to retry a 4xx, so that answer settles at once instead of holding a stranger on
 * a spinner for seven seconds.
 *
 * <b>A publication now ends on its own, and this page needs no change for it.</b> That is the
 * point of every ending answering as an unknown token does: a link that ran out while somebody had
 * the page open, and one that was never real, arrive here as the same thing and are drawn as the
 * same thing. Nothing here may tell the two apart, and nothing here tries.
 *
 * Kept fresh while the watch is armed, and afterwards only for as long as something in the answer
 * goes stale on its own — which is the signed URL behind every published photograph, and nothing
 * else. A closed trip that publishes none is read once and never asked about again, because a link
 * handed round a club can be open in a hundred tabs nobody is looking at — and that holds for a
 * return to the tab and a reconnection as much as for the interval.
 */
export function usePublicTrip(token: string | undefined) {
  return useQuery({
    queryKey: queryKeys.publicTrip(token ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/public/trips/{token}', { params: { path: { token: token! } } })),
    enabled: !!token,
    refetchInterval: (query) => publicTripRefetchInterval(query.state.data, query.state.error),
    refetchOnWindowFocus: (query) => publicTripRefetchOnReturn(query.state.data, query.state.error),
    refetchOnReconnect: (query) => publicTripRefetchOnReturn(query.state.data, query.state.error),
  });
}

// ---- the past trips of the same cave, for somebody holding one published link ----

export type PublicPastTripList = components['schemas']['PublicPastTripListDto'];
export type PublicPastTrip = components['schemas']['PublicPastTripDto'];
export type PublicPastTrack = components['schemas']['PublicPastTrackDto'];
export type PublicPastTrackParticipant = components['schemas']['PublicPastTrackParticipantDto'];
export type PublicPastTrackFix = components['schemas']['PublicPastTrackFixDto'];
export type PublicPastTrackPicture = components['schemas']['PublicPastTrackPictureDto'];

/**
 * How long the list of a cave's past trips is believed before it is worth asking for again.
 *
 * Long against how often the answer changes and short against how long a tab is left open: a
 * reader who glances back within it is shown what they were shown, at no cost to anybody, and one
 * who comes back after lunch is shown the trip that finished meanwhile.
 */
export const PUBLIC_ARCHIVE_FRESH_MS = 5 * 60_000;

/**
 * Whether a return to the tab, or a reconnection, may re-read the list of past trips.
 *
 * <b>"May", because how old the list is decides the rest</b>: the query library re-reads on a
 * return only what has outlived its freshness, so this answers yes for everything but one case. A
 * settled refusal is that case — an installation that does not open its archive to visitors, or a
 * link that is over, answers this route the same way for good, and a list that was never handed
 * over counts as out of date for ever. Left to that rule, every glance at the tab would ask again
 * for something that will never be given.
 */
export function publicPastTripsRefetchOnReturn(error: unknown) {
  return !isSettledRefusal(error);
}

/**
 * The past trips of this link's cave — the archive behind a published page.
 *
 * <b>Asked for only when somebody asks for it, and that is a rule rather than a tuning.</b> The
 * page this hangs off is opened by families while a party is underground, on phones, in numbers
 * nobody can see; its one job is to say whether the party is out. Reading a cave's history on
 * every one of those opens would double the traffic of the surface that must stay cheapest, to
 * answer a question nobody asked. So the picker fetches on the press that opens it and the caller
 * holds the gate — which is also what makes the embed's cost unchanged for an article whose reader
 * never opens the list.
 *
 * <b>Believed for a few minutes, and never polled.</b> An archive changes only when a party
 * comes out and its trip moves here from the list of parties being followed — a few times a day
 * at the busiest camp — and this list carries no signed URL to go stale: what it holds is a title,
 * two dates and two numbers. So no clock re-reads it. But a page left open through an afternoon
 * must not go on listing the cave as it stood at noon: once the list is older than
 * {@link PUBLIC_ARCHIVE_FRESH_MS} it is read again the next time somebody is actually looking —
 * on coming back to the tab, on the signal returning, on opening the list again — and inside that
 * time none of those costs a request. See {@link publicPastTripsRefetchOnReturn} for the one
 * answer that is never asked for twice.
 */
export function usePublicPastTrips(token: string | undefined, enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.publicPastTrips(token ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/public/trips/{token}/past', { params: { path: { token: token! } } })),
    enabled: !!token && enabled,
    staleTime: PUBLIC_ARCHIVE_FRESH_MS,
    refetchOnWindowFocus: (query) => publicPastTripsRefetchOnReturn(query.state.error),
    refetchOnReconnect: (query) => publicPastTripsRefetchOnReturn(query.state.error),
  });
}

/**
 * How often a past trip's answer is read again: never, unless it came with photographs.
 *
 * Returns `false` for an answer that carries no photograph — every answer of an installation that
 * does not publish them — and the interval the followed page keeps its own pictures alive by for
 * one that does.
 */
export function publicPastTrackPollInterval(track: PublicPastTrack | undefined) {
  // A photograph's address is signed for a few minutes and is spent when the clock reaches its
  // moment — which, on a replay somebody is watching, can be an hour after the answer landed. So
  // an answer that carries any is read again on the interval the followed page keeps its own
  // pictures alive by. One that carries none — every answer of an installation that does not
  // publish them, and every answer from before the list existed — is read once, as it always was.
  return (track?.pictures?.length ?? 0) > 0 ? PUBLIC_PICTURE_REFRESH_MS : (false as const);
}

/**
 * One past trip of this link's cave, played back.
 *
 * <b>Never fetched until a visitor has chosen a trip.</b> `tripLogId` undefined is the whole of the
 * gate: a page that has not been asked to play anything makes no request, so the archive costs a
 * follower nothing until they ask for it. The weight is the reason — this answer carries the whole
 * log of a party's movements and the survey they were measured on, where the list above carries
 * two dates.
 *
 * Read once per trip and not polled, for the reason the list is not. A finished trip's track does
 * not change. Its model's delivery URL does expire, but the survey file behind it is fetched the
 * moment the viewer is handed the address, which is the moment this answer lands — so there is no
 * later spend for a poll to keep alive, unlike the station pictures on the live envelope.
 *
 * <b>The one exception is a replay that came with photographs on its moments</b>, which only an
 * installation that publishes them sends: see {@link publicPastTrackPollInterval}.
 */
export function usePublicPastTrack(token: string | undefined, tripLogId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.publicPastTrack(token ?? '', tripLogId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/public/trips/{token}/past/{tripLogId}', {
          params: { path: { token: token!, tripLogId: tripLogId! } },
        }),
      ),
    enabled: !!token && !!tripLogId,
    staleTime: Infinity,
    refetchInterval: (query) => publicPastTrackPollInterval(query.state.data),
  });
}

// ---- the parties being followed in the same cave right now, for the same link ----

export type PublicLiveTripList = components['schemas']['PublicLiveTripListDto'];
export type PublicLiveTrip = components['schemas']['PublicLiveTripDto'];

/**
 * How often the list of parties being followed is re-read while nobody in it is underground.
 *
 * Such a list is not finished the way a closed trip is: it is a statement about a cave, and the
 * next party can go in at any time. A reader who left the list standing open — or who is watching
 * a party that has just come out — would otherwise be shown "nobody is being followed" for as
 * long as the tab lived, however many parties went in meanwhile, and would never be shown that
 * the party they were watching has left the list. Slow, because nothing in such a list moves, and
 * the same few minutes the archive is believed for: the two lists are the two halves of one cave
 * and a trip passes from this one to that one.
 */
export const PUBLIC_IDLE_POLL_MS = 5 * 60_000;

/**
 * How often the list of parties being followed is re-read, and when it stops.
 *
 * <b>At the followed pace while somebody in it is underground, slowly while nobody is, and never
 * again once the link has been refused for good.</b> A row whose watch has closed is a party that
 * is out and a trip on its way into the archive; nothing in it moves any more, so a list of such
 * rows — or an empty one, or one whose first read has not landed — is kept at
 * {@link PUBLIC_IDLE_POLL_MS}, which is what notices the next party going in and a finished one
 * leaving. A settled refusal stops the clock, as it does for the envelope: a link that has been
 * taken back does not go on asking after the cave's other parties.
 *
 * <b>What bounds the cost is not this number but who is asked for it.</b> The caller reads this
 * list only while a reader has its section open or is watching a party out of it, and an interval
 * does not fire in a tab nobody is looking at — so the slow pace is paid by a visible page whose
 * reader asked about the cave, and by nobody else.
 *
 * Exported to be checked directly, for the reason the envelope's interval is.
 */
export function publicLiveTripsPollInterval(list: PublicLiveTripList | undefined, error: unknown) {
  if (isSettledRefusal(error)) {
    return false as const;
  }
  return list?.trips.some((trip) => trip.state === 'armed')
    ? PUBLIC_TRACKING_POLL_MS
    : PUBLIC_IDLE_POLL_MS;
}

/**
 * Whether a return to the tab, or a reconnection, re-reads the list of parties being followed.
 *
 * <b>At once while somebody is underground or no list has arrived; otherwise only a list the slow
 * pace would already have re-read.</b> A family coming back to the tab while a party is in wants
 * where they are now, and a first read that never landed is what a return is for. A list in which
 * nobody is underground is different: it changes rarely, a reader flicking between two tabs would
 * ask for it on every flick, and nothing they are waiting for is in it. But a hidden tab's
 * interval does not fire, so a reader back after an hour holds a list an hour old — and for them
 * the return is the tick that was missed. `readAt` is when the list in hand arrived.
 *
 * A settled refusal is never asked again.
 */
export function publicLiveTripsRefetchOnReturn(
  list: PublicLiveTripList | undefined,
  error: unknown,
  readAt = 0,
  now = Date.now(),
) {
  const interval = publicLiveTripsPollInterval(list, error);
  if (interval === false) {
    return false;
  }
  return list === undefined || interval === PUBLIC_TRACKING_POLL_MS || now - readAt >= interval;
}

/**
 * Every party being followed in this link's cave right now — the other half of the archive.
 *
 * <b>A published trip is in exactly one of the two lists at any moment, and a page has to read
 * both to see all of them.</b> The archive carries finished trips and deliberately excludes one
 * still being followed; this list carries those, each with its party, including a watch that has
 * closed but whose link is still inside its grace — a trip that is in neither list is a gap this
 * page would otherwise have for the length of that grace. Rows are matched to the page's own trip
 * by `tripLogId` and never by title: two trips of one cave may share a title.
 *
 * <b>Behind the same press as the archive, for the archive's reason.</b> The page this hangs off
 * is opened by families while a party is underground, on phones, in numbers nobody can see, and
 * its cost has to stay what it was; a reader who never opens the list is never charged for it.
 * Read afresh each time the list is opened — it is a statement about now — and kept fresh for as
 * long as it is being read: at the followed pace while a row in it is still underground, slowly
 * otherwise.
 */
export function usePublicLiveTrips(token: string | undefined, enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.publicLiveTrips(token ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/public/trips/{token}/live', { params: { path: { token: token! } } })),
    enabled: !!token && enabled,
    refetchInterval: (query) => publicLiveTripsPollInterval(query.state.data, query.state.error),
    refetchOnWindowFocus: (query) =>
      publicLiveTripsRefetchOnReturn(
        query.state.data,
        query.state.error,
        query.state.dataUpdatedAt,
      ),
    refetchOnReconnect: (query) =>
      publicLiveTripsRefetchOnReturn(
        query.state.data,
        query.state.error,
        query.state.dataUpdatedAt,
      ),
  });
}
