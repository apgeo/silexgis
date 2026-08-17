// SPDX-License-Identifier: AGPL-3.0-or-later
import { Empty, Spin, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { useTripLogs } from '../../api/hooks.ts';
import List from '../../components/List.tsx';
import TripStatisticsPanel from '../../components/statistics/TripStatisticsPanel.tsx';
import TripStateTag from '../../components/trips/TripStateTag.tsx';
import { formatTripDates } from '../../components/trips/tripDates.ts';

const PAGE_SIZE = 50;

/**
 * The trips gathered into one camp, and what they add up to.
 *
 * Both halves are counted over the trips *this* reader may see, so two people looking at the same
 * camp will find different lists and different totals, and both are right. The roll-up says so in
 * words of its own; the list says it here, because an empty list on a camp somebody knows ran for
 * a fortnight otherwise reads as data loss rather than as an answer about the reader.
 */
export default function ExpeditionTripsTab({ expeditionId }: { expeditionId: string }) {
  const { t, i18n } = useTranslation();
  const { data, isPending } = useTripLogs({ expeditionId, page: 1, pageSize: PAGE_SIZE });

  return (
    <div data-testid="expedition-trips-tab">
      {isPending ? (
        <Spin />
      ) : !data || data.items.length === 0 ? (
        <Empty description={t('expeditions.noTripsVisible')} />
      ) : (
        <List
          size="small"
          dataSource={data.items}
          renderItem={(trip) => (
            <List.Item>
              <List.Item.Meta
                title={<Link to={`/trip-logs/${trip.id}`}>{trip.title}</Link>}
                description={formatTripDates(trip.tripDate, trip.tripDateEnd, i18n.resolvedLanguage)}
              />
              <TripStateTag state={trip.state} />
            </List.Item>
          )}
        />
      )}
      {data && data.totalItems > data.items.length && (
        <Typography.Paragraph type="secondary" style={{ marginTop: 8 }}>
          {t('expeditions.tripsTruncated', { shown: data.items.length, total: data.totalItems })}
        </Typography.Paragraph>
      )}

      <TripStatisticsPanel subject="expedition" id={expeditionId} />
    </div>
  );
}
