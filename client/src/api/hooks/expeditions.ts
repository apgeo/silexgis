// SPDX-License-Identifier: AGPL-3.0-or-later
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, ApiError, lastReadETag } from '../client.ts';
import type { components } from '../schema';
import {
  invalidateAfterDelete,
  unwrap,
  unwrapVoid,
  useInvalidateAttachments,
  useInvalidateHistory,
} from './shared.ts';
import type { ActivityState, ExpeditionListParams } from './types.ts';
import { queryKeys } from './queryKeys.ts';

// ---------------------------------------------------------------------------
// Expeditions
// ---------------------------------------------------------------------------

export type ExpeditionInfo = components['schemas']['ExpeditionDto'];

/** Camps, most recent first, narrowed by the filters the list offers. */
export function useExpeditions(params: ExpeditionListParams = {}) {
  return useQuery({
    queryKey: queryKeys.expeditions(params),
    queryFn: () => unwrap(api.GET('/api/v1/expeditions', { params: { query: params } })),
    // Paging or retyping a filter keeps the rows on screen while the next answer arrives, rather
    // than emptying the table under whoever is reading it.
    placeholderData: keepPreviousData,
  });
}

/**
 * One camp. A camp the caller may not read answers exactly as one that does not exist does —
 * the server spells both `expedition.not_found` — so the page has no way to tell them apart and
 * must not try: an address that answered differently for the two would be an address anybody
 * could probe for the existence of a camp they cannot see.
 */
export function useExpedition(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.expedition(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/expeditions/{id}', { params: { path: { id: id! } } })),
    enabled: !!id,
    // A refusal here is a settled answer about the caller, not a transient failure: retrying it
    // three times only delays the page saying so.
    retry: false,
  });
}

export type ExpeditionWrite = components['schemas']['ExpeditionWriteRequest'];

function useInvalidateExpeditions() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: ['expeditions'] });
}

/**
 * Hands the next write on a camp the version this one produced.
 *
 * A write is checked against the version the caller last read, and only a read records one, so
 * a second save a second after the first would be refused unless the camp is read back in
 * between. Returned rather than fired and forgotten, so the mutation resolves after the re-read
 * and the surface that waits for it waits for the right thing.
 */
function useReadExpeditionsBack() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: ['expeditions'] });
}

export function useCreateExpedition() {
  const invalidate = useInvalidateExpeditions();
  return useMutation({
    mutationFn: (body: ExpeditionWrite) => unwrap(api.POST('/api/v1/expeditions', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateExpedition() {
  const readBack = useReadExpeditionsBack();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: ExpeditionWrite }) =>
      unwrap(api.PUT('/api/v1/expeditions/{id}', { params: { path: { id } }, body })),
    onSuccess: () => {
      invalidateHistory();
      return readBack();
    },
  });
}

export function useDeleteExpedition() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/expeditions/{id}', { params: { path: { id } } })),
    onSuccess: (_, id) => invalidateAfterDelete(queryClient, ['expeditions'], id),
  });
}

/**
 * Moving a camp to another lifecycle state, through the one route that names the state it
 * moves to rather than a verb per move — a camp has eight states and two dozen moves between
 * them, and two dozen routes is not a surface.
 *
 * It is a write on the camp and is checked against the version the caller was looking at, so it
 * carries the precondition the camp's own read captured. Which moves are legal from which state
 * is the server's to decide; a move the rules refuse comes back as a conflict.
 */
export function useMoveExpedition() {
  const readBack = useReadExpeditionsBack();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ id, state }: { id: string; state: ActivityState }) => {
      const etag = lastReadETag(`/api/v1/expeditions/${id}`);
      return unwrap(
        api.POST('/api/v1/expeditions/{id}/state', {
          params: { path: { id } },
          headers: etag ? { 'If-Match': etag } : undefined,
          body: { state },
        }),
      );
    },
    onSuccess: () => {
      invalidateHistory();
      // As on a full update: the move is checked against the version last read, so it is not
      // finished until the version it produced has been read.
      return readBack();
    },
  });
}

export type ExpeditionSharing = components['schemas']['ExpeditionSharingDto'];
export type ExpeditionSharedRule = components['schemas']['ExpeditionSharedRuleDto'];
export type ExpeditionShareEntry = components['schemas']['ExpeditionShareEntryWrite'];
/**
 * What applying a camp's sharing did: the sharing as it now stands, how many trips took it, the
 * skipped trips the caller may read, and a bare count of the skipped ones they may not.
 */
