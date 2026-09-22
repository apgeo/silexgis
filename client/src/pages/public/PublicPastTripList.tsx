// SPDX-License-Identifier: AGPL-3.0-or-later
import { CaretRightOutlined } from '@ant-design/icons';
import { Alert, Skeleton, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { PublicPastTrip } from '../../api/hooks.ts';
import { tripDateRange } from './publicTripParty.ts';

export interface PublicPastTripListProps {
  trips: readonly PublicPastTrip[] | undefined;
  /** True when older trips exist that this list does not carry — deliberately not a count. */
  more: boolean;
  loading: boolean;
  failed: boolean;
  /** Which row is playing, so the list says which one the drawing above is showing. */
  playingId: string | null;
  onPlay(tripLogId: string): void;
}

/**
 * The past trips of this cave, as rows somebody picks from.
 *
 * <b>One list, two surroundings.</b> The followed page opens it inline under the party; the
 * embedded viewer opens it in a sheet over the drawing, because a frame in somebody's article has
 * no room to carry it standing open. What a row says and what pressing one does are identical in
 * both, which is the whole reason this is a component and not two pieces of markup.
 *
 * <b>A trip with nothing to play is drawn as a trip with nothing to play.</b> The server answers
 * `playable` for a reason a client could not work out for itself: a trip can be published, followed
 * and closed with nothing recorded but entries and exits, or with every report measured against a
 * survey that has since been replaced. Either way its playback opens empty. Offering such a row as
 * pressable and then showing an empty cave is worse than offering nothing, so the row is present —
 * a club's history is what this list is — visibly not pressable, and says why in words rather than
 * by being greyed out, which on a phone is indistinguishable from a rendering glitch.
 *
 * <b>Nothing here is a link to anywhere.</b> Like the page around it, a visitor holding one token
 * has exactly one address in this installation; a row is a control that changes what the drawing
 * above is showing, and never a navigation.
 */
export default function PublicPastTripList({
  trips,
  more,
  loading,
  failed,
  playingId,
  onPlay,
}: PublicPastTripListProps) {
  const { t, i18n } = useTranslation();

  if (failed) {
    return (
      <Alert
        type="warning"
        showIcon
        title={t('publicTrip.past.listFailedTitle')}
        description={t('publicTrip.past.listFailedBody')}
        data-testid="public-past-failed"
      />
    );
  }

  if (loading || trips === undefined) {
    return <Skeleton active paragraph={{ rows: 3 }} data-testid="public-past-loading" />;
  }

  if (trips.length === 0) {
    return (
      <Typography.Text type="secondary" data-testid="public-past-empty">
        {t('publicTrip.past.none')}
      </Typography.Text>
    );
  }

  return (
    <div data-testid="public-past-list">
      <ul className="public-past-rows">
        {trips.map((trip) => {
          const dates = tripDateRange(trip.tripDate, trip.tripDateEnd, i18n.language);
          const playing = trip.tripLogId === playingId;
          return (
            <li key={trip.tripLogId}>
              <button
                type="button"
                className="public-past-row"
                disabled={!trip.playable}
                aria-current={playing ? 'true' : undefined}
                onClick={() => onPlay(trip.tripLogId)}
                data-testid={`public-past-trip-${trip.tripLogId}`}
              >
                <span className="public-past-row-main">
                  <span className="public-past-row-title">{trip.title}</span>
                  <Typography.Text type="secondary" className="public-past-row-when">
                    {t('publicTrip.past.rowWhen', { dates, count: trip.participantCount })}
                  </Typography.Text>
                </span>
                {/* Said in words, never by colour alone: the difference between a row that plays
                    and one that cannot is the one thing a reader has to be able to see here. */}
                {trip.playable ? (
                  <Tag
                    color={playing ? 'blue' : undefined}
                    icon={<CaretRightOutlined />}
                    className="public-past-row-mark"
                  >
                    {playing ? t('publicTrip.past.playing') : t('publicTrip.past.play')}
                  </Tag>
                ) : (
                  <Tag
                    className="public-past-row-mark"
                    data-testid={`public-past-unplayable-${trip.tripLogId}`}
                  >
                    {t('publicTrip.past.notPlayable')}
                  </Tag>
                )}
              </button>
            </li>
          );
        })}
      </ul>
      {/* Not a count, because the server deliberately does not send one: how many times a club has
          been into one cave is a disclosure, and that there is something older is not. */}
      {more && (
        <Typography.Text type="secondary" className="public-past-more" data-testid="public-past-more">
          {t('publicTrip.past.more')}
        </Typography.Text>
      )}
    </div>
  );
}
