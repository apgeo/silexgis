// SPDX-License-Identifier: AGPL-3.0-or-later
import { App, ConfigProvider } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { PublishedLink, PublishedLinks, PublishedLinksParams } from '../../api/hooks.ts';

const LIVE = '11111111-1111-1111-1111-111111111111';
const PAST = '22222222-2222-2222-2222-222222222222';
const GONE = '33333333-3333-3333-3333-333333333333';
const TRIP_A = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const TRIP_B = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const CAVE = 'cccccccc-cccc-cccc-cccc-cccccccccccc';

function link(overrides: Partial<PublishedLink>): PublishedLink {
  return {
    id: LIVE,
    handle: 'Ab3dEf9h',
    tripLogId: TRIP_A,
    tripTitle: 'Invented trip one',
    tripDate: '2026-09-12',
    tripDateEnd: null,
    cave: { id: CAVE, name: 'Invented cave' },
    watchState: 'armed',
    watchArmedAt: '2026-09-12T07:00:00Z',
    watchClosedAt: null,
    createdBy: 'user-1',
    createdByLabel: 'Ana Example',
    createdAt: '2026-09-12T08:00:00Z',
    expiresAt: '2026-09-27T00:00:00Z',
    revokedAt: null,
    status: 'followable',
    protectedCaveWithinSurveyBounds: false,
    ...overrides,
  };
}

function answer(items: PublishedLink[], overrides: Partial<PublishedLinks> = {}): PublishedLinks {
  const tally = (status: PublishedLink['status']) =>
    items.filter((item) => item.status === status).length;
  return {
    items,
    page: 1,
    pageSize: 25,
    totalItems: items.length,
    asOf: '2026-09-14T12:00:00Z',
    counts: (['followable', 'inGrace', 'inArchive', 'withheld', 'lapsed', 'revoked'] as const).map(
      (status) => ({ status, count: tally(status) }),
    ),
    publishesRealNames: true,
    archiveEnabled: true,
    seenFrom: '203.0.113.7',
    ...overrides,
  };
}

const three = () =>
  answer([
    link({}),
    link({
      id: PAST,
      handle: 'Zz9yXx8w',
      tripLogId: TRIP_B,
      tripTitle: 'Invented trip two',
      status: 'inArchive',
      watchState: 'closed',
      watchClosedAt: '2026-09-01T18:00:00Z',
      createdByLabel: null,
      cave: null,
    }),
    link({
      id: GONE,
      handle: 'Qq1wEe2r',
      status: 'revoked',
      revokedAt: '2026-09-13T09:00:00Z',
    }),
  ]);

let list: {
  data?: PublishedLinks;
  error: unknown;
  isPending: boolean;
  isFetching: boolean;
  refetch: () => Promise<unknown>;
};
let asked: PublishedLinksParams[] = [];
const replace = vi.fn();
const revokeTrip = vi.fn();
const revokeEverything = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  usePublishedLinks: (params: PublishedLinksParams) => {
    asked.push(params);
    return list;
  },
  useReplaceTripTrackingShare: () => ({ mutateAsync: replace, isPending: false }),
  useRevokeTripPublishedLinks: () => ({ mutateAsync: revokeTrip, isPending: false }),
  useRevokeEveryPublishedLink: () => ({ mutateAsync: revokeEverything, isPending: false }),
}));

const { default: PublishedTripsPage } = await import('./PublishedTripsPage.tsx');

// Without motion, so a dialog that was closed is gone when the press returns. With it the test
// environment never finishes the closing animation, a closed dialog stays in the document, and
// "the address is no longer on screen" could not be asserted at all.
function show() {
  return render(
    <MemoryRouter>
      <ConfigProvider theme={{ token: { motion: false } }}>
        <App>
          <PublishedTripsPage />
        </App>
      </ConfigProvider>
    </MemoryRouter>,
  );
}

const settled = (data: PublishedLinks) => ({
  data,
  error: null,
  isPending: false,
  isFetching: false,
  refetch: vi.fn().mockResolvedValue(undefined),
});