export type ExpeditionSharingOutcome = components['schemas']['ExpeditionSharingOutcomeDto'];
export type ExpeditionSkippedTrip = components['schemas']['ExpeditionSkippedTripDto'];

/**
 * What a camp's sharing grants, and how many of its trips carry it.
 *
 * Sharing a camp is not a rule on the camp: it is one object-scoped rule written onto each trip
 * the camp gathers, marked with the camp that wrote it, so a partner club given the camp can read
 * what the camp gathered. The read takes the right to manage the camp's permissions, and a
 * refusal is a settled answer about the caller rather than something to retry.
 */
export function useExpeditionSharing(expeditionId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.expeditionSharing(expeditionId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/expeditions/{id}/sharing', { params: { path: { id: expeditionId! } } })),
    enabled: enabled && !!expeditionId,
    retry: false,
  });
}

function useInvalidateExpeditionSharing(expeditionId: string) {
  const queryClient = useQueryClient();
  const invalidateHistory = useInvalidateHistory();
  return () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.expeditionSharing(expeditionId) });
    // The rules land on the trips, where each trip's own permissions dialog lists them.
    void queryClient.invalidateQueries({ queryKey: ['object-access'] });
    void queryClient.invalidateQueries({ queryKey: ['effective-access'] });
    invalidateHistory();
  };
}

/**
 * Shares the camp: adds and restates one rule per member trip the caller may administer, and
 * never removes. The answer says which trips it skipped; a sharing that reached none is refused.
 */
export function useApplyExpeditionSharing(expeditionId: string) {
  const invalidate = useInvalidateExpeditionSharing(expeditionId);
  return useMutation({
    mutationFn: (entries: ExpeditionShareEntry[]) =>
      unwrap(
        api.POST('/api/v1/expeditions/{id}/sharing', {
          params: { path: { id: expeditionId } },
          body: { entries },
        }),
      ),
    onSuccess: invalidate,
  });
}

/**
 * Carries the camp's sharing onto the trips it does not reach yet: ones that joined since it
 * was applied, and ones skipped then whose owners have since delegated. A trip joining is not
 * covered by itself: coverage is an act somebody performs and the trail records.
 */
export function useReapplyExpeditionSharing(expeditionId: string) {
  const invalidate = useInvalidateExpeditionSharing(expeditionId);
  return useMutation({
    mutationFn: () =>
      unwrap(
        api.POST('/api/v1/expeditions/{id}/sharing/re-apply', {
          params: { path: { id: expeditionId } },
        }),
      ),
    onSuccess: invalidate,
  });
}

/** Withdraws every rule this camp's sharing wrote, and only those. */
export function useWithdrawExpeditionSharing(expeditionId: string) {
  const invalidate = useInvalidateExpeditionSharing(expeditionId);
  return useMutation({
    mutationFn: () =>
      unwrapVoid(
        api.DELETE('/api/v1/expeditions/{id}/sharing', { params: { path: { id: expeditionId } } }),
      ),
    onSuccess: invalidate,
  });
}

export type ExpeditionReportSaved = components['schemas']['ExpeditionReportSavedDto'];

/**
 * Files the camp's write-up against the camp. What is filed is built from the reading any
 * account has, not the filer's own: a file on a camp is reachable by everybody who may read the
 * camp, which is routinely wider than the trips gathered into it.
 */
export function useKeepExpeditionReport() {
  const invalidateAttachments = useInvalidateAttachments();
  return useMutation({
    mutationFn: ({ id, templateId }: { id: string; templateId?: string }) =>
      unwrap(
        api.POST('/api/v1/expeditions/{id}/report', {
          params: { path: { id }, query: templateId ? { templateId } : {} },
        }),
      ),
    onSuccess: () => invalidateAttachments(),
  });
}

export type ExpeditionRoster = components['schemas']['ExpeditionRosterDto'];
export type ExpeditionRosterEntry = components['schemas']['ExpeditionRosterEntryDto'];
export type ExpeditionRosterRole = components['schemas']['ExpeditionRosterRoleDto'];

