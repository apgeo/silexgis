// SPDX-License-Identifier: AGPL-3.0-or-later
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, ApiError, lastReadETag } from '../client.ts';
import type { components } from '../schema';
import { invalidateAfterDelete, unwrap, unwrapVoid, useInvalidateHistory } from './shared.ts';
import type { ActivityState, EventListParams } from './types.ts';
import { queryKeys } from './queryKeys.ts';
import type { TripInvitationAnswer } from './trips.ts';

export type EventInvitationInfo = components['schemas']['EventInvitationDto'];
export type EventInvitationList = components['schemas']['EventInvitationListDto'];

/**
 * Everybody on a club event's list and what each has said, in the order the server put them in.
 *
 * The same rows a trip's answers are, read through the shape that names an event: there is one
 * answering mechanism and one table behind both. As on a trip, the place each person holds, in or
 * waiting, and whether this caller may write an answer for them are the server's conclusions and
 * never re-derived here.
 *
 * A kind of event that nobody comes to — a deadline — takes no answers at all and refuses this
 * whole group under its own code, so nothing asks for a list it has no way to hold.
 */
export function useEventInvitations(eventId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.eventInvitations(eventId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/events/{eventId}/invitations', {
          params: { path: { eventId: eventId! } },
        }),
      ),
    enabled: !!eventId && enabled,
  });
}

function useInvalidateEventInvitations() {
  const queryClient = useQueryClient();
  const invalidateHistory = useInvalidateHistory();
  return (eventId: string) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.eventInvitations(eventId) });
    // A row here is an audit child of the event, so writing one moves the event's own timeline.
    invalidateHistory();
  };
}

/**
 * Puts somebody on an event's list. The person is named by their entry in the club's directory
 * and never by a bare name, exactly as on a trip: a list of people to be told about something
 * that could hold text nobody can resolve would be a list nobody can act on.
 */
export function useInviteToEvent() {
  const invalidate = useInvalidateEventInvitations();
  return useMutation({
    mutationFn: ({ eventId, caverId }: { eventId: string; caverId: string }) =>
      unwrap(
        api.POST('/api/v1/events/{eventId}/invitations', {
          params: { path: { eventId } },
          body: { caverId },
        }),
      ),
    onSuccess: (_data, { eventId }) => invalidate(eventId),
  });
}

/**
 * Records what one person says about coming to an event. The note travels with the answer and is
 * replaced by it, so a cleared note is sent as an explicit absence rather than omitted.
 */
export function useAnswerEventInvitation() {
  const invalidate = useInvalidateEventInvitations();
  return useMutation({
    mutationFn: ({
      eventId,
      caverId,
      response,
      note,
    }: {
      eventId: string;
      caverId: string;
      response: TripInvitationAnswer;
      note: string | null;
    }) =>
      unwrap(
        api.PUT('/api/v1/events/{eventId}/invitations/{caverId}/response', {
          params: { path: { eventId, caverId } },
          body: { response, note },
        }),
      ),
    onSuccess: (_data, { eventId }) => invalidate(eventId),
  });
}

/** Picks one person out for the event, or puts them back in the order. The order is unchanged. */
export function useSelectForEvent() {
  const invalidate = useInvalidateEventInvitations();
  return useMutation({
    mutationFn: ({
      eventId,
      caverId,
      selected,
    }: {
      eventId: string;
      caverId: string;
      selected: boolean;
    }) =>
      unwrap(
        api.PUT('/api/v1/events/{eventId}/invitations/{caverId}/selection', {
          params: { path: { eventId, caverId } },
          body: { selected },
        }),
      ),
    onSuccess: (_data, { eventId }) => invalidate(eventId),
  });
}

/**
 * Takes somebody off an event's list entirely, answer and all. For a person put on it by mistake —
 * recording a "no" in their name instead would be writing down words they never said.
 */
