// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { LibraryPhotographPage, LibraryPhotoStatus } from '../../api/hooks.ts';

/**
 * The photographs a neighbouring library holds from the days one trip was out.
 *
 * <p>
 * <b>What is really under test is one sentence.</b> The panel and the browsing page draw the same
 * grid from the same helpers, and the only thing separating them is that this one says its numbers
 * describe a <em>window</em>: "showing 12 of 12 photographs the library holds from these days"
 * rather than "of 12 photographs the library holds", and "nothing was taken while this trip was
 * out" rather than "this library holds nothing". Written wrongly, both are fluent, both render, and
 * both tell somebody with forty thousand photographs in a club library that the library holds
 * twelve. Nothing else in the suite would notice, which is why the wording is asserted here rather
 * than the helper being called with a literal and trusted.
 * </p>
 * <p>
 * The rest is the states that draw no grid at all and are otherwise only ever reached by looking:
 * an account with no right to a neighbouring library, an installation whose only library has been
 * stopped, and the two refusals that are about this application's own record of a trip rather than
 * about any library — which must never be reported as a library that did not answer, because that
 * sends somebody to restart a container nothing was asked of.
 * </p>
 * <p>
 * Every identifier, title and library name below is invented, and no photograph named here exists.
 * </p>
 */

const { statusSpy, photographsSpy, photographSpy } = vi.hoisted(() => ({
  statusSpy: vi.fn(),
  photographsSpy: vi.fn(),
  photographSpy: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  usePhotoLibraries: (...args: unknown[]) => statusSpy(...args),
  usePhotoLibraryPhotographs: (...args: unknown[]) => photographsSpy(...args),
  usePhotoLibraryPhotograph: (...args: unknown[]) => photographSpy(...args),
}));

const { default: TripLibraryPhotoPanel } = await import('./TripLibraryPhotoPanel.tsx');

const TripId = '11111111-1111-4111-8111-111111111111';

function health(over: Partial<LibraryPhotoStatus['providers'][number]['health']> = {}) {
  return {
    reach: 'reached',
    version: null,
    missingPermissions: [],
    picturesAvailable: true,
    failureCode: null,
    probedAt: null,
    ...over,
  };
}

function provider(source: string, name: string, suspended = false) {
  return { source, name, search: 'text', configured: true, suspended, health: health() };
}

function status(over: Partial<LibraryPhotoStatus> = {}): LibraryPhotoStatus {
  return {
    mayRead: true,
    maxSearchLength: 200,
    providers: [provider('photoprism', 'An invented library')],
    unconfigured: [],
    ...over,
  } as LibraryPhotoStatus;
}

function page(over: Partial<LibraryPhotographPage> = {}): LibraryPhotographPage {
  return {
    source: 'photoprism',
    libraryName: 'An invented library',
    items: [
      {
        photographId: 'psinvented1',
        reference: 'aa11bb22cc33',
        title: 'An invented picture',
        takenAt: '2026-03-14T09:00:00Z',
        kind: null,
      },
      {
        photographId: 'psinvented2',
        reference: 'bb22cc33dd44',
        title: null,
        takenAt: null,
        kind: null,
      },
    ],
    page: 1,
    pageSize: 60,
    total: 2,
    hasMore: false,
    pageSizeCapped: false,
    picturesAvailable: true,
    pictureUrlTemplate:
      '/api/v1/photo-libraries/photoprism/thumbnails/{reference}?size={size}&token=invented',
    readAt: '2026-03-20T10:00:00Z',
    ...over,
  } as LibraryPhotographPage;
}

interface Showing {
  libraries?: LibraryPhotoStatus;
  askingStatus?: boolean;
  answered?: LibraryPhotographPage | undefined;
  isPending?: boolean;
  error?: unknown;
}