describe('the page of everything published', () => {
  beforeEach(() => {
    asked = [];
    list = settled(three());
    replace.mockReset();
    revokeTrip.mockReset().mockResolvedValue({ revokedLinks: 2, trips: 1 });
    revokeEverything.mockReset().mockResolvedValue({ revokedLinks: 2, trips: 2 });
  });

  afterEach(cleanup);

  it('shows it is reading before there is an answer, and offers nothing to withdraw meanwhile', () => {
    list = { data: undefined, error: null, isPending: true, isFetching: true, refetch: vi.fn() };
    show();

    expect(screen.getByTestId('published-trips')).toBeInTheDocument();
    expect(document.querySelector('.ant-spin')).not.toBeNull();
    // No figures yet means nothing known to be standing, so the one irreversible act is not armed
    // on a guess.
    expect(screen.getByTestId('published-trips-revoke-everything')).toBeDisabled();
    expect(screen.queryByText('Nothing has been published on this installation.')).toBeNull();
  });

  it('says plainly that nothing is published, and tells that apart from an empty filter', () => {
    list = settled(answer([]));
    show();

    expect(screen.getByText('Nothing has been published on this installation.')).toBeInTheDocument();
    expect(screen.queryByText('No link has this status.')).toBeNull();
    expect(screen.getByTestId('published-trips-revoke-everything')).toBeDisabled();
  });

  /**
   * A refusal is the server's word and is drawn as one — not as a failure, and not as an empty
   * list. The positive twin is the test below it: the same page with an answer lists rows, so
   * what hides them here is the refusal and nothing else.
   */
  it('tells somebody who is not a full administrator that the page is not theirs, and lists nothing', () => {
    list = {
      data: undefined,
      error: new ApiError(403, 'access.forbidden'),
      isPending: false,
      isFetching: false,
      refetch: vi.fn(),
    };
    show();

    expect(screen.getByTestId('published-trips-refused')).toHaveTextContent(
      /For full administrators only/,
    );
    expect(screen.queryByTestId('published-trips')).toBeNull();
    expect(screen.queryByTestId('published-trips-revoke-everything')).toBeNull();
    expect(screen.queryByTestId('published-trips-unavailable')).toBeNull();
  });

  it('says the list could not be read when the read failed for any other reason', () => {
    list = {
      data: undefined,
      error: new ApiError(500),
      isPending: false,
      isFetching: false,
      refetch: vi.fn(),
    };
    show();

    expect(screen.getByTestId('published-trips-unavailable')).toHaveTextContent(/could not be read/);
    expect(screen.queryByTestId('published-trips-refused')).toBeNull();
  });

  it('lists each link with the status it was given, who published it, its trip and its cave', () => {
    show();

    expect(screen.getByTestId(`published-trips-status-${LIVE}`)).toHaveTextContent('Followed now');
    expect(screen.getByTestId(`published-trips-status-${PAST}`)).toHaveTextContent('In the archive');
    expect(screen.getByTestId(`published-trips-status-${GONE}`)).toHaveTextContent('Taken back');

    // Each trip is one click away, and so is its cave when the watch still has one.
    const trip = screen.getByRole('link', { name: 'Invented trip two' });
    expect(trip).toHaveAttribute('href', `/trip-logs/${TRIP_B}`);
    expect(screen.getAllByRole('link', { name: 'Invented cave' })[0]).toHaveAttribute(
      'href',
      `/caves/${CAVE}`,
    );
    expect(screen.getByText('No cave')).toBeInTheDocument();
    expect(screen.getAllByText('Ana Example').length).toBeGreaterThan(0);
    expect(screen.getByText('Account no longer shown')).toBeInTheDocument();

    // The short code the request log writes — never an address, which this list is not sent.
    expect(screen.getByText('Ab3dEf9h')).toBeInTheDocument();

    // The figures are the server's, zero included, and cover the installation.
    expect(screen.getByTestId('published-trips-count-followable')).toHaveTextContent('1');
    expect(screen.getByTestId('published-trips-count-inArchive')).toHaveTextContent('1');
    expect(screen.getByTestId('published-trips-count-revoked')).toHaveTextContent('1');
    expect(screen.getByTestId('published-trips-count-withheld')).toHaveTextContent('0');

    expect(screen.getByTestId('published-trips-notes')).toHaveTextContent(/Statuses as of/);
    expect(screen.getByTestId('published-trips-notes')).toHaveTextContent(/real names/);
  });

  it('offers no act on a link that was already taken back, and both on one that stands', () => {
    show();

    expect(screen.getByTestId(`published-trips-replace-${LIVE}`)).toBeInTheDocument();
    expect(screen.getByTestId(`published-trips-revoke-trip-${PAST}`)).toBeInTheDocument();
    expect(screen.queryByTestId(`published-trips-replace-${GONE}`)).toBeNull();
    expect(screen.queryByTestId(`published-trips-revoke-trip-${GONE}`)).toBeNull();
  });

  it('asks the server for one status when the filter is set, from the first page', async () => {
    show();
    expect(asked.at(-1)).toMatchObject({ page: 1, status: undefined });

    fireEvent.mouseDown(
      within(screen.getByTestId('published-trips-status-filter')).getByRole('combobox'),
    );
    fireEvent.click(await screen.findByTitle('In the archive'));

    await waitFor(() => expect(asked.at(-1)).toMatchObject({ page: 1, status: 'inArchive' }));
  });

  /**
   * Tracking left running is shown for what it is, and closing it is one click away — on the
   * trip, where the coordinator's own button is. A watch somebody closed gets neither the count
   * nor the way in, so what puts them on the first row is that its tracking is running.
   */
  it('says when tracking was started and how long it has run, and leads to the trip only while it runs', () => {
    show();

    // Started 2026-09-12 07:00, statuses as of 2026-09-14 12:00: two whole days, by the server's
    // two instants and not by this machine's clock.
    const running = screen.getByTestId(`published-trips-running-${LIVE}`);
    expect(running).toHaveTextContent('Days running: 2');
    expect(screen.getByTestId(`published-trips-watch-${LIVE}`)).toHaveAttribute(
      'href',
      `/trip-logs/${TRIP_A}?tab=tracking`,
    );
    // Followed now: its link has not run out, so nothing is said about that.
    expect(running).not.toHaveTextContent(/this link has run out/);

    expect(screen.queryByTestId(`published-trips-running-${PAST}`)).toBeNull();
    expect(screen.queryByTestId(`published-trips-watch-${PAST}`)).toBeNull();
    expect(screen.getByText(/^Closed /)).toBeInTheDocument();
  });

  it('points out tracking that is still running behind a link that ran out', () => {
    list = settled(
      answer([link({ status: 'lapsed', watchArmedAt: '2026-08-01T07:00:00Z' }), link({ id: PAST })]),
    );
    show();

    const forgotten = screen.getByTestId(`published-trips-running-${LIVE}`);
    expect(forgotten).toHaveTextContent('Days running: 44');
    expect(forgotten).toHaveTextContent('Tracking is still running, but this link has run out.');
    // The followed one beside it runs too and is not flagged.
    expect(screen.getByTestId(`published-trips-running-${PAST}`)).not.toHaveTextContent(
      /this link has run out/,
    );
  });

  it('says so when nothing recorded the moment tracking was started', () => {
    list = settled(answer([link({ watchArmedAt: null })]));
    show();

    expect(screen.getByText('Not recorded')).toBeInTheDocument();
    expect(screen.queryByTestId(`published-trips-running-${LIVE}`)).toBeNull();
  });

  it('asks the server for tracking running longer than the days typed, and stops asking when emptied', async () => {
    show();
    expect(asked.at(-1)).toMatchObject({ armedLongerThanDays: undefined });

    const days = screen.getByRole('spinbutton', { name: 'Tracking running longer than (days)' });
    fireEvent.change(days, { target: { value: '7' } });
    await waitFor(() =>
      expect(asked.at(-1)).toMatchObject({ page: 1, armedLongerThanDays: 7 }),
    );

    // Zero is a question of its own — every trip whose tracking is running — not "no filter".
    fireEvent.change(days, { target: { value: '0' } });
    await waitFor(() => expect(asked.at(-1)).toMatchObject({ armedLongerThanDays: 0 }));

    fireEvent.change(days, { target: { value: '' } });
    await waitFor(() => expect(asked.at(-1)).toMatchObject({ armedLongerThanDays: undefined }));
  });

  it('tells an empty answer to the long-running question apart from nothing published at all', async () => {
    list = settled(answer([]));
    show();
    expect(screen.getByText('Nothing has been published on this installation.')).toBeInTheDocument();

    fireEvent.change(
      screen.getByRole('spinbutton', { name: 'Tracking running longer than (days)' }),
      { target: { value: '30' } },
    );

    expect(
      await screen.findByText("No published trip's tracking has been running that long."),
    ).toBeInTheDocument();
    expect(screen.queryByText('Nothing has been published on this installation.')).toBeNull();
  });

  it('asks for the longest-running first when the column is pressed', async () => {
    show();

    // The table renders the heading's text more than once. Pressing the first is enough: what
    // proves the press reached the column's sorter is the question asked below.
    fireEvent.click(screen.getAllByText('Tracking started')[0]);

    await waitFor(() =>
      expect(asked.at(-1)).toMatchObject({ sort: 'watchArmedAt', descending: false }),
    );
  });

  /**
   * The address is the server's reading of where this request came from, printed back so that a
   * wrong proxy count — which fails in no other visible way — can be seen by somebody who knows
   * their own address. When the server could not tell, the page says that and invents nothing.
   */
  it('prints the address the server counted the request under, and what it means when it is a proxy', () => {
    const told = show();

    expect(screen.getByTestId('published-trips-seen-from-address')).toHaveTextContent(
      /^203\.0\.113\.7$/,
    );
    const line = screen.getByTestId('published-trips-seen-from');
    expect(line).toHaveTextContent(/This request reached the server from:/);
    expect(line).toHaveTextContent(/number of proxies is too low/);
    expect(line).toHaveTextContent(/SILEXGIS__Proxy__Hops/);
    told.unmount();

    list = settled(answer([link({})], { seenFrom: null }));
    show();

    expect(screen.getByTestId('published-trips-seen-from')).toHaveTextContent(
      'The server could not tell which address this request came from.',
    );
    expect(screen.queryByTestId('published-trips-seen-from-address')).toBeNull();
  });

  /**
   * The tag and its explanation appear together and only when the server flagged a row.
   *
   * Asserted both ways in one test: an explanation always on screen would be read by nobody, and
   * one that never appeared would pass any assertion that it is absent.
   */
  it('tags a link whose survey area holds a protected cave, and explains what the check cannot see', () => {
    const plain = show();
    expect(screen.queryByTestId('published-trips-bounds-explain')).toBeNull();
    expect(screen.queryByTestId(`published-trips-bounds-${LIVE}`)).toBeNull();
    plain.unmount();

    list = settled(answer([link({ protectedCaveWithinSurveyBounds: true })]));
    show();

    expect(screen.getByTestId(`published-trips-bounds-${LIVE}`)).toHaveTextContent(
      'Protected cave nearby?',
    );
    const explain = screen.getByTestId('published-trips-bounds-explain');
    expect(explain).toHaveTextContent(/check by position only/);
    expect(explain).toHaveTextContent(/cannot see inside the survey file/);
    expect(explain).toHaveTextContent(/Nothing was refused/);
  });

  it('unpublishes one trip only after saying what goes with it', async () => {
    show();

    fireEvent.click(screen.getByTestId(`published-trips-revoke-trip-${PAST}`));

    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveTextContent('Invented trip two');
    expect(dialog).toHaveTextContent(/leaves its cave's public list of past trips/);
    expect(dialog).toHaveTextContent(/published again only by starting its watch again/);
    expect(dialog).toHaveTextContent(/cannot be undone/);
    expect(revokeTrip).not.toHaveBeenCalled();

    fireEvent.click(within(dialog).getByRole('button', { name: 'Unpublish this trip' }));

    await waitFor(() => expect(revokeTrip).toHaveBeenCalledWith({ tripLogId: TRIP_B }));
    expect(await screen.findByText('Links taken back: 2')).toBeInTheDocument();
    expect(revokeEverything).not.toHaveBeenCalled();
  });

  it('backs out of unpublishing a trip without sending anything', async () => {
    show();

    fireEvent.click(screen.getByTestId(`published-trips-revoke-trip-${LIVE}`));
    const dialog = await screen.findByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));

    expect(revokeTrip).not.toHaveBeenCalled();
  });

  /**
   * Withdrawing everything cannot be narrowed afterwards, so the button that does it is dead
   * until a word has been typed — and stays dead for a near miss.
   */
  it('unpublishes everything only once the word has been typed, after spelling out the consequence', async () => {
    show();

    fireEvent.click(screen.getByTestId('published-trips-revoke-everything'));

    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveTextContent(/Links still standing: 2/);
    expect(dialog).toHaveTextContent(/Every cave's public list of past trips empties/);
    expect(dialog).toHaveTextContent(/published again only by starting its watch again/);
    expect(dialog).toHaveTextContent(/cannot be undone/);

    const go = within(dialog).getByRole('button', { name: 'Unpublish everything' });
    expect(go).toBeDisabled();

    const word = screen.getByTestId('published-trips-revoke-everything-word');
    fireEvent.change(word, { target: { value: 'UNPUBLIS' } });
    expect(go).toBeDisabled();
    fireEvent.click(go);
    expect(revokeEverything).not.toHaveBeenCalled();

    fireEvent.change(word, { target: { value: 'UNPUBLISH' } });
    expect(go).toBeEnabled();
    fireEvent.click(go);

    await waitFor(() => expect(revokeEverything).toHaveBeenCalledTimes(1));
    expect(await screen.findByText('Links taken back: 2 · Trips: 2')).toBeInTheDocument();
    expect(revokeTrip).not.toHaveBeenCalled();
  });

  it('forgets a word typed for a withdrawal that was backed out of', async () => {
    show();

    fireEvent.click(screen.getByTestId('published-trips-revoke-everything'));
    fireEvent.change(await screen.findByTestId('published-trips-revoke-everything-word'), {
      target: { value: 'UNPUBLISH' },
    });
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());

    fireEvent.click(screen.getByTestId('published-trips-revoke-everything'));
    const dialog = await screen.findByRole('dialog');

    expect(within(dialog).getByRole('button', { name: 'Unpublish everything' })).toBeDisabled();
    expect(revokeEverything).not.toHaveBeenCalled();
  });

  /**
   * A replacement's answer carries the only copy of the new address, so it is shown at once, with
   * a way to copy it, and the dialog holding it closes only by the button that says it was copied.
   */
  it('replaces a link after saying the old address dies, and shows the fresh one once', async () => {
    replace.mockResolvedValue({
      id: '44444444-4444-4444-4444-444444444444',
      token: 'fresh-TOKEN_9',
      createdAt: '2026-09-14T12:00:00Z',
      expiresAt: '2026-09-27T00:00:00Z',
      protectedCaveWithinSurveyBounds: false,
    });
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    show();

    fireEvent.click(screen.getByTestId(`published-trips-replace-${LIVE}`));
    const confirm = await screen.findByRole('dialog');
    expect(confirm).toHaveTextContent(/old address stops answering at once/);
    expect(confirm).toHaveTextContent(/nobody on the trip is told/);
    expect(replace).not.toHaveBeenCalled();
    fireEvent.click(within(confirm).getByRole('button', { name: 'Replace link' }));

    await waitFor(() =>
      expect(replace).toHaveBeenCalledWith({ tripLogId: TRIP_A, shareId: LIVE }),
    );
    const address = await screen.findByTestId('published-trips-replaced-link');
    const expected = `${window.location.origin}/shared/trips/fresh-TOKEN_9`;
    expect(address).toHaveValue(expected);
    expect(screen.getByText(/this is the only time it is shown/)).toBeInTheDocument();

    fireEvent.click(screen.getByTestId('published-trips-replaced-copy-link'));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith(expected));

    fireEvent.click(screen.getByTestId('published-trips-replaced-done'));
    await waitFor(() => expect(screen.queryByTestId('published-trips-replaced-link')).toBeNull());
  });

  it('says why when the link to replace had already been taken back, and shows no address', async () => {
    replace.mockRejectedValue(new ApiError(409, 'tracking.share_revoked'));
    show();

    fireEvent.click(screen.getByTestId(`published-trips-replace-${LIVE}`));
    fireEvent.click(
      within(await screen.findByRole('dialog')).getByRole('button', { name: 'Replace link' }),
    );

    expect(await screen.findByText(/already been taken back/)).toBeInTheDocument();
    expect(screen.queryByTestId('published-trips-replaced-link')).toBeNull();
  });
});
