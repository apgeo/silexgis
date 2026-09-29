// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Skeleton, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { PublicLiveTrip } from '../../api/hooks.ts';
import { tripDateRange } from './publicTripParty.ts';

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
  /** This link's own trip, so its row is named as the one this page is already showing. */
  ownTripLogId: string | null | undefined;
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
 * <b>Nothing here is pressable.</b> The drawing above is this link's own trip, and it is the one
 * party this page follows; a row is a statement about the cave, not a control that changes the
 * view. The link's own trip appears here too while it is being followed, and is named as the trip
 * already on screen — matched by identifier, never by title, because two trips of one cave may
 * share a title.
 */
export default function PublicLiveTripList({
  trips,
  more,
  loading,
  failed,
  refused = false,
  linkEnded = false,
  ownTripLogId,
}: PublicLiveTripListProps) {
  const { t, i18n } = useTranslation();

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
            return (
              <li
                key={trip.tripLogId}
                className="public-live-row"
                data-testid={`public-live-trip-${trip.tripLogId}`}
              >
                <span className="public-past-row-main">
                  <span className="public-past-row-title">{trip.title}</span>
                  <Typography.Text type="secondary" className="public-past-row-when">
                    {t('publicTrip.past.rowWhen', { dates, count: trip.participants.length })}
                  </Typography.Text>
                </span>
                <span className="public-live-row-marks">
                  {trip.tripLogId === ownTripLogId && (
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
