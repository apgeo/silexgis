// SPDX-License-Identifier: AGPL-3.0-or-later
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { unwrap } from './shared.ts';
import { queryKeys } from './queryKeys.ts';

export type PhoneUser = components['schemas']['PhoneUserDto'];
/** Who made the current link: the phone itself, an archive import, or somebody keeping the roster. */
export type PhoneUserLinkSource = components['schemas']['DevicePersonLinkSource'];

/**
 * The people who have used a phone with this installation, one page at a time.
 *
 * The server orders the whole list — unlinked first, then the most recently seen — so a page is a
 * slice of that order and the client never re-sorts it.
 */
export function usePhoneUsers(page: number, pageSize: number) {
  return useQuery({
    queryKey: [...queryKeys.phoneUsers, page, pageSize],
    queryFn: () =>
      unwrap(api.GET('/api/v1/phone-users', { params: { query: { page, pageSize } } })),
    placeholderData: keepPreviousData,
  });
}

/**
 * Says which caver a phone user is. The server records it as made by a roster keeper, which
 * outranks anything a phone or an archive import says afterwards.
 */
export function useLinkPhoneUser() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ deviceUserId, caverId }: { deviceUserId: string; caverId: string }) =>
      unwrap(
        api.PUT('/api/v1/phone-users/{deviceUserId}/link', {
          params: { path: { deviceUserId } },
          body: { caverId },
        }),
      ),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.phoneUsers }),
  });
}

/** Takes a link away. The phone user stays listed, as one nobody has said anything about yet. */
export function useUnlinkPhoneUser() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (deviceUserId: string) =>
      unwrap(
        api.DELETE('/api/v1/phone-users/{deviceUserId}/link', {
          params: { path: { deviceUserId } },
        }),
      ),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.phoneUsers }),
  });
}
