// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { PublicLiveTrip } from '../../api/hooks.ts';
import PublicLiveTripList from './PublicLiveTripList.tsx';

/**
 * The parties being followed in the cave right now, as the list above the archive says them.
 *
 * <b>What is proved here is the wording of the two states.</b> "Being followed" and "underground"
 * are different statements: a watch that closed an hour ago is still in this list and is no longer
 * a party in the cave, and a row that said otherwise would be the one false sentence this list can
 * make about a real expedition.
 */

const OWN = 'aaaaaaaa-0000-0000-0000-000000000001';
const OTHER = 'aaaaaaaa-0000-0000-0000-000000000002';

function party(tripLogId: string, state: PublicLiveTrip['state'], title: string): PublicLiveTrip {
  return {
    tripLogId,
    expedition: null,
    title,
    tripDate: '2026-09-14',
    tripDateEnd: null,
    state,
    armedAt: '2026-09-14T08:00:00Z',
    closedAt: state === 'closed' ? '2026-09-14T16:00:00Z' : null,
    positionsWithheld: false,
    teams: [],
    participants: [
      { ordinal: 1, label: 'Ileana', teamId: null, teamTitle: null, position: null, lastReportAt: null },
      { ordinal: 2, label: 'Mircea', teamId: null, teamTitle: null, position: null, lastReportAt: null },
    ] as unknown as PublicLiveTrip['participants'],
  };
}

/** A party whose members stand as told: `in` and `out`, per person, as the server sends them. */
function standing(
  tripLogId: string,
  title: string,
  people: readonly { in: boolean; out: boolean }[],
): PublicLiveTrip {
  return {
    ...party(tripLogId, 'armed', title),
    participants: people.map((flags, index) => ({
      ordinal: index + 1,
      label: `Caver ${index + 1}`,
      teamId: null,
      stationName: null,
      depthM: null,
      lastRecordedAt: null,
      positionRecordedAt: null,
      positionOnOtherModel: false,
      ...flags,
    })),
  };
}

afterEach(cleanup);

describe('the parties being followed in the cave now', () => {
  it('says which party is still underground and which has just finished, never the other way round', () => {
    render(
      <PublicLiveTripList
        trips={[party(OWN, 'armed', 'E1, the deep end'), party(OTHER, 'closed', 'E2, the survey')]}
        more={false}
        loading={false}
        failed={false}
        ownTripLogId={OWN}
      />,
    );

    expect(screen.getByTestId('public-live-heading')).toHaveTextContent('Being followed now');
    expect(screen.getByTestId(`public-live-state-${OWN}`)).toHaveTextContent('Underground now');
    expect(screen.getByTestId(`public-live-state-${OTHER}`)).toHaveTextContent('Just finished');
    // The claim that must not be made of a finished watch is the state's, "Underground now". The
    // row does carry the word as the label of a count — how many of the party were last reported
    // underground is a figure, said the same way whichever state the watch is in.
    expect(screen.getByTestId(`public-live-trip-${OTHER}`).textContent).not.toContain('Underground now');
    expect(screen.getByTestId(`public-live-state-${OTHER}`).textContent).not.toContain('Underground');
    // Headcount and dates, in the words the archive rows use.
    expect(screen.getByTestId(`public-live-trip-${OTHER}`)).toHaveTextContent('2 in the party');
  });

  it("names this link's own trip as the one already on screen, by identifier and not by title", () => {
    render(
      <PublicLiveTripList
        trips={[party(OWN, 'armed', 'The same title'), party(OTHER, 'armed', 'The same title')]}
        more={false}
        loading={false}
        failed={false}
        ownTripLogId={OWN}
      />,
    );

    expect(screen.getByTestId(`public-live-own-${OWN}`)).toHaveTextContent("This link's trip");
    expect(screen.queryByTestId(`public-live-own-${OTHER}`)).toBeNull();
  });

  it('says so when nobody in the cave is being followed', () => {
    render(
      <PublicLiveTripList trips={[]} more={false} loading={false} failed={false} ownTripLogId={OWN} />,
    );

    expect(screen.getByTestId('public-live-empty')).toHaveTextContent('No party of this cave is being followed');
    expect(screen.queryByTestId('public-live-list')).toBeNull();
  });

  it('says the list could not be read, rather than that nobody is being followed', () => {
    render(
      <PublicLiveTripList trips={undefined} more={false} loading={false} failed ownTripLogId={OWN} />,
    );

    expect(screen.getByTestId('public-live-failed')).toBeInTheDocument();
    expect(screen.queryByTestId('public-live-empty')).toBeNull();
  });

  it('says a link that still opens the past no longer lists today’s parties, quietly and with no second try', () => {
    const { rerender } = render(
      <PublicLiveTripList
        trips={undefined}
        more={false}
        loading={false}
        failed
        refused
        pastOnly
        ownTripLogId={OWN}
      />,
    );

    const notice = screen.getByTestId('public-live-past-only');
    expect(notice).toHaveTextContent('This link no longer lists who is in the cave today');
    expect(notice.className).toContain('ant-alert-info');
    expect(screen.queryByTestId('public-live-failed')).toBeNull();
    expect(screen.queryByText(/Try opening this list again/)).toBeNull();

    // It wins over the link being over: past trips that answered at about the moment this list
    // was refused say the link still works. Whether they did is the caller's to establish.
    rerender(
      <PublicLiveTripList
        trips={undefined}
        more={false}
        loading={false}
        failed
        refused
        linkEnded
        pastOnly
        ownTripLogId={OWN}
      />,
    );
    expect(screen.getByTestId('public-live-past-only')).toBeInTheDocument();
    expect(screen.queryByTestId('public-live-link-ended')).toBeNull();

    // And it is never said of a list that merely failed, whatever else is claimed beside it.
    rerender(
      <PublicLiveTripList
        trips={undefined}
        more={false}
        loading={false}
        failed
        pastOnly
        ownTripLogId={OWN}
      />,
    );
    expect(screen.getByTestId('public-live-failed')).toBeInTheDocument();
    expect(screen.queryByTestId('public-live-past-only')).toBeNull();
  });

  it('says there are more parties than these, and never how many', () => {
    render(
      <PublicLiveTripList
        trips={[party(OTHER, 'armed', 'E2')]}
        more
        loading={false}
        failed={false}
        ownTripLogId={OWN}
      />,
    );

    expect(screen.getByTestId('public-live-more')).toHaveTextContent('more parties being followed');
  });
});

