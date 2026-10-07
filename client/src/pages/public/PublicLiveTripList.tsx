// SPDX-License-Identifier: AGPL-3.0-or-later
import { EyeOutlined } from '@ant-design/icons';
import { Alert, Button, Skeleton, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { PublicLiveTrip } from '../../api/hooks.ts';
import { partyStandings, tripDateRange } from './publicTripParty.ts';

export interface PublicLiveTripListProps {
  trips: readonly PublicLiveTrip[] | undefined;
  /** True when more parties are being followed than this list carries — deliberately not a count. */
  more: boolean;
  loading: boolean;
  failed: boolean;
  /** True when the failure was the server's own final answer rather than a request that did not land. */
  refused?: boolean;
  /**
   * True when this link's own read has been refused for good as well. A settled refusal of this
   * list then belongs to the link being over, and the list says that instead of claiming the link
   * opens the trip above and inviting another try that will be refused the same way.
   */
  linkEnded?: boolean;
  /** This link's own trip, so its row is named as the one this link was published for. */
  ownTripLogId: string | null | undefined;
  /**
   * Asks for a party to be drawn — and, given this link's own trip, for the way back to it.
   * Without it the rows are statements and nothing on them can be pressed.
   */
  onWatch?(tripLogId: string): void;
  /** The other party being drawn now, or null while the drawing is this link's own trip. */
  watchingId?: string | null;
}

/**
 * The parties being followed in this cave right now, as rows above the archive.
 *
 * <b>The other half of the cave's list, and the half the archive cannot carry.</b> A trip still
 * being followed is deliberately not a past trip, so a page reading the archive alone shows an
 * expedition's second party nowhere — and for the whole of a watch's grace after it closes, that
 * trip is in neither list. These rows are what closes that gap: every party of the cave being
 * followed at this moment, each with its headcount and whether it is still underground.
 *
 * <b>A row says which of two things it is, in words.</b> "Being followed" and "underground" are
 * not the same statement: a watch that closed an hour ago is still readable and is no longer a
 * party in the cave. A row whose watch has closed says it has just finished, and never that
 * anybody is underground — that is the one false sentence this list must not produce.
 *
 * <b>A row of another party can be asked for, and that changes whom the drawing shows — never
 * which drawing.</b> Every place in a row was decided by the server against the survey this link
 * was published with, so pressing *Watch* puts that party on the drawing already loaded; nothing
 * is fetched and no other survey is opened. The row being watched says so in words instead of
 * offering the press again, and while another party is on screen the link's own row carries the
 * way back. The control is a button inside the row and not the row itself: a row also carries
 * what a reader may only want to read — who is still underground — and a whole row that changed
 * the drawing under a thumb scrolling past it would do so by accident.
 *
 * <b>The link's own trip is never something to watch.</b> It appears here too while it is being
 * followed, named as this link's trip — matched by identifier, never by title, because two trips
 * of one cave may share a title — and it is what the page draws whenever nothing else was asked
 * for. Its standings are not repeated in its row: the page above reads them by the link's own
 * route, more often, and two figures for one party a minute apart would be two stories.
 */
export default function PublicLiveTripList({
  trips,
  more,
  loading,
  failed,
  refused = false,
  linkEnded = false,
  ownTripLogId,
  onWatch,
  watchingId = null,
}: PublicLiveTripListProps) {
  const { t, i18n } = useTranslation();

  /** How many of a party stand where, as "Label: number" — the three states, never folded to two. */
  const standingsOf = (trip: PublicLiveTrip) => {
    const counts = partyStandings(trip.participants);
    return (['underground', 'out', 'unheard'] as const)
      .map((standing) =>
        t('publicTrip.live.standingCount', {
          standing: t(`publicTrip.standing.${standing}`),
          number: counts[standing],
        }),
      )
      .join(' · ');
  };

  const body = () => {
    if (failed && refused && linkEnded) {
      return (
        <Alert
          type="info"
          showIcon
          title={t('publicTrip.live.linkEndedTitle')}
          description={t('publicTrip.live.linkEndedBody')}
          data-testid="public-live-link-ended"
        />
      );
    }
    if (failed) {
      return (
        <Alert
          type="warning"
          showIcon
          title={t('publicTrip.live.failedTitle')}
          description={t('publicTrip.live.failedBody')}
          data-testid="public-live-failed"
        />
      );
    }
    if (loading || trips === undefined) {
      return <Skeleton active paragraph={{ rows: 1 }} data-testid="public-live-loading" />;
    }
    if (trips.length === 0) {
      return (
        <Typography.Text type="secondary" data-testid="public-live-empty">
          {t('publicTrip.live.none')}
        </Typography.Text>
      );
    }
    return (
      <>
        <ul className="public-past-rows" data-testid="public-live-list">
          {trips.map((trip) => {
            const dates = tripDateRange(trip.tripDate, trip.tripDateEnd, i18n.language);
            const underground = trip.state === 'armed';
            const own = trip.tripLogId === ownTripLogId;
            const watched = !own && trip.tripLogId === watchingId;
            return (
              <li
                key={trip.tripLogId}
                className="public-live-row"
                aria-current={watched ? 'true' : undefined}
                data-testid={`public-live-trip-${trip.tripLogId}`}
              >
                <span className="public-past-row-main">
                  <span className="public-past-row-title">{trip.title}</span>
                  <Typography.Text type="secondary" className="public-past-row-when">
                    {t('publicTrip.past.rowWhen', { dates, count: trip.participants.length })}
                  </Typography.Text>
                  {/* The camp this party is out from, where the server names one. A line and not
                      a grouping: there are a handful of rows here at most, and two parties of
                      one camp are told from a visiting club's by reading it. */}
                  {trip.expedition != null && (
                    <Typography.Text
                      type="secondary"
                      className="public-past-row-when"
                      data-testid={`public-live-camp-${trip.tripLogId}`}
                    >
                      {t('publicTrip.camp', { name: trip.expedition.name })}
                    </Typography.Text>
                  )}
                  {!own && (
                    <Typography.Text
                      type="secondary"
                      className="public-past-row-when"
                      data-testid={`public-live-standings-${trip.tripLogId}`}
                    >
                      {standingsOf(trip)}
                    </Typography.Text>
                  )}
                </span>
                <span className="public-live-row-marks">
                  {own && (
                    <Tag
                      className="public-past-row-mark"
                      data-testid={`public-live-own-${trip.tripLogId}`}
                    >
                      {t('publicTrip.live.thisLink')}
                    </Tag>
                  )}
                  <Tag
                    color={underground ? 'green' : undefined}
                    className="public-past-row-mark"
                    data-testid={`public-live-state-${trip.tripLogId}`}
                  >
                    {t(underground ? 'publicTrip.live.underground' : 'publicTrip.live.justFinished')}
                  </Tag>
                  {/* Said in words, as the archive's rows say which one is playing: which party
                      the drawing above is showing is the one thing this list must not leave to a
                      colour. */}
                  {watched && (
                    <Tag
                      color="blue"
                      icon={<EyeOutlined />}
                      className="public-past-row-mark"
                      data-testid={`public-live-watching-${trip.tripLogId}`}
                    >
                      {t('publicTrip.live.watching')}
                    </Tag>
                  )}
                  {onWatch !== undefined && !own && !watched && (
                    <Button
                      icon={<EyeOutlined />}
                      className="public-live-watch"
                      onClick={() => onWatch(trip.tripLogId)}
                      data-testid={`public-live-watch-${trip.tripLogId}`}
                    >
                      {t('publicTrip.live.watch')}
                    </Button>
                  )}
                  {/* Only while somebody else is on screen: with the link's own trip drawn there
                      is nowhere to go back to, and a button that did nothing would be a control
                      that looks broken. */}
                  {onWatch !== undefined && own && watchingId !== null && (
                    <Button
                      className="public-live-watch"
                      onClick={() => onWatch(trip.tripLogId)}
                      data-testid="public-live-back-own"
                    >
                      {t('publicTrip.live.backToOwn')}
                    </Button>
                  )}
                </span>
              </li>
            );
          })}
        </ul>
        {more && (
          <Typography.Text type="secondary" className="public-past-more" data-testid="public-live-more">
            {t('publicTrip.live.more')}
          </Typography.Text>
        )}
      </>
    );
  };

  return (
    <div className="public-live" data-testid="public-live">
      <Typography.Text strong className="public-live-heading" data-testid="public-live-heading">
        {t('publicTrip.live.heading')}
      </Typography.Text>
      {body()}
    </div>
  );
}
