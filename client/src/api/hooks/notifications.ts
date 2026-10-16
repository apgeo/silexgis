// SPDX-License-Identifier: AGPL-3.0-or-later
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  defaultInboxTransport,
  inboxPollIntervalMs,
  isInboxTransport,
} from '../../notifications/transport.ts';
import { api } from '../client.ts';
import { unwrap, unwrapVoid } from './shared.ts';
import type {
  NotificationCategory,
  NotificationChannelName,
  NotificationChoice,
  NotificationListParams,
} from './types.ts';
import { queryKeys } from './queryKeys.ts';

/** Every channel, in the order a settings page reads best: the one that always works first. */
export const NOTIFICATION_CHANNELS: readonly NotificationChannelName[] = ['inApp', 'email', 'sms'];

export function useNotificationPreferences() {
  return useQuery({
    queryKey: queryKeys.notificationPrefs,
    queryFn: () => unwrap(api.GET('/api/v1/me/notifications')),
  });
}

export function useUpdateNotificationPreferences() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: {
      categories: {
        category: NotificationCategory['category'];
        channels: { channel: NotificationChannelName; choice: NotificationChoice }[];
      }[];
    }) => unwrap(api.PUT('/api/v1/me/notifications', { body })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['me'] }),
  });
}

/** The reader's own inbox, newest first. Nobody else's is reachable through it. */
export function useNotifications(params: NotificationListParams) {
  return useQuery({
    queryKey: queryKeys.notificationInbox(params),
    queryFn: () => unwrap(api.GET('/api/v1/notifications', { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

/**
 * Everything a read changes: the line's own unread mark, the page it sits on, and the count the
 * header shows. One prefix covers all three because they share a query root.
 */
function useInvalidateNotificationInbox() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['notification-inbox'] });
}

/**
 * Marks one line read. Idempotent on the server, which does not move the stamp a second time, so
 * a row that is already read can be marked again without rewriting when it was first seen.
 */
export function useMarkNotificationRead() {
  const invalidate = useInvalidateNotificationInbox();
  return useMutation({
    mutationFn: (id: number) =>
      unwrapVoid(api.POST('/api/v1/notifications/{id}/read', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

/** Marks everything the reader has not read yet, in one act. */
export function useMarkAllNotificationsRead() {
  const invalidate = useInvalidateNotificationInbox();
  return useMutation({
    mutationFn: () => unwrapVoid(api.POST('/api/v1/notifications/read-all')),
    onSuccess: () => invalidate(),
  });
}

/**
 * How many lines the reader has not opened yet, for the count the header shows.
 *
 * Its own request rather than a number read off the list, because the header is on every page and
 * the list is on one: asking for the count costs one small answer, while asking for the first page
 * of the inbox everywhere would fetch and re-render rows nobody is looking at.
 *
 * Kept current three ways, and the timer is the weakest of them. Both ways of marking something
 * read invalidate the inbox root this key sits under, so the number moves as the reader acts
 * rather than up to a minute later; returning to the tab refetches, which is what covers a browser
 * that throttles timers in a background tab; and the interval itself only has to cover a
 * notification arriving while somebody is watching a page that is not the inbox. `staleTime` is
 * left at zero for the second of those: a query still considered fresh is not refetched on focus.
 *
 * The timer is the one of the three the installation chooses: it runs while the configured
 * transport is polling, and stops when the server says it will push instead. The other two hold
 * whatever the transport is.
 */
export function useUnreadNotificationCount() {
  const transport = useInboxTransport();
  return useQuery({
    queryKey: queryKeys.unreadNotificationCount,
    queryFn: () => unwrap(api.GET('/api/v1/notifications/unread-count')),
    refetchInterval: transport === 'poll' ? inboxPollIntervalMs : false,
    refetchOnWindowFocus: true,
  });
}

/**
 * Which transport this installation has been configured for.
 *
 * The choice belongs to whoever runs the server, not to this client, so it is asked for rather
 * than compiled in. Until the answer arrives — and if it never does, because the request failed —
 * the header polls: that is the transport which needs nothing else in place, so a count is never
 * left with nothing to move it.
 *
 * It changes when an operator restarts the server with a different setting, so it is asked for
 * once and then left alone rather than re-fetched on every mount of the header.
 */
export function useInboxTransport() {
  const { data } = useQuery({
    queryKey: queryKeys.notificationTransport,
    queryFn: () => unwrap(api.GET('/api/v1/notifications/config')),
    staleTime: Infinity,
  });
  const named = data?.badgeTransport;
  return isInboxTransport(named) ? named : defaultInboxTransport;
}