function show({
  libraries = status(),
  askingStatus = false,
  answered = page(),
  isPending = false,
  error = null,
}: Showing = {}) {
  statusSpy.mockReturnValue({ data: libraries, isPending: askingStatus });
  photographsSpy.mockReturnValue({ data: answered, isPending, error });
  photographSpy.mockReturnValue({ data: undefined, isPending: false, error: null });
  return render(<TripLibraryPhotoPanel tripId={TripId} />);
}

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe('what a neighbouring library holds from the days a trip was out', () => {
  /**
   * The trip is what is named, and it is the only narrowing that leaves this browser.
   *
   * A pair of dates chosen here would make the panel a general date filter with a trip's name
   * written over it — the server reads the trip's own days, which is what lets the heading claim
   * anything at all.
   */
  it('asks about a trip and never about a stretch of time it chose itself', () => {
    show();

    const asked = photographsSpy.mock.calls[0];
    expect(asked[0]).toBe('photoprism');
    expect(asked[1]).toMatchObject({ page: 1, tripId: TripId });
    expect(Object.keys(asked[1] as object).sort()).toEqual(['page', 'pageSize', 'tripId']);
  });

  /**
   * The load-bearing sentence. The number beside a narrowed listing counts what the library holds
   * <em>in that window</em>, and the browsing page's own wording written over it would report a
   * club's whole library as the size of one weekend.
   */
  it('says the count is of these days and not of the whole library', () => {
    show();

    expect(
      screen.getByText('Showing 2 of 2 photographs the library holds from these days'),
    ).toBeTruthy();
  });

  /**
   * The same sentence where the library publishes no way to ask how many it holds. One of the two
   * products does not, so this branch is half the installations rather than an edge.
   */
  it('says so too when the library states no total', () => {
    show({ answered: page({ total: null }) });

    expect(screen.getByText(/Showing 2 photographs from these days/)).toBeTruthy();
  });

  /**
   * An empty answer here is a fact about the days, not about the library — and the difference is
   * the entire reason the panel exists in a separate file from the browsing page.
   */
  it('tells an empty window apart from an empty library', () => {
    show({ answered: page({ items: [], total: 0 }) });

    expect(
      screen.getByText(/This library holds nothing taken while this trip was out/),
    ).toBeTruthy();

    // And "showing 0" is not written above it: what the empty grid is doing there is said inside
    // it, and a second way of saying the same thing would be a worse one.
    expect(screen.queryByText(/Showing 0/)).toBeNull();
  });

  /**
   * A trip the reader may not see, or one that is not there.
   *
   * Reported as itself rather than as a library that did not answer. Nothing was asked of any
   * library, and the sentence for an unanswered library sends an administrator to a container that
   * is running perfectly.
   */
  it('names a trip it could not read rather than blaming the library', () => {
    show({
      answered: undefined,
      error: new ApiError(404, 'photo_library.trip_not_found'),
    });

    expect(screen.getByTestId('trip-library-photo-problem').textContent).toContain(
      'This trip could not be read',
    );
    expect(screen.queryByText(/did not answer/)).toBeNull();
  });

  /** A trip whose own dates cannot make a window, which is a record to correct and not an outage. */
  it('names dates that cannot make a window rather than blaming the library', () => {
    show({
      answered: undefined,
      error: new ApiError(400, 'photo_library.trip_window_unusable'),
    });

    expect(screen.getByTestId('trip-library-photo-problem').textContent).toContain(
      "This trip's dates do not give a stretch of time",
    );
    expect(screen.queryByText(/did not answer/)).toBeNull();
  });

  /**
   * A library that has been stopped by an administrator.
   *
   * Said in one line rather than drawn as an empty grid: somebody decided this, and the route
   * behind the panel refuses, so a grid would report a deliberate act as a library holding nothing.
   */
  it('says one line when the only library has been stopped, and asks it nothing', () => {
    show({
      libraries: status({ providers: [provider('photoprism', 'An invented library', true)] }),
    });

    expect(screen.getByText(/not using this photo library for now/)).toBeTruthy();
    expect(screen.queryByTestId('library-photo-tile-picture')).toBeNull();

    // And no library was named to the hook at all, so nothing was asked of the stopped one — the
    // grid being absent would also be true of a request that went out and was refused.
    expect(photographsSpy.mock.calls[0]?.[0]).toBeUndefined();
  });

  /**
   * An account with no right to a neighbouring library gets no panel at all.
   *
   * This is one section of a page about something else, so there is no errand that begins here and
   * nothing to explain — unlike the library's own page, which somebody reached by an address.
   */
  it('draws nothing for an account that may not look at a library', () => {
    const { container } = show({ libraries: status({ mayRead: false }) });

    expect(container.textContent).toBe('');
  });

  /** An installation running no photo library at all is a supported installation, not a gap. */
  it('draws nothing where no library is configured', () => {
    const { container } = show({ libraries: status({ providers: [] }) });

    expect(container.textContent).toBe('');
  });

  /**
   * Switching library goes back to the first page.
   *
   * Two of these products can be connected at once and they are separate installations with
   * separate contents, so page four of one names nothing in the other — and the request that
   * followed would either be refused as too deep or answer with somebody else's fourth page.
   */
  it('goes back to the first page when the library is switched', () => {
    show({
      libraries: status({
        providers: [
          provider('photoprism', 'An invented library'),
          provider('immich', 'Another invented library'),
        ],
      }),
      answered: page({ hasMore: true }),
    });

    // The page in hand is the page being asked for, so the controls are live.
    fireEvent.click(screen.getByText('Next').closest('button')!);
    expect(photographsSpy.mock.calls.at(-1)?.[1]).toMatchObject({ page: 2 });

    fireEvent.click(screen.getByText('Another invented library'));
    expect(photographsSpy.mock.calls.at(-1)?.[1]).toMatchObject({ page: 1, tripId: TripId });
  });
});
