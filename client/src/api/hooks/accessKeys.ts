// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { unwrap, unwrapVoid } from './shared.ts';
import { queryKeys } from './queryKeys.ts';

export type AccessKey = components['schemas']['AccessKeyDto'];
export type AccessKeyList = components['schemas']['AccessKeyListDto'];
export type AccessKeyCreated = components['schemas']['AccessKeyCreatedDto'];
export type AccessKeyCreateRequest = components['schemas']['AccessKeyCreateRequest'];
export type AccessKeyState = components['schemas']['AccessKeyState'];

/**
 * The lifetimes a new key may be given, and the one the form starts on.
 *
 * Read from the answer rather than written into the form: the installation sets a ceiling and the
 * server decides which of the usual lifetimes fit under it, so a form with 30, 90 and 365 baked in
 * would offer a choice the server then refuses. A lifetime above the ceiling is dropped even if
 * the server listed it, and the starting choice falls back to the longest one offered when the
 * stated default is not among them.
 */
export function accessKeyLifetimeChoices(list: Pick<AccessKeyList, 'maxDays' | 'offeredDays' | 'defaultDays'>): {
  days: number[];
  initial: number | undefined;
} {
  const days = [...new Set(list.offeredDays)].filter((d) => d > 0 && d <= list.maxDays).sort((a, b) => a - b);
  const initial = days.includes(list.defaultDays) ? list.defaultDays : days[days.length - 1];
  return { days, initial };
}

/** Whether a device holding this key is still let in: neither revoked nor past its expiry. */
export function accessKeyIsUsable(key: Pick<AccessKey, 'state'>): boolean {
  return key.state === 'active';
}

/**
 * The account's own keys, newest first, with the lifetimes a new key may have.
 *
 * Only a normal sign-in may ask: a key cannot list keys, so the web never meets the refusal a
 * device would.
 */
export function useAccessKeys() {
  return useQuery({
    queryKey: queryKeys.accessKeys,
    queryFn: () => unwrap(api.GET('/api/v1/me/access-keys')),
  });
}

/**
 * Makes a key. The answer carries the secret, and it is the only time the secret exists anywhere
 * the browser can read it, so it is returned to the caller to show once and is deliberately **not**
 * put in the query cache: the list is asked for again and answers descriptions only.
 */
export function useCreateAccessKey() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: AccessKeyCreateRequest) => unwrap(api.POST('/api/v1/me/access-keys', { body })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.accessKeys }),
  });
}

/** Revokes a key; the device holding it is refused on its next call. Asking twice is not an error. */
export function useRevokeAccessKey() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/me/access-keys/{id}', { params: { path: { id } } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.accessKeys }),
  });
}
