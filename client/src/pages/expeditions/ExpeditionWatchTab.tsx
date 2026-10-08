// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Card, Empty, Flex, Spin, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import type { ExpeditionSurfaceLogPerson, ExpeditionSurfaceLogTrip } from '../../api/hooks.ts';
import { useExpeditionSurfaceLog } from '../../api/hooks.ts';
import {
  lastHeardInWords,
  trackingStandingOf,
  trackingStandings,
  type TrackingStanding,
} from '../../components/trips/trackingWatch.ts';
import { formatTripDates } from '../../components/trips/tripDates.ts';
import { useNow } from '../../hooks/useNow.ts';
import './ExpeditionWatchTab.css';

/**
 * Who is underground on the camp's trips, on one screen.
 *
 * <b>A head count, not a map.</b> A camp runs several parties on the same day and the person at
 * the surface has one question; the trip's own tracking answers it one party at a time. This
 * section is that answer for the whole camp — who is in, who is out, who has not been heard from
 * and when each was last heard — and it says nothing about where anybody is. That is not a gap to
 * be filled later: the answer it draws carries no station, depth, survey or cave, which is what
 * lets it count a party in a cave whose position this reader may not be told. Where somebody is
 * stays the business of the trip's own tracking, one link away, behind the trip's own rules.
 *
 * <b>It records; it raises nothing.</b> The hour a party said it would be out by is printed as the
 * time the trip already tells its readers. Nothing here compares it with the clock, colours it or
 * calls anybody late: a count that looked like an alarm would be read as one, and the alarm is the
 * callout's, which this section knows nothing about.
 *
 * <b>Told when it is the section on screen.</b> The camp page keeps a section mounted once it has
 * been opened, so being mounted is not the same question as being looked at. While it is not the
 * one shown this asks the server nothing and draws nothing — no request every half minute from
 * behind the map, and no timer redrawing ages nobody can see.
 */
export default function ExpeditionWatchTab({
  expeditionId,
  active,
}: {
  expeditionId: string;
  active: boolean;
}) {
  const { t } = useTranslation();
  const { data, isPending, error } = useExpeditionSurfaceLog(expeditionId, active);

  if (!active) {
    return <div data-testid="expedition-watch-tab" />;
  }

  if (error) {
    return (
      <div data-testid="expedition-watch-tab">
        <Alert
          type="warning"
          showIcon
          data-testid="expedition-watch-unavailable"
          title={t('expeditions.watch.unavailable')}
        />
      </div>
    );
  }

  if (isPending || !data) {
    return (
      <div data-testid="expedition-watch-tab">
        <Spin />
      </div>
    );
  }

  return (
    <div data-testid="expedition-watch-tab">
      <Typography.Paragraph type="secondary">{t('expeditions.watch.intro')}</Typography.Paragraph>
      {data.truncated && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid="expedition-watch-truncated"
          title={t('expeditions.watch.truncated')}
        />
      )}
      {data.trips.length === 0 ? (
        // Said in full rather than left as a blank pane: an empty count on a camp with a party
        // underground would be the most dangerous thing this section could draw, so the reader
        // is told exactly what puts a trip here and can tell "nobody is being tracked" from
        // "tracking was never started".
        <Empty description={t('expeditions.watch.empty')} data-testid="expedition-watch-empty" />
      ) : (
        <WatchedTrips trips={data.trips} />
      )}
    </div>
  );
}

/**
 * A last-heard moment in the one shape the shared wording takes it in.
 *
 * The answer may leave the moment out altogether for somebody nobody has reported, as well as
 * send it empty; both mean that nothing was heard, and neither may reach a date as a value.
 */
const heardAt = (lastRecordedAt: string | null | undefined) => ({
  lastRecordedAt: lastRecordedAt ?? null,
});

/**
 * The cards themselves, apart from the section above so that the one clock every age on the
 * screen is measured from runs only while there are ages on the screen.
 */
function WatchedTrips({ trips }: { trips: readonly ExpeditionSurfaceLogTrip[] }) {
  // One present moment for every card: two ages a millisecond apart rounding to different
  // minutes would be one screen disagreeing with itself about how long it has been. And a
  // moment that moves by itself rather than with each fresh answer, because an age that only
  // advanced when the server was asked again would stand still for half a minute at a time, and
  // for as long as the server could not be reached.
  const now = useNow();

  return (
    <Flex vertical gap={12}>
      {trips.map((trip) => (
        <WatchedTrip key={trip.tripLogId} trip={trip} now={now} />
      ))}
    </Flex>
  );
}

