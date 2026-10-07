// SPDX-License-Identifier: AGPL-3.0-or-later
import { Descriptions, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useCavers, useTripLogs, useTripTypes, type TripLogInfo, type TripType } from '../../api/hooks.ts';
import { formatTripDates } from '../../components/trips/tripDates.ts';
import {
  TRIP_SECTIONS,
  tripSectionFieldLabel,
  writtenSectionRows,
  type WrittenSectionRow,
} from '../../components/trips/tripSectionFields.ts';
import { parsePropertiesSchema } from '../../components/typedProperties/propertiesSchema.ts';
import { EXPEDITION_TRIPS_PAGE_SIZE } from './expeditionTrips.ts';

/** What one trip wrote about itself, already reduced to what there is to print. */
interface Account {
  trip: TripLogInfo;
  sections: { key: (typeof TRIP_SECTIONS)[number]; rows: WrittenSectionRow[] }[];
}

function schemaOf(type: TripType | undefined, section: (typeof TRIP_SECTIONS)[number]) {
  return parsePropertiesSchema(
    section === 'fieldData'
      ? type?.fieldDataSchema
      : section === 'logistics'
        ? type?.logisticsSchema
        : type?.safetySchema,
  );
}

/**
 * What each trip gathered into a camp wrote about itself — its account, its results and the
 * answers on its form — trip by trip, as the camp's write-up prints it.
 *
 * <p>
 * The words are the ones each trip's own answer carries for this reader, from the same request
 * the list of the camp's trips makes. Nothing is decided here: a trip this reader may not open
 * is not in that answer, and the account of what went wrong arrives as nothing at all for a
 * reader who may not change the trip, so it is printed for nobody else and no heading stands
 * where it would have been.
 * </p>
 * <p>
 * A trip that wrote nothing gets no heading, and a camp none of whose trips wrote anything gets
 * no part at all: an empty title on a circulated page reads as something somebody removed.
 * </p>
 */
export default function ExpeditionTripAccounts({ expeditionId }: { expeditionId: string }) {
  const { t, i18n } = useTranslation();
  const { data } = useTripLogs({ expeditionId, page: 1, pageSize: EXPEDITION_TRIPS_PAGE_SIZE });
  const { data: tripTypes } = useTripTypes();
  const { data: cavers } = useCavers();

  const accounts: Account[] = [...(data?.items ?? [])]
    // Day by day, as the document orders them, whatever order the list arrived in.
    .sort((a, b) => a.tripDate.localeCompare(b.tripDate) || a.title.localeCompare(b.title))
    .map((trip) => {
      const type = tripTypes?.find((row) => row.id === trip.tripTypeId);
      const sections = TRIP_SECTIONS
        // Nothing at all, not an empty object, is how a part this reader is not given arrives.
        .filter((key) => !(key === 'safety' && trip.safety === null))
        .map((key) => ({ key, rows: writtenSectionRows(trip[key], schemaOf(type, key), cavers, t) }))
        .filter((section) => section.rows.length > 0);
      return { trip, sections };
    })
    .filter(({ trip, sections }) => !!trip.description || !!trip.results || sections.length > 0);

  if (accounts.length === 0) {
    return null;
  }

  return (
    <section
      className="trip-report-section"
      style={{ marginTop: 24 }}
      data-testid="expedition-report-accounts"
    >
      <Typography.Title level={4} style={{ marginBottom: 8 }}>
        {t('expeditions.report.accounts')}
      </Typography.Title>
      <Typography.Paragraph type="secondary">
        {t('expeditions.report.accountsVisibleToYou')}
      </Typography.Paragraph>
      {accounts.map(({ trip, sections }) => (
        <div key={trip.id} style={{ marginTop: 16 }} data-testid={`expedition-report-account-${trip.id}`}>
          <Typography.Title level={5} style={{ marginBottom: 4 }}>
            {formatTripDates(trip.tripDate, trip.tripDateEnd, i18n.resolvedLanguage)} · {trip.title}
          </Typography.Title>
          {trip.description && (
            <Typography.Paragraph style={{ whiteSpace: 'pre-wrap' }}>
              {trip.description}
            </Typography.Paragraph>
          )}
          {trip.results && (
            <>
              <Typography.Text strong>{t('trips.results')}</Typography.Text>
              <Typography.Paragraph style={{ marginTop: 4, whiteSpace: 'pre-wrap' }}>
                {trip.results}
              </Typography.Paragraph>
            </>
          )}
          {sections.map((section) => (
            <div key={section.key} style={{ marginTop: 8 }}>
              <Typography.Text type="secondary">{t(`trips.sections.${section.key}`)}</Typography.Text>
              <Descriptions column={1} size="small" bordered style={{ marginTop: 4 }}>
                {section.rows.map((row) => (
                  <Descriptions.Item key={row.field.key} label={tripSectionFieldLabel(row.field, t)}>
                    {row.text}
                  </Descriptions.Item>
                ))}
              </Descriptions>
            </div>
          ))}
        </div>
      ))}
    </section>
  );
}
