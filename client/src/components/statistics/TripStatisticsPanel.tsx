// SPDX-License-Identifier: AGPL-3.0-or-later
import { DownloadOutlined } from '@ant-design/icons';
import { App, Button, Card, Col, Row, Statistic, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import { downloadFile, tripStatisticsExportUrl } from '../../api/download.ts';
import { useTripStatistics, type StatisticsSubject, type TripStatistics } from '../../api/hooks.ts';

interface TripStatisticsPanelProps {
  subject: StatisticsSubject;
  id: string;
}

/** One figure and what it is called. */
interface Tile {
  key: string;
  label: string;
  value: string | number;
}

/**
 * What a person, a cave, a club or a camp adds up to across trips.
 *
 * Every figure here is counted over the trips this reader may see, and the panel says so in words
 * underneath them. That sentence is load-bearing, not decoration: two colleagues comparing their
 * screens will find different totals for the same person, which is correct and looks exactly like a
 * bug — and the repair somebody reaches for is to stop filtering, which would turn a page of
 * figures into a way of counting rows nobody was going to be shown.
 *
 * Nothing here is stored anywhere. A total is worked out when it is asked for, so an older trip
 * typed up next week takes its place in the history rather than contradicting a number written
 * before it existed.
 */
export default function TripStatisticsPanel({ subject, id }: TripStatisticsPanelProps) {
  const { t } = useTranslation();
  // Through the app's own message holder rather than the static one: a static call sits outside
  // the theme and locale context and antd says so on the console every time it is made.
  const { message } = App.useApp();
  const { data, isLoading, isError } = useTripStatistics(subject, id);

  // A caller who may not read the subject is refused by the server, and then there is nothing
  // honest to show — an empty set of figures would read as "this person has done nothing".
  if (isError) {
    return null;
  }

  const onExport = () => {
    downloadFile(tripStatisticsExportUrl(subject, id)).catch(() => message.error(t('common.saveFailed')));
  };

  const body = (
    <div data-testid="trip-statistics">
      <Row gutter={[16, 16]}>
        {tilesFor(subject, data, t).map((tile) => (
          <Col key={tile.key} xs={12} sm={8} md={6} data-testid={`trip-statistics-${tile.key}`}>
            <Statistic title={tile.label} value={tile.value} loading={isLoading} />
          </Col>
        ))}
      </Row>
      <Typography.Paragraph type="secondary" style={{ marginTop: 16, marginBottom: 0 }}>
        {t('statistics.asVisibleToYou')}
      </Typography.Paragraph>
      {data && data.timedPersonTrips < data.personTrips && (
        <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
          {t('statistics.hoursPartial', { counted: data.timedPersonTrips, total: data.personTrips })}
        </Typography.Paragraph>
      )}
      {/* Said wherever a tracked trip is counted, whether or not its log came to any hours: the
          two hours tiles stand a few centimetres apart and the first thing a reader does with two
          figures in the same unit is add them. Where a trip has both a roster with times and a
          log they are the same hours, so the sentence says outright that they are not summed —
          and how many of the times somebody went the log's figure rests on. */}
      {data && data.trackedTrips > 0 && (
        <Typography.Paragraph
          type="secondary"
          style={{ marginBottom: 0 }}
          data-testid="trip-statistics-watch-note"
        >
          {t('statistics.watchNote', { counted: data.watchTimedPersonTrips })}
        </Typography.Paragraph>
      )}
      {data?.earliestTripDate && data.latestTripDate && (
        <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
          {t('statistics.span', { from: data.earliestTripDate, to: data.latestTripDate })}
        </Typography.Paragraph>
      )}
    </div>
  );

  return (
    <Card
      size="small"
      title={t('statistics.title')}
      style={{ marginTop: 16 }}
      extra={
        <Button size="small" icon={<DownloadOutlined />} onClick={onExport} disabled={!data}>
          {t('common.export')}
        </Button>
      }
    >
      {body}
    </Card>
  );
}

/**
 * The figures worth a tile, per subject.
 *
 * A dash where a figure is not in yet, never a zero: "0 trips" is a statement about a person and
 * this component must not make it while it is still asking.
 */
function tilesFor(
  subject: StatisticsSubject,
  data: TripStatistics | undefined,
  t: (key: string, options?: Record<string, unknown>) => string,
): Tile[] {
  const dash = '—';
  const count = (value: number | undefined) => value ?? dash;
  const metres = (value: number | undefined) =>
    value === undefined ? dash : t('trips.metres', { value });

  const tiles: Tile[] = [
    { key: 'trips', label: t('statistics.trips'), value: count(data?.trips) },
  ];

  // How many people were on a person's own trips is a fact about their company rather than about
  // them, and reading it beside their name invites it to be taken for something it is not.
  if (subject !== 'caver') {
    tiles.push({ key: 'people', label: t('statistics.people'), value: count(data?.people) });
  }

  // Two figures are true of a cave and say nothing about it, so its panel leaves them out. Every
  // trip counted for a cave names that cave, and its places are narrowed to that cave alone — so
  // "places" can only read one, or none while nobody has been. And everybody on those trips
  // reached the cave for the first time on one of them, so "first visits" can only repeat the
  // people figure standing beside it. A tile that cannot differ from its neighbour still reads as
  // a second fact. Both stay wherever they can vary: for a person, a club and a camp.
  if (subject !== 'cave') {
    tiles.push(
      { key: 'places', label: t('statistics.places'), value: count(data?.places) },
      { key: 'firstVisits', label: t('statistics.firstVisits'), value: count(data?.firstVisits) },
    );
  }

  tiles.push(
    {
      key: 'hours',
      label: t('statistics.hoursUnderground'),
      value:
        data === undefined
          ? dash
          : // One decimal: the underlying figure is minutes, and an hours total printed to the
            // minute invites a precision the missing times below it do not support.
            t('statistics.hours', { value: Math.round(data.undergroundMinutes / 6) / 10 }),
    },
    { key: 'surveyed', label: t('statistics.metresSurveyed'), value: metres(data?.lengthSurveyedM) },
    { key: 'rope', label: t('statistics.metresOfRope'), value: metres(data?.ropeMetresM) },
    { key: 'stations', label: t('statistics.surveyStations'), value: count(data?.surveyStations) },
    { key: 'incidents', label: t('statistics.incidents'), value: count(data?.incidents) },
    // Counted over the pictures this reader may see, like everything beside it — the sentence
    // under the tiles covers this one too.
    { key: 'photographs', label: t('statistics.photographs'), value: count(data?.photographs) },
    // What the tracking logs say, after everything the trips and their rosters say and under
    // labels that name the source. The hours are never folded into the tile above: a log and a
    // roster with times describe the same hours, and one figure made of both would count them
    // twice wherever a trip has the two.
    { key: 'trackedTrips', label: t('statistics.trackedTrips'), value: count(data?.trackedTrips) },
    {
      key: 'watchHours',
      label: t('statistics.watchHoursUnderground'),
      value:
        // A dash, not "0 h", where no log holds an entry that an exit followed. A party whose
        // exits were never written down did not spend no time underground — the log does not
        // know how long, and nought is a duration.
        data === undefined || data.watchTimedPersonTrips === 0
          ? dash
          : t('statistics.hours', { value: Math.round(data.watchUndergroundMinutes / 6) / 10 }),
    },
  );

  return tiles;
}