/** What somebody may be recorded as having been at a camp as. Seeded rows plus an installation's own. */
export function useExpeditionRosterRoles() {
  return useQuery({
    queryKey: queryKeys.taxonomy('expedition-roster-roles'),
    queryFn: () => unwrap(api.GET('/api/v1/expedition-roster-roles')),
    staleTime: 5 * 60_000,
  });
}

/** A camp-roster role as an administrator authors it. */
export interface ExpeditionRosterRoleWrite {
  code: string;
  name: string;
  description: string | null;
  sortOrder: number;
}

function useInvalidateExpeditionRosterRoles() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.taxonomy('expedition-roster-roles') });
    // Every roster row renders its role from this list, so a renamed or removed row leaves the
    // camps already in cache showing wording that no longer exists.
    void queryClient.invalidateQueries({ queryKey: ['expeditions'] });
  };
}

export function useCreateExpeditionRosterRole() {
  const invalidate = useInvalidateExpeditionRosterRoles();
  return useMutation({
    mutationFn: (body: ExpeditionRosterRoleWrite) =>
      unwrap(api.POST('/api/v1/expedition-roster-roles', { body })),
    onSuccess: invalidate,
  });
}

export function useUpdateExpeditionRosterRole() {
  const invalidate = useInvalidateExpeditionRosterRoles();
  return useMutation({
    mutationFn: ({ id, ...body }: ExpeditionRosterRoleWrite & { id: number }) =>
      unwrap(api.PUT('/api/v1/expedition-roster-roles/{id}', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useDeleteExpeditionRosterRole() {
  const invalidate = useInvalidateExpeditionRosterRoles();
  return useMutation({
    mutationFn: (id: number) =>
      unwrapVoid(api.DELETE('/api/v1/expedition-roster-roles/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  });
}

/**
 * Who was at a camp, and for which days.
 *
 * Two rights, not one: the right to read the camp *and* the right to read people. A caller
 * holding the first and not the second is refused outright, with a code of its own, rather than
 * being handed rows with the names struck out — a struck-out list still says how many people were
 * there and when. That refusal is a designed answer and the surface showing it renders it as a
 * state of the page, which is why it must not be retried: the identical request cannot produce
 * anything else, and three attempts only hold the screen in its loading state meanwhile.
 */
export function useExpeditionRoster(expeditionId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.expeditionRoster(expeditionId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/expeditions/{expeditionId}/roster', {
          params: { path: { expeditionId: expeditionId! } },
        }),
      ),
    enabled: !!expeditionId,
    retry: false,
  });
}

/**
 * One stay as it is written: who, as what, and for which days.
 *
 * The person is named in exactly one of two ways — `caverId` for somebody picked out of the
 * directory, `newCaverName` for a name typed in — and the server refuses a row carrying both or
 * neither. A typed name is not a new person by being typed: the server reads it against the
 * directory, means whoever is already recorded under exactly it, and adds somebody only when
 * nobody is. That is the rule a trip's people are named by, and it lives on the server only.
 */
export type ExpeditionRosterEntryWrite = components['schemas']['ExpeditionRosterEntryWriteRequest'];

/**
 * What every write on a camp's roster leaves stale.
 *
 * The roster itself, read again rather than patched: the head count beside the rows is worked
 * out on the server, distinctly by person, and a row added locally would leave it wrong by
 * exactly the mistake that count exists to prevent. The camp's timeline, because a stay is a
 * change to the camp's own record and is written there. And the directory of people, because a
 * stay that names somebody by a name nobody held has just added them to it — a picker opened a
 * moment later must offer the person the last save made, or the next stay types them in again.
 */
function useExpeditionRosterWritten(expeditionId: string) {
  const queryClient = useQueryClient();
  const invalidateHistory = useInvalidateHistory();
  return () => {
    invalidateHistory();
    void queryClient.invalidateQueries({ queryKey: queryKeys.cavers });
    // Returned, so the write is not finished until the rows it changed have been read back and
    // a dialog closing on it closes over the list it expects to see.
    return queryClient.invalidateQueries({ queryKey: queryKeys.expeditionRoster(expeditionId) });
  };
}

/**
 * What a refused write on a camp's roster says about what is held here.
 *
 * One refusal is a statement about the cache rather than about the request: the entry a stay
 * named is not in the directory any more. It was removed after the list of people was read, or
 * — the only way it can go while a stay still names it — it was joined into another entry for
 * the same person, and the stays it held moved to the one that was kept. Either way what is
 * held here is behind: the list of people still offers the entry that is gone, and the roster
 * may still show stays under it. Both are read again, so the next thing somebody presses is
 * not the same dead entry.
 */
function useExpeditionRosterRefused(expeditionId: string) {
  const queryClient = useQueryClient();
  return (error: unknown) => {
    if (error instanceof ApiError && error.code === 'expedition_roster.caver_unknown') {
      void queryClient.invalidateQueries({ queryKey: queryKeys.cavers });
      void queryClient.invalidateQueries({ queryKey: queryKeys.expeditionRoster(expeditionId) });
    }
  };
}

/** Records that somebody was at a camp for a stretch of days. Takes the right to write the camp. */
export function useCreateExpeditionRosterEntry(expeditionId: string) {
  const written = useExpeditionRosterWritten(expeditionId);
  const refused = useExpeditionRosterRefused(expeditionId);
  return useMutation({
    mutationFn: (body: ExpeditionRosterEntryWrite) =>
      unwrap(
        api.POST('/api/v1/expeditions/{expeditionId}/roster', {
          params: { path: { expeditionId } },
          body,
        }),
      ),
    onSuccess: () => written(),
    onError: refused,
  });
}

/**
 * Rewrites one stay whole. No version travels with it: a stay is not versioned against its camp,
 * so correcting the day somebody arrived is never refused for want of a camp read in between.
 */
export function useUpdateExpeditionRosterEntry(expeditionId: string) {
  const written = useExpeditionRosterWritten(expeditionId);
  const refused = useExpeditionRosterRefused(expeditionId);
  return useMutation({
    mutationFn: ({ entryId, body }: { entryId: number; body: ExpeditionRosterEntryWrite }) =>
      unwrap(
        api.PUT('/api/v1/expeditions/{expeditionId}/roster/{entryId}', {
          params: { path: { expeditionId, entryId } },
          body,
        }),
      ),
    onSuccess: () => written(),
    onError: refused,
  });
}

/** Removes one recorded stay. The person stays in the directory: a stay is not who somebody is. */
export function useDeleteExpeditionRosterEntry(expeditionId: string) {
  const written = useExpeditionRosterWritten(expeditionId);
  return useMutation({
    mutationFn: (entryId: number) =>
      unwrapVoid(
        api.DELETE('/api/v1/expeditions/{expeditionId}/roster/{entryId}', {
          params: { path: { expeditionId, entryId } },
        }),
      ),
    onSuccess: () => written(),
  });
}

/**
 * Everything one camp draws on a map, as one answer.
 *
 * Three kinds of shape come back together — the camp's working area, the sketches of the member
 * trips this caller may read, and the entrances of the caves those trips name whose positions
 * this caller may see — because they are governed by three different rules and only the server
 * can apply them. Nothing here filters what arrives: whatever reached the client was cleared to.
 *
 * Fetched only once the map tab is the one on screen, so opening a camp does not pay for a map
 * nobody looked at.
 */
export function useExpeditionMap(expeditionId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.expeditionMap(expeditionId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/expeditions/{id}/map', { params: { path: { id: expeditionId! } } })),
    enabled: !!expeditionId && enabled,
    retry: false,
  });
}

export type ExpeditionLeads = components['schemas']['ExpeditionLeadsDto'];
export type ExpeditionLeadGroup = components['schemas']['ExpeditionLeadGroupDto'];
export type ExpeditionLead = components['schemas']['ExpeditionLeadDto'];

/**
 * What a camp's trips left open, grouped by whether each way on is still going.
 *
 * Nothing here is derived on the client and nothing here is filtered on it: which leads are on
 * the board is decided per lead on the server, against the same rule any single place is judged
 * by, and a lead this caller may not place exactly never arrives. So the board is drawn from what
 * came back and the count beside it is the count of what came back — two readers of the same camp
 * see different boards, and the page says so rather than leaving the numbers to imply otherwise.
 */
export function useExpeditionLeads(expeditionId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.expeditionLeads(expeditionId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/expeditions/{id}/leads', { params: { path: { id: expeditionId! } } })),
    enabled: !!expeditionId,
    retry: false,
  });
}
