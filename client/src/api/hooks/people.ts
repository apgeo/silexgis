// SPDX-License-Identifier: AGPL-3.0-or-later
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { invalidateAfterDelete, unwrap, unwrapVoid } from './shared.ts';
import type { MemberListParams } from './types.ts';
import { queryKeys } from './queryKeys.ts';

export function useMembers(params: MemberListParams) {
  return useQuery({
    queryKey: queryKeys.members(params),
    queryFn: () => unwrap(api.GET('/api/v1/members', { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

export function useMember(id: string) {
  return useQuery({
    queryKey: queryKeys.member(id),
    queryFn: () => unwrap(api.GET('/api/v1/members/{id}', { params: { path: { id } } })),
    enabled: id.length > 0,
  });
}

export type CavingGroupInfo = components['schemas']['CavingGroupDto'];
export type CavingGroupMemberInfo = components['schemas']['CavingGroupMemberDto'];
export type CaverInfo = components['schemas']['CaverDto'];

export function useCavingGroups() {
  return useQuery({
    queryKey: queryKeys.cavingGroups,
    queryFn: () => unwrap(api.GET('/api/v1/caving-groups')),
    staleTime: 60_000,
  });
}

export function useCavingGroupMembers(cavingGroupId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.cavingGroupMembers(cavingGroupId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/caving-groups/{id}/members', { params: { path: { id: cavingGroupId! } } })),
    enabled: !!cavingGroupId,
  });
}

/**
 * What creating a caving group changes, which is more than the directory.
 *
 * Whoever creates a group joins it as its owner, and the group's starter rules reach them from
 * that moment: the right to add people to its roster, and the right to create content that
 * belongs to the group. So the creator's own capabilities are read again with the directory —
 * left to go stale by themselves, the create doors those rights open would stay shut for minutes
 * on the very account that has just earned them.
 */
function useInvalidateCavingGroups() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['cavingGroups'] });
    void queryClient.invalidateQueries({ queryKey: queryKeys.capabilities });
  };
}

/**
 * What a roster edit changes, which is more than the directory row.
 *
 * The list of clubs carries each one's member count, so it has to be refetched — but so does the
 * roster the edit was made in, and so does how many people an announcement to that club would
 * reach. Refetching only the directory leaves the drawer showing the roster as it was before the
 * edit that was just made in it.
 *
 * And membership is what a group's rules reach people through, so an edit to a roster can be an
 * edit to what the person making it may do: somebody who puts themselves on a roster, or takes
 * themselves off one, has just gained or lost the right to create that group's content. Their
 * own capabilities are read again rather than left to go stale, so a create door opens and shuts
 * with the membership instead of leading, for a few minutes, to a refusal.
 */
function useInvalidateCavingGroupRoster(cavingGroupId: string) {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['cavingGroups'] });
    void queryClient.invalidateQueries({ queryKey: queryKeys.cavingGroupMembers(cavingGroupId) });
    void queryClient.invalidateQueries({ queryKey: queryKeys.cavingGroupAudience(cavingGroupId) });
    void queryClient.invalidateQueries({ queryKey: queryKeys.capabilities });
  };
}

export function useCreateCavingGroup() {
  const invalidate = useInvalidateCavingGroups();
  return useMutation({
    mutationFn: (body: { name: string; type: CavingGroupInfo['type']; description: string | null; website: string | null }) =>
      unwrap(api.POST('/api/v1/caving-groups', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpsertCavingGroupMember(cavingGroupId: string) {
  const invalidate = useInvalidateCavingGroupRoster(cavingGroupId);
  return useMutation({
    mutationFn: (body: { caverId: string; role: CavingGroupMemberInfo['role'] }) =>
      unwrap(api.POST('/api/v1/caving-groups/{id}/members', { params: { path: { id: cavingGroupId } }, body })),
    onSuccess: () => invalidate(),
  });
}

/**
 * Puts one person in some caving groups and takes them out of others: one roster write per
 * group, from the person's side.
 *
 * A group's roster is written group by group, each asking the right to edit that group, so some
 * of a set may be refused while the rest go through. The refused ones are answered rather than
 * thrown: whoever asked is owed the names of the groups that did not change, not one failure
 * standing for a save that mostly happened. Somebody joined here joins as an ordinary member —
 * who runs a group is said on the group's own page.
 */
export function useChangeCaverCavingGroups() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ caverId, join, leave }: { caverId: string; join: string[]; leave: string[] }) => {
      const refused: string[] = [];
      for (const id of join) {
        const { error } = await api.POST('/api/v1/caving-groups/{id}/members', {
          params: { path: { id } },
          body: { caverId, role: 'member' },
        });
        if (error !== undefined) {
          refused.push(id);
        }
      }
      for (const id of leave) {
        const { error } = await api.DELETE('/api/v1/caving-groups/{id}/members/{caverId}', {
          params: { path: { id, caverId } },
        });
        if (error !== undefined) {
          refused.push(id);
        }
      }
      return { refused };
    },
    // Whatever was refused, the rest was written: every list that shows who is in what is stale.
    onSettled: (_data, _error, { join, leave }) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.cavers });
      void queryClient.invalidateQueries({ queryKey: queryKeys.cavingGroups });
      for (const id of [...join, ...leave]) {
        void queryClient.invalidateQueries({ queryKey: queryKeys.cavingGroupMembers(id) });
        void queryClient.invalidateQueries({ queryKey: queryKeys.cavingGroupAudience(id) });
      }
      void queryClient.invalidateQueries({ queryKey: queryKeys.capabilities });
    },
  });
}

