// SPDX-License-Identifier: AGPL-3.0-or-later
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { unwrap, unwrapVoid } from './shared.ts';
import { queryKeys } from './queryKeys.ts';

// ---- account administration: the register of who can sign in ----

export type AdminUser = components['schemas']['AdminUserDto'];
export type AdminUserDetail = components['schemas']['AdminUserDetailDto'];
export type AdminUserCreated = components['schemas']['AdminUserCreatedDto'];

export interface AdminUserListParams {
  q?: string;
  page: number;
  pageSize: number;
}

/** The register of accounts, searched and read a page at a time. */
export function useAdminUsers(params: AdminUserListParams) {
  return useQuery({
    queryKey: ['admin-users', 'list', params] as const,
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/users', {
          params: { query: { q: params.q || undefined, page: params.page, pageSize: params.pageSize } },
        }),
      ),
    // The page before stays on screen while the next is read, so typing into the search box does
    // not blank the list between keystrokes.
    placeholderData: keepPreviousData,
  });
}

/**
 * What changing an account changes in this browser: the register, and — because an account is
 * also somebody on the roster — the people pickers that read it.
 */
function useInvalidateAdminUsers() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['admin-users'] });
    void queryClient.invalidateQueries({ queryKey: ['user-search'] });
  };
}

/** Opens an account for somebody and invites them to choose a password. */
export function useCreateAdminUser() {
  const invalidate = useInvalidateAdminUsers();
  return useMutation({
    mutationFn: (body: { email: string; displayName: string }) =>
      unwrap(api.POST('/api/v1/users', { body })),
    onSuccess: () => invalidate(),
  });
}

/** Sends a fresh invitation to an account nobody has entered yet. */
export function useReinviteAdminUser() {
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/users/{id}/invitation', { params: { path: { id } } })),
  });
}

/** Shuts an account out, or lets it back in. */
export function useSetAdminUserLocked() {
  const invalidate = useInvalidateAdminUsers();
  return useMutation({
    mutationFn: ({ id, locked }: { id: string; locked: boolean }) =>
      unwrap(api.PUT('/api/v1/users/{id}', { params: { path: { id } }, body: { locked } })),
    onSuccess: () => invalidate(),
  });
}

/**
 * Marks an account so that everything it uploads waits for a reviewer, or clears the mark.
 * The review queue is not refreshed: marking changes what the account's future uploads do, not
 * what already waits.
 */
export function useSetAdminUserUploadReview() {
  const invalidate = useInvalidateAdminUsers();
  return useMutation({
    mutationFn: ({ id, uploadsNeedReview }: { id: string; uploadsNeedReview: boolean }) =>
      unwrap(api.PUT('/api/v1/users/{id}/upload-review', { params: { path: { id } }, body: { uploadsNeedReview } })),
    onSuccess: () => invalidate(),
  });
}

/** Removes an account. The person it belonged to stays on the roster. */
export function useDeleteAdminUser() {
  const invalidate = useInvalidateAdminUsers();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/users/{id}', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

/** One account with the permission groups it belongs to, read when somebody opens it. */
export function useAdminUser(id: string) {
  return useQuery({
    queryKey: ['admin-users', 'one', id] as const,
    queryFn: () => unwrap(api.GET('/api/v1/users/{id}', { params: { path: { id } } })),
  });
}

/** Switches every second factor of somebody else's account off and signs them out everywhere. */
export function useResetAdminUserTwoFactor() {
  const invalidate = useInvalidateAdminUsers();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/users/{id}/two-factor/reset', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

/** Admin-only; the query is left disabled for anyone else so no 403 is provoked. */
export function useAdminSettings(enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.adminSettings,
    queryFn: () => unwrap(api.GET('/api/v1/admin/settings')),
    enabled,
  });
}

export function useMessageTemplates(enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.messageTemplates,
    queryFn: () => unwrap(api.GET('/api/v1/admin/message-templates')),
    enabled,
  });
}