export function useRemoveEventInvitation() {
  const invalidate = useInvalidateEventInvitations();
  return useMutation({
    mutationFn: ({ eventId, caverId }: { eventId: string; caverId: string }) =>
      unwrapVoid(
        api.DELETE('/api/v1/events/{eventId}/invitations/{caverId}', {
          params: { path: { eventId, caverId } },
        }),
      ),
    onSuccess: (_data, { eventId }) => invalidate(eventId),
  });
}

// ---------------------------------------------------------------------------
// Calendar events
// ---------------------------------------------------------------------------

export type EventInfo = components['schemas']['EventDto'];
export type EventWrite = components['schemas']['EventWriteRequest'];
export type EventKind = components['schemas']['EventKind'];
export type EventDefaults = components['schemas']['EventDefaultsDto'];
/** How a repeating event comes round. A closed list the server steps by once, at creation. */
export type EventRecurrenceFrequency = NonNullable<
  components['schemas']['EventRecurrenceFrequency']
>;

/** The events this reader may open, narrowed by the filters the list offers. */
export function useEvents(params: EventListParams = {}) {
  return useQuery({
    queryKey: queryKeys.events(params),
    queryFn: () => unwrap(api.GET('/api/v1/events', { params: { query: params } })),
    // Paging or retyping a filter keeps the rows on screen while the next answer arrives, rather
    // than emptying the table under whoever is reading it.
    placeholderData: keepPreviousData,
  });
}

/**
 * One event. An event the caller may not read answers exactly as one that does not exist does —
 * the server spells both `event.not_found` — so the page has no way to tell them apart and must
 * not try: an address that answered differently for the two would be one anybody could probe for
 * the existence of an event they cannot see.
 */
export function useEvent(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.event(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/events/{id}', { params: { path: { id: id! } } })),
    enabled: !!id,
    // A refusal here is a settled answer about the caller, not a transient failure: retrying it
    // three times only delays the page saying so.
    retry: false,
  });
}

/**
 * The audience a new event would get if its author names none.
 *
 * Read from the server rather than worked out here, because the write applies the same rule: a
 * form that guessed would be guessing about who can read something, and the two answers would be
 * free to disagree the day either changed.
 */
export function useEventDefaults(enabled = true) {
  return useQuery({
    queryKey: queryKeys.eventDefaults,
    queryFn: () => unwrap(api.GET('/api/v1/events/defaults')),
    enabled,
  });
}

/**
 * Re-reads everything a write to an event changes, and hands back the promise rather than
 * starting it and forgetting it.
 *
 * The promise matters on the writes the server checks a precondition on. No write answers with
 * the version it produced, so the token the next write must carry is only recorded by a read —
 * and a mutation that settled before that read left the buttons live while the cached token was
 * still the one from before. The second click then carries a spent version and is refused for a
 * move that was perfectly legal.
 */
function useInvalidateEvents() {
  const queryClient = useQueryClient();
  /** `deleted` is the event that has just gone, when that is why this is being called. */
  return (id?: string, deleted?: string) => {
    const pending = [
      deleted
        ? invalidateAfterDelete(queryClient, ['events'], deleted)
        : queryClient.invalidateQueries({ queryKey: ['events'] }),
      // An event is a row on the calendar, so writing one moves what that window answers.
      queryClient.invalidateQueries({ queryKey: ['calendar'] }),
    ];
    if (id) {
      pending.push(queryClient.invalidateQueries({ queryKey: queryKeys.event(id) }));
    }
    return Promise.all(pending);
  };
}

export function useCreateEvent() {
  const invalidate = useInvalidateEvents();
  return useMutation({
    mutationFn: (body: EventWrite) => unwrap(api.POST('/api/v1/events', { body })),
    onSuccess: () => invalidate(),
  });
}

/**
 * Stores an edited event. The server requires the version the form was loaded against; a full
 * update is on the path the detail read captured that version under, so the precondition is
 * threaded onto it without this call having to say so.
 */
