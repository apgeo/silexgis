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
 * What a person, a cave or a club adds up to across trips.
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
          <Col key={tile.key} xs={12} sm={8} md={6}>
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

  tiles.push(
    { key: 'places', label: t('statistics.places'), value: count(data?.places) },
    { key: 'firstVisits', label: t('statistics.firstVisits'), value: count(data?.firstVisits) },
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
  );

  return tiles;
}