describe('asking for another party of the cave to be drawn', () => {
  const rows = () => [party(OWN, 'armed', 'E1, the deep end'), party(OTHER, 'armed', 'E2, the survey')];

  it('offers nothing to press where nobody is listening for a press', () => {
    render(
      <PublicLiveTripList trips={rows()} more={false} loading={false} failed={false} ownTripLogId={OWN} />,
    );

    expect(within(screen.getByTestId('public-live-list')).queryAllByRole('button')).toHaveLength(0);
  });

  it('offers Watch on another party’s row and never on this link’s own', () => {
    const onWatch = vi.fn();
    render(
      <PublicLiveTripList
        trips={rows()}
        more={false}
        loading={false}
        failed={false}
        ownTripLogId={OWN}
        onWatch={onWatch}
      />,
    );

    // The link's own trip is what the page draws when nothing else is asked for: there is nothing
    // to watch there, and with nobody else on screen nowhere to go back to either.
    expect(screen.queryByTestId(`public-live-watch-${OWN}`)).toBeNull();
    expect(screen.queryByTestId('public-live-back-own')).toBeNull();

    fireEvent.click(screen.getByTestId(`public-live-watch-${OTHER}`));
    expect(onWatch).toHaveBeenCalledExactlyOnceWith(OTHER);
  });

  it('says in words which party is on screen, and puts the way back on this link’s own row', () => {
    const onWatch = vi.fn();
    render(
      <PublicLiveTripList
        trips={rows()}
        more={false}
        loading={false}
        failed={false}
        ownTripLogId={OWN}
        onWatch={onWatch}
        watchingId={OTHER}
      />,
    );

    expect(screen.getByTestId(`public-live-watching-${OTHER}`)).toHaveTextContent('Watching');
    expect(screen.getByTestId(`public-live-trip-${OTHER}`)).toHaveAttribute('aria-current', 'true');
    // The press is not offered again on the row already on screen.
    expect(screen.queryByTestId(`public-live-watch-${OTHER}`)).toBeNull();
    expect(screen.getByTestId(`public-live-trip-${OWN}`)).not.toHaveAttribute('aria-current');

    fireEvent.click(screen.getByTestId('public-live-back-own'));
    expect(onWatch).toHaveBeenCalledExactlyOnceWith(OWN);
  });

  it('counts another party in its three states, and leaves this link’s own to the page above', () => {
    render(
      <PublicLiveTripList
        trips={[
          party(OWN, 'armed', 'E1, the deep end'),
          standing(OTHER, 'E2, the survey', [
            { in: true, out: false },
            { in: true, out: false },
            { in: true, out: true },
            { in: false, out: false },
          ]),
        ]}
        more={false}
        loading={false}
        failed={false}
        ownTripLogId={OWN}
      />,
    );

    // Somebody nobody has reported yet is a state of its own here too, never "not underground".
    expect(screen.getByTestId(`public-live-standings-${OTHER}`)).toHaveTextContent(
      'Underground: 2 · Out: 1 · Not reported yet: 1',
    );
    expect(screen.queryByTestId(`public-live-standings-${OWN}`)).toBeNull();
  });

  it('says which camp a party is out from where the server names one, and nothing where it names none', () => {
    render(
      <PublicLiveTripList
        trips={[
          {
            ...party(OWN, 'armed', 'Demo cave'),
            expedition: { id: 'cccccccc-0000-0000-0000-000000000001', name: 'Summer camp 2026' },
          },
          party(OTHER, 'armed', 'Demo cave'),
        ]}
        more={false}
        loading={false}
        failed={false}
        ownTripLogId={OWN}
      />,
    );

    expect(screen.getByTestId(`public-live-camp-${OWN}`)).toHaveTextContent('Camp: Summer camp 2026');
    expect(screen.queryByTestId(`public-live-camp-${OTHER}`)).toBeNull();
  });
});
