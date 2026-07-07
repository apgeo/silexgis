// SPDX-License-Identifier: AGPL-3.0-or-later
import { useQuery } from '@tanstack/react-query';
import { api } from './client.ts';

// Query keys live here so invalidation stays precise (04-frontend-spec.md §8).
export const queryKeys = {
  me: ['me'] as const,
};

export function useMe() {
  return useQuery({
    queryKey: queryKeys.me,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/v1/me');
      if (error !== undefined) {
        throw new Error('Failed to load profile');
      }
      return data;
    },
  });
}