export function useUpdateEvent() {
  const invalidate = useInvalidateEvents();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: EventWrite }) =>
      unwrap(api.PUT('/api/v1/events/{id}', { params: { path: { id } }, body })),
    // Handed back rather than started and forgotten: the next write on this event is checked
    // against the version this one produced, and only the read records it.
    onSuccess: (_data, variables) => invalidate(variables.id),
  });
}

export function useDeleteEvent() {
  const invalidate = useInvalidateEvents();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/events/{id}', {
        params: { path: { id } },
      });
      if (error) {
        throw new ApiError(response.status, error);
      }
    },
    onSuccess: (_, id) => invalidate(undefined, id),
  });
}

export type EventSeriesEditResult = components['schemas']['EventSeriesEditResultDto'];
export type EventSeriesDeleteResult = components['schemas']['EventSeriesDeleteResultDto'];

/**
 * Applies one edit to this occurrence of a repeating event and to every later one of its series.
 *
 * The precondition is set here by hand rather than left to the replay that threads it onto an
 * ordinary update. That replay is keyed by the path a version was read under, and this write is
 * addressed to a sub-path of the event rather than to the event itself, so nothing would be sent
 * and a server that requires one would refuse every edit. It is honestly a precondition over the
 * occurrence on the screen alone — one token cannot speak for a set — and it is still worth
 * carrying: it says the evening the author was looking at has not moved underneath them.
 */
export function useEditEventSeriesFollowing() {
  const invalidate = useInvalidateEvents();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: EventWrite }) => {
      const etag = lastReadETag(`/api/v1/events/${id}`);
      return unwrap(
        api.PUT('/api/v1/events/{id}/series/following', {
          params: { path: { id } },
          headers: etag ? { 'If-Match': etag } : undefined,
          body,
        }),
      );
    },
    // Every occurrence of the series is an ordinary event on its own page and its own row of the
    // calendar, so an edit reaching a dozen of them has moved a dozen things this cache holds.
    onSuccess: (_data, variables) => invalidate(variables.id),
  });
}

/**
 * Calls off this occurrence of a repeating event and every later one, keeping any that has
 * already begun.
 *
 * It answers with both halves — what went and what stayed — because the half that stays is the
 * surprising one: an occurrence that has already happened is the record of an evening and of who
 * said they would come, and is never removed by an act aimed at the rest of the run.
 */
export function useDeleteEventSeriesFollowing() {
  const invalidate = useInvalidateEvents();
  return useMutation({
    mutationFn: async (id: string) => {
      const { data, error, response } = await api.DELETE('/api/v1/events/{id}/series/following', {
        params: { path: { id } },
      });
      if (error) {
        throw new ApiError(response.status, error);
      }
      return data as EventSeriesDeleteResult;
    },
    // The occurrence it was asked from is the first of the ones that went.
    onSuccess: (_, id) => invalidate(undefined, id),
  });
}

/**
 * Moves an event to another lifecycle state — one endpoint naming the state to move to rather
 * than a verb per move.
 *
 * The server requires the version the caller was looking at, so the move carries the token the
 * detail read captured: two people announcing and un-announcing the same evening otherwise land
 * in whichever order the database happens to see. Which moves are legal from which state is the
 * server's to decide; the control only offers the ones a reader would expect, and one the table
 * refuses comes back as a conflict rather than being prevented here.
 */
export function useMoveEvent() {
  const invalidate = useInvalidateEvents();
  return useMutation({
    mutationFn: ({ id, state }: { id: string; state: ActivityState }) => {
      const etag = lastReadETag(`/api/v1/events/${id}`);
      return unwrap(
        api.POST('/api/v1/events/{id}/state', {
          params: { path: { id } },
          headers: etag ? { 'If-Match': etag } : undefined,
          body: { state },
        }),
      );
    },
    // As on the event's own update: a move is checked against the version last read, so the move
    // is not finished until the version it produced has been read.
    onSuccess: (_data, variables) => invalidate(variables.id),
  });
}
