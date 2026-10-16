// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, ApiError } from '../client.ts';
import type { components } from '../schema';
import { unwrap, unwrapVoid } from './shared.ts';
import type { DataExport, MeLocaleWrite, MeUpdate, UserAddressWrite } from './types.ts';
import { queryKeys } from './queryKeys.ts';

export function useMe() {
  return useQuery({
    queryKey: queryKeys.me,
    queryFn: () => unwrap(api.GET('/api/v1/me')),
    // The avatar URL carries a short-lived delivery token, so a long-cached response would
    // start 403ing in the header. Same cadence the attachment gallery refreshes on.
    staleTime: 5 * 60_000,
    refetchInterval: 8 * 60_000,
  });
}

/** Everything the settings pages write invalidates the one profile response they all read. */
function useInvalidateMe() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['me'] });
}

export function useUpdateProfile() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (body: MeUpdate) => unwrap(api.PUT('/api/v1/me', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateLocale() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (body: MeLocaleWrite) => unwrap(api.PUT('/api/v1/me/locale', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useAvatarPresets() {
  return useQuery({
    queryKey: queryKeys.avatarPresets,
    queryFn: () => unwrap(api.GET('/api/v1/avatar-presets')),
    staleTime: Infinity, // ships with the build
  });
}

export function useUploadAvatar() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (file: File) => {
      const form = new FormData();
      form.append('file', file, file.name);
      return unwrap(api.POST('/api/v1/me/avatar', {
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
    onSuccess: () => invalidate(),
  });
}

export function useSetAvatarPreset() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (preset: string) => unwrap(api.PUT('/api/v1/me/avatar', { body: { preset } })),
    onSuccess: () => invalidate(),
  });
}

export function useRemoveAvatar() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: () => unwrapVoid(api.DELETE('/api/v1/me/avatar')),
    onSuccess: () => invalidate(),
  });
}

export function useCreateAddress() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (body: UserAddressWrite) => unwrap(api.POST('/api/v1/me/addresses', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateAddress() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UserAddressWrite }) =>
      unwrap(api.PUT('/api/v1/me/addresses/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteAddress() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/me/addresses/{id}', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

export function useRequestEmailChange() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (newEmail: string) => unwrapVoid(api.POST('/api/v1/me/email/change', { body: { newEmail } })),
    onSuccess: () => invalidate(),
  });
}

export function useConfirmEmailChange() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (token: string) => unwrap(api.POST('/api/v1/me/email/confirm', { body: { token } })),
    onSuccess: () => invalidate(),
  });
}

export function useResendEmailChange() {
  return useMutation({ mutationFn: () => unwrapVoid(api.POST('/api/v1/me/email/resend')) });
}

export function useCancelEmailChange() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: () => unwrapVoid(api.DELETE('/api/v1/me/email/pending')),
    onSuccess: () => invalidate(),
  });
}

export function useVerifyEmail() {
  return useMutation({ mutationFn: () => unwrapVoid(api.POST('/api/v1/me/email/verify')) });
}

export function useChangeUsername() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (username: string) => unwrap(api.PUT('/api/v1/me/username', { body: { username } })),
    onSuccess: () => invalidate(),
  });
}

export function useChangePassword() {
  return useMutation({
    mutationFn: (body: { currentPassword: string; newPassword: string }) =>
      unwrapVoid(api.PUT('/api/v1/me/password', { body })),
  });
}

/**
 * The installation's starting arrangement. A default only: the client fills gaps with it and never
 * lets it overwrite something the person chose.
 */
export function useUiDefaults() {
  return useQuery({
    queryKey: queryKeys.uiDefaults,
    queryFn: () => unwrap(api.GET('/api/v1/ui-defaults')),
    // It changes when an administrator publishes a new one, which is rare; asking again on every
    // mount would be a request per page load for an answer that is the same all day.
    staleTime: 10 * 60_000,
  });
}

export function useUiPreferences() {
  return useQuery({
    queryKey: queryKeys.uiPreferences,
    queryFn: () => unwrap(api.GET('/api/v1/me/preferences')),
  });
}

export function useUpdateUiPreferences() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (preferences: Record<string, unknown>) =>
      unwrap(api.PUT('/api/v1/me/preferences', { body: { preferences } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['me'] }),
  });
}

/**
 * The caller's newest account-data export. Polls only while one is being built, the same
 * settle pattern the geodata pages use; a missing export is a normal empty state, not an error.
 */
export function useDataExport() {
  return useQuery({
    queryKey: queryKeys.dataExport,
    queryFn: async (): Promise<DataExport | null> => {
      const { data, error, response } = await api.GET('/api/v1/me/data-export');
      if (response.status === 404) {
        return null;
      }
      if (error !== undefined || data === undefined) {
        const problem = error as { code?: string; detail?: string } | undefined;
        throw new ApiError(response.status, problem?.code, problem?.detail);
      }
      return data;
    },
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status === 'queued' || status === 'running' ? 2000 : false;
    },
  });
}

export function useRequestDataExport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => unwrap(api.POST('/api/v1/me/data-export')),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['me', 'data-export'] }),
  });
}

/**
 * The caller's calendar feed addresses, and whether the installation offers feeds at all. The
 * second rides on the first because the settings page draws the section only where a mint can
 * work: an installation with feeds switched off shows nothing rather than a button that refuses.
 */
export function useCalendarFeeds() {
  return useQuery({
    queryKey: queryKeys.calendarFeeds,
    queryFn: () => unwrap(api.GET('/api/v1/me/calendar-feeds')),
  });
}

/**
 * Mints a subscription address for the caller's own calendar. The answer carries the whole
 * address exactly once — the server keeps only a hash of it — so the page that calls this is
 * the one place it can ever be read, and must show it before the next render loses it.
 */
export function useMintCalendarFeed() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (label: string | null) =>
      unwrap(api.POST('/api/v1/me/calendar-feeds', { body: { label } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.calendarFeeds }),
  });
}

/** Withdraws one address; a calendar still polling it is answered as though it never existed. */
export function useRevokeCalendarFeed() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/me/calendar-feeds/{id}', { params: { path: { id } } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.calendarFeeds }),
  });
}

export type MyPermissionGroup = components['schemas']['MyPermissionGroupDto'];

/** The permission groups the caller reaches — where a right of theirs comes from. */
export function useMyPermissionGroups() {
  return useQuery({
    queryKey: queryKeys.myPermissionGroups,
    queryFn: () => unwrap(api.GET('/api/v1/me/permission-groups')),
    staleTime: 5 * 60_000,
  });
}

export function useMfaStatus() {
  return useQuery({
    queryKey: queryKeys.mfa,
    queryFn: () => unwrap(api.GET('/api/v1/me/mfa')),
  });
}

export function usePhoneStatus() {
  return useQuery({
    queryKey: queryKeys.phone,
    queryFn: () => unwrap(api.GET('/api/v1/me/phone')),
  });
}
