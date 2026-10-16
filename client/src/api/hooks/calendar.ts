// SPDX-License-Identifier: AGPL-3.0-or-later
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { unwrap } from './shared.ts';
import type { CalendarParams } from './types.ts';
import { queryKeys } from './queryKeys.ts';

export type CalendarEntry = components['schemas']['CalendarEntryDto'];
export type CalendarResult = components['schemas']['CalendarResultDto'];
export type CalendarSource = components['schemas']['CalendarSource'];
export type CalendarPlacement = components['schemas']['CalendarPlacement'];

/**
 * The dated records the reader may open whose days fall in one window.
 *
 * Its own key rather than a shape of any list's: it spans two families of row, so writing either
 * of them should re-read it, and it goes stale as days pass rather than only as people edit.
 */
export function useCalendar(params: CalendarParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.calendar(params),
    queryFn: () => unwrap(api.GET('/api/v1/calendar', { params: { query: params } })),
    enabled: options?.enabled ?? true,
    // Changing the window or a toggle keeps the rows on screen while the next answer arrives,
    // rather than emptying the record under whoever is reading it.
    placeholderData: keepPreviousData,
  });
}