function WatchedTrip({ trip, now }: { trip: ExpeditionSurfaceLogTrip; now: number }) {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? i18n.language;
  // Counted from the party as it is drawn below, through the same counter the trip's own
  // tracking uses, rather than read from a second set of figures: the three numbers and the
  // names under them are then one reading of one list and cannot disagree on this card, nor with
  // the trip this card links to.
  const standings = trackingStandings(trip.party);
  const moment = (value: string) =>
    new Date(value).toLocaleString(language, { dateStyle: 'medium', timeStyle: 'short' });
  const lastHeard = lastHeardInWords(heardAt(trip.lastRecordedAt), now, language);

  return (
    <Card
      size="small"
      data-testid={`expedition-watch-trip-${trip.tripLogId}`}
      title={
        // The title is the way to everything this card leaves out — where each person is, the
        // log, the survey — so it opens the trip on its tracking, not on its report.
        <Link
          to={`/trip-logs/${trip.tripLogId}?tab=tracking`}
          data-testid="expedition-watch-trip-link"
          title={t('expeditions.watch.openTracking')}
          style={{ whiteSpace: 'normal' }}
        >
          {trip.title}
        </Link>
      }
      extra={
        // Written out rather than assembled from the state: the check that every key the code
        // asks for exists reads literal calls out of the source.
        trip.state === 'armed' ? (
          <Tag color="blue" data-testid="expedition-watch-state-armed">
            {t('trips.tracking.stateValues.armed')}
          </Tag>
        ) : (
          <Tag data-testid="expedition-watch-state-closed">
            {t('trips.tracking.stateValues.closed')}
          </Tag>
        )
      }
    >
      <Typography.Text type="secondary">
        {formatTripDates(trip.tripDate, trip.tripDateEnd ?? null, language)}
      </Typography.Text>

      <div className="expedition-watch-counts" data-testid="expedition-watch-counts">
        <div className="expedition-watch-count-cell">
          <span className="expedition-watch-count" data-testid="expedition-watch-count-underground">
            {standings.underground}
          </span>
          <Typography.Text type="secondary" className="expedition-watch-count-label">
            {t('trips.tracking.standing.underground')}
          </Typography.Text>
        </div>
        <div className="expedition-watch-count-cell">
          <span className="expedition-watch-count" data-testid="expedition-watch-count-out">
            {standings.out}
          </span>
          <Typography.Text type="secondary" className="expedition-watch-count-label">
            {t('trips.tracking.standing.out')}
          </Typography.Text>
        </div>
        <div className="expedition-watch-count-cell">
          <span className="expedition-watch-count" data-testid="expedition-watch-count-unheard">
            {standings.unheard}
          </span>
          <Typography.Text type="secondary" className="expedition-watch-count-label">
            {t('trips.tracking.standing.unheard')}
          </Typography.Text>
        </div>
      </div>

      <Flex wrap gap="4px 16px" style={{ marginBottom: 8 }}>
        {/* A time and nothing else. Not compared with the clock, not coloured, never "late":
            whether a party is overdue is the callout's to say, and a count that looked like an
            alarm would be trusted as one. Absent when the trip named no hour. */}
        {trip.expectedReturnAt && (
          <Typography.Text data-testid="expedition-watch-expected-return">
            {t('expeditions.watch.expectedReturn')}: {moment(trip.expectedReturnAt)}
          </Typography.Text>
        )}
        <Typography.Text
          data-testid="expedition-watch-last-heard"
          title={trip.lastRecordedAt ? moment(trip.lastRecordedAt) : undefined}
        >
          {t('trips.tracking.columnLastHeard')}: {lastHeard ?? t('expeditions.watch.neverHeard')}
        </Typography.Text>
        {trip.state === 'closed' && trip.closedAt && (
          <Typography.Text type="secondary" data-testid="expedition-watch-closed-at">
            {t('expeditions.watch.closedAt')}: {moment(trip.closedAt)}
          </Typography.Text>
        )}
      </Flex>

      {trip.party.length === 0 ? (
        <Typography.Text type="secondary" data-testid="expedition-watch-party-empty">
          {t('expeditions.watch.partyEmpty')}
        </Typography.Text>
      ) : (
        <ul className="expedition-watch-party" data-testid="expedition-watch-party">
          {trip.party.map((person) => (
            <WatchedPerson key={person.caverId} person={person} now={now} language={language} />
          ))}
        </ul>
      )}
    </Card>
  );
}

function WatchedPerson({
  person,
  now,
  language,
}: {
  person: ExpeditionSurfaceLogPerson;
  now: number;
  language: string;
}) {
  const { t } = useTranslation();
  const standing = trackingStandingOf(person);
  // Written out rather than assembled from the standing, for the same reason as the state above —
  // and these are the words that say whether somebody is still in a cave.
  const label: Record<TrackingStanding, string> = {
    underground: t('trips.tracking.standing.underground'),
    out: t('trips.tracking.standing.out'),
    unheard: t('trips.tracking.standing.unheard'),
  };
  const heard = lastHeardInWords(heardAt(person.lastRecordedAt), now, language);

  return (
    <li className="expedition-watch-person" data-testid={`expedition-watch-person-${person.caverId}`}>
      <span className="expedition-watch-person-name">
        {/* A person the server could give this reader no name for is still a person in the
            count; an empty line beside "Underground" would read as a drawing fault. */}
        {person.name || t('expeditions.watch.unnamed')}
      </span>
      <Tag
        color={standing === 'underground' ? 'blue' : standing === 'out' ? 'green' : 'default'}
        data-testid={`expedition-watch-standing-${standing}`}
      >
        {label[standing]}
      </Tag>
      {/* Silence is drawn as nothing at all: the tag beside it already says nobody has been
          heard from, and any words for an absent moment would be a report nobody made. */}
      {heard !== null && (
        <Typography.Text
          type="secondary"
          title={person.lastRecordedAt ? new Date(person.lastRecordedAt).toLocaleString(language) : undefined}
        >
          {heard}
        </Typography.Text>
      )}
    </li>
  );
}
