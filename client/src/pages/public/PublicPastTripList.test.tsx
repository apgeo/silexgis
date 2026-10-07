// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { PublicPastTrip } from '../../api/hooks.ts';
import PublicPastTripList from './PublicPastTripList.tsx';

/**
 * The archive of a cave, gathered by the camp each trip was part of.
 *
 * <b>What is proved here is the arrangement and nothing else about a row.</b> What a row says and
 * what pressing one does are proved through the pages that hold this list; this file is about a
 * club reading its own history by camp — and about the list being exactly the plain list it
 * always was wherever the server names no camp.
 */

const CAMP_2026 = { id: 'cccccccc-0000-0000-0000-000000000001', name: 'Summer camp' };
// The same name on purpose: two summers' camps are two camps.
const CAMP_2019 = { id: 'cccccccc-0000-0000-0000-000000000002', name: 'Summer camp' };

function past(
  tripLogId: string,
  title: string,
  expedition: PublicPastTrip['expedition'] = null,
): PublicPastTrip {
  return {
    tripLogId,
    expedition,
    title,
    tripDate: '2026-07-06',
    tripDateEnd: null,
    closedAt: '2026-07-06T18:00:00Z',
    participantCount: 3,
    playable: true,
  };
}

const list = (trips: PublicPastTrip[], onPlay: (tripLogId: string) => void = () => {}) =>
  render(
    <PublicPastTripList
      trips={trips}
      more={false}
      loading={false}
      failed={false}
      playingId={null}
      onPlay={onPlay}
    />,
  );

const rowsOf = (group: HTMLElement) =>
  within(group)
    .getAllByRole('button')
    .map((row) => row.getAttribute('data-testid'));

afterEach(cleanup);

describe('the past trips of a cave, gathered by camp', () => {
  it('puts each camp\'s trips under a heading of its own, and the trips of no camp last', () => {
    list([
      past('trip-a', 'First push', CAMP_2026),
      past('trip-b', 'A weekend visit'),
      past('trip-c', 'The old survey', CAMP_2019),
      past('trip-d', 'Second push', CAMP_2026),
    ]);

    const headings = screen.getAllByRole('heading').map((heading) => heading.textContent);
    expect(headings).toEqual(['Camp: Summer camp', 'Camp: Summer camp', 'Other trips of this cave']);

    expect(rowsOf(screen.getByTestId(`public-past-group-${CAMP_2026.id}`))).toEqual([
      'public-past-trip-trip-a',
      'public-past-trip-trip-d',
    ]);
    expect(rowsOf(screen.getByTestId(`public-past-group-${CAMP_2019.id}`))).toEqual([
      'public-past-trip-trip-c',
    ]);
    expect(rowsOf(screen.getByTestId('public-past-group-other'))).toEqual([
      'public-past-trip-trip-b',
    ]);
    // Each group is announced by its heading, not merely preceded by it.
    expect(screen.getByRole('region', { name: 'Other trips of this cave' })).toBe(
      screen.getByTestId('public-past-group-other'),
    );
  });

  it('has no "other trips" heading when every trip belongs to a camp', () => {
    list([past('trip-a', 'First push', CAMP_2026)]);

    expect(screen.getAllByRole('heading').map((heading) => heading.textContent)).toEqual([
      'Camp: Summer camp',
    ]);
    expect(screen.queryByTestId('public-past-group-other')).toBeNull();
  });

  it('is the plain list, with no heading at all, where the server names no camp', () => {
    list([past('trip-a', 'First push'), past('trip-b', 'A weekend visit')]);

    expect(screen.queryAllByRole('heading')).toHaveLength(0);
    expect(screen.queryByTestId('public-past-group-other')).toBeNull();
    // The twin: the trips themselves are all there, in the order they arrived.
    expect(rowsOf(screen.getByTestId('public-past-list'))).toEqual([
      'public-past-trip-trip-a',
      'public-past-trip-trip-b',
    ]);
  });

  it('still plays the row that was pressed, whichever group it stands in', () => {
    const onPlay = vi.fn();
    list([past('trip-a', 'First push', CAMP_2026), past('trip-b', 'A weekend visit')], onPlay);

    fireEvent.click(screen.getByTestId('public-past-trip-trip-b'));

    expect(onPlay).toHaveBeenCalledExactlyOnceWith('trip-b');
  });
});