/**
 * How many people an announcement to this caving group would reach, asked before one is written.
 *
 * Not the roster's size: the members with no account have nowhere to receive anything and the
 * person asking is never told their own announcement. It is the server's own count rather than
 * one this page works out, so what somebody is shown and what is sent cannot drift apart.
 *
 * Left disabled for anyone who may not write to the group, so no 403 is provoked by opening a
 * page: only the people the list already marks as able to announce ever ask.
 */
export function useCavingGroupAnnouncementAudience(cavingGroupId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.cavingGroupAudience(cavingGroupId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/caving-groups/{id}/announcements/audience', {
          params: { path: { id: cavingGroupId! } },
        }),
      ),
    enabled: !!cavingGroupId,
  });
}

export function useAnnounceToCavingGroup(cavingGroupId: string) {
  return useMutation({
    mutationFn: (body: { message: string }) =>
      unwrap(
        api.POST('/api/v1/caving-groups/{id}/announcements', {
          params: { path: { id: cavingGroupId } },
          body,
        }),
      ),
  });
}

export function useRemoveCavingGroupMember(cavingGroupId: string) {
  const invalidate = useInvalidateCavingGroupRoster(cavingGroupId);
  return useMutation({
    mutationFn: async (caverId: string) => {
      const { error, response } = await api.DELETE('/api/v1/caving-groups/{id}/members/{caverId}', {
        params: { path: { id: cavingGroupId, caverId } },
      });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

/**
 * The roster of people, optionally filtered by name; `unlinked` narrows to those with no account.
 *
 * `keepRows` is for a table that is narrowed while it is on screen: the rows of the previous
 * search stay until the next answer has come, so the table is never emptied in between — and
 * whatever stands open on one of its rows is not thrown away with it. A typeahead leaves it
 * off: the suggestions of the previous spelling are not suggestions for this one.
 */
export function useCavers(search?: string, unlinked?: boolean, options: { keepRows?: boolean } = {}) {
  return useQuery({
    queryKey: [...queryKeys.cavers, search ?? '', unlinked ?? false],
    queryFn: () => unwrap(api.GET('/api/v1/cavers', { params: { query: { search, unlinked } } })),
    staleTime: 30_000,
    placeholderData: options.keepRows ? keepPreviousData : undefined,
  });
}

export function useCaver(id: string | undefined) {
  return useQuery({
    queryKey: [...queryKeys.cavers, id],
    queryFn: () => unwrap(api.GET('/api/v1/cavers/{id}', { params: { path: { id: id! } } })),
    enabled: !!id,
  });
}

function useInvalidateCavers() {
  const queryClient = useQueryClient();
  /** `deleted` is the caver who has just been removed, when that is why this is being called. */
  return (deleted?: string) => {
    if (deleted) {
      void invalidateAfterDelete(queryClient, queryKeys.cavers, deleted);
    } else {
      void queryClient.invalidateQueries({ queryKey: queryKeys.cavers });
    }
    // A roster edit can change how a person is named on trips and group pages too.
    void queryClient.invalidateQueries({ queryKey: ['cavingGroups'] });
  };
}

export function useCreateCaver() {
  const invalidate = useInvalidateCavers();
  return useMutation({
    mutationFn: (body: { fullName: string; email: string | null; phone: string | null; notes: string | null }) =>
      unwrap(api.POST('/api/v1/cavers', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateCaver(id: string) {
  const invalidate = useInvalidateCavers();
  return useMutation({
    mutationFn: (body: { fullName: string; email: string | null; phone: string | null; notes: string | null }) =>
      unwrap(api.PUT('/api/v1/cavers/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteCaver() {
  const invalidate = useInvalidateCavers();
  return useMutation({
    // Through the shared unwrap, so that a refusal arrives with its code and its members: the
    // refusal over trips names the trips, and a screen can only show them if they get this far.
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/cavers/{id}', { params: { path: { id } } })),
    onSuccess: (_, id) => invalidate(id),
  });
}

export function useMergeCavers(id: string) {
  const invalidate = useInvalidateCavers();
  return useMutation({
    mutationFn: (sourceCaverId: string) =>
      unwrap(api.POST('/api/v1/cavers/{id}/merge', { params: { path: { id } }, body: { sourceCaverId } })),
    onSuccess: () => invalidate(),
  });
}

export function useLinkCaverAccount(id: string) {
  const invalidate = useInvalidateCavers();
  return useMutation({
    mutationFn: (userId: string) =>
      unwrap(api.POST('/api/v1/cavers/{id}/account-link', { params: { path: { id } }, body: { userId } })),
    onSuccess: () => invalidate(),
  });
}

export function useUnlinkCaverAccount(id: string) {
  const invalidate = useInvalidateCavers();
  return useMutation({
    mutationFn: async () => {
      const { error, response } = await api.DELETE('/api/v1/cavers/{id}/account-link', {
        params: { path: { id } },
      });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}
