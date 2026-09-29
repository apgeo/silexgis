// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
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
    expect(screen.getByTestId(`public-live-trip-${OTHER}`).textContent).not.toContain('Underground');
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
