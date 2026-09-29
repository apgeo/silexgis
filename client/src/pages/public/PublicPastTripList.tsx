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
  /**
   * True when the failure was the server's own final answer rather than a request that did not
   * land. An installation can switch its archive off, and then this list is refused exactly as an
   * unknown link is refused — a settled refusal, which no retry will ever change. Told apart from
   * a dropped request so that the reader is not invited to try again something that will never
   * answer.
   */
  refused?: boolean;
  /**
   * True when this link's own read has been refused for good as well — the page is showing the
   * last envelope it was given under a notice that the link has stopped answering.
   *
   * <b>Then a refused list is not evidence that the archive is switched off.</b> The archive
   * outlives a trip's live window, so a link whose party has merely gone past its grace still opens
   * the cave's earlier trips; one that has been taken back or has run out is refused for them too,
   * with the very answer an installation whose archive is off gives. The page cannot tell those
   * apart, so the list must not claim either one — and it must not invite another try, which will
   * be refused the same way.
   */
  linkEnded?: boolean;
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
  refused = false,
  linkEnded = false,
  playingId,
  onPlay,
}: PublicPastTripListProps) {
  const { t, i18n } = useTranslation();

  if (failed) {
    // Failures with different honest sentences. A refusal the server settled while the link itself
    // still answers means the archive is not offered on this installation, and is said as that, in
    // plain words and without a way to try again. The same refusal on a link that has stopped
    // answering could be either the link being over or the archive being off, so it says only
    // what is certain. A request that did not land keeps the invitation, because that one can
    // clear.
    if (refused && linkEnded) {
      return (
        <Alert
          type="info"
          showIcon
          title={t('publicTrip.past.listLinkEndedTitle')}
          description={t('publicTrip.past.listLinkEndedBody')}
          data-testid="public-past-link-ended"
        />
      );
    }
    return refused ? (
      <Alert
        type="info"
        showIcon
        title={t('publicTrip.past.listNotOfferedTitle')}
        description={t('publicTrip.past.listNotOfferedBody')}
        data-testid="public-past-not-offered"
      />
    ) : (
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
