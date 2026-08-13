// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState } from 'react';
import { ArrowLeftOutlined, FileWordOutlined, PrinterOutlined, SaveOutlined } from '@ant-design/icons';
import {
  App,
  Alert,
  Button,
  Descriptions,
  Divider,
  Empty,
  Flex,
  Select,
  Spin,
  Tooltip,
  Typography,
} from 'antd';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';
import { downloadFile, tripReportUrl } from '../../api/download.ts';
import {
  hasAccessAction,
  parseAccessActions,
  useCan,
  useCapabilities,
  useCavers,
  useCavingGroups,
  useEffectiveAccess,
  useKeepTripReport,
  usePhotos,
  useTripLog,
  useTripParticipantRoles,
  useTripReportTemplates,
  useTripTypes,
  type TripLogInfo,
  type TripParticipantRole,
} from '../../api/hooks.ts';
import TripCaveLink from '../../components/trips/TripCaveLink.tsx';
import TripCover from '../../components/trips/TripCover.tsx';
import TripStateTag from '../../components/trips/TripStateTag.tsx';
import { participantRoleLabel } from '../../components/trips/participantRoles.ts';
import { countPeople } from '../../components/trips/roster.ts';
import { formatTripDates, formatUndergroundTime, isMultiDay } from '../../components/trips/tripDates.ts';
import {
  isCaverReferenceField,
  tripSectionFieldLabel,
  tripSectionValueText,
} from '../../components/trips/tripSectionFields.ts';
import { tripTypeLabelOf } from '../../components/trips/tripTypes.ts';
import { parsePropertiesSchema, type SchemaField } from '../../components/typedProperties/propertiesSchema.ts';
import './TripReport.css';
import TripGeometryField from './TripGeometryField.tsx';
import TripRoleFields from './TripRoleFields.tsx';
import { formatPosition, shapeLabelKey, tripGeometrySummary } from './tripGeometrySummary.ts';

/** How many photographs a write-up carries. The rest are one click away in the gallery. */
const PlateCount = 24;

/** The three per-purpose sections, in the order a report is written in. */
const SECTIONS = ['fieldData', 'logistics', 'safety'] as const;
type SectionKey = (typeof SECTIONS)[number];

type Bag = Record<string, unknown>;

const asBag = (value: unknown): Bag =>
  typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Bag) : {};

/** A drawn shape as this reader's language words it, or as it is stored when it has no wording. */
function shapeLabel(type: string, t: TFunction): string {
  const key = shapeLabelKey(type);
  return key ? t(key) : type;
}

/** A titled part of the document, kept whole across a page break. */
function Part({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="trip-report-section" style={{ marginTop: 24 }}>
      <Typography.Title level={4} style={{ marginBottom: 8 }}>
        {title}
      </Typography.Title>
      {children}
    </section>
  );
}

/**
 * One person on the roster, written out in full.
 *
 * Nothing here is clipped or hidden behind a hover: the note is the reason somebody's hours read
 * the way they do, and a report is read on paper as often as on a screen, where a tooltip is not
 * a place a reader can reach.
 */
function personLine(
  person: TripLogInfo['participants'][number],
  roles: TripParticipantRole[] | undefined,
  t: TFunction,
): string {
  const role = roles?.find((r) => r.id === person.roleId);
  const named = role && role.code !== 'participant' && role.code !== 'proposer';
  // Wall-clock strings, shown to the minute and never through a date: they carry no zone.
  const times =
    person.entryTime || person.exitTime
      ? `${person.entryTime?.slice(0, 5) ?? '—'} – ${person.exitTime?.slice(0, 5) ?? '—'}`
      : null;
  return [
    person.name,
    named ? participantRoleLabel(role, t) : null,
    times,
    person.note,
  ]
    .filter(Boolean)
    .join(' · ');
}

/**
 * One trip, laid out as the document a club circulates.
 *
 * <p>
 * The same trip, from the same request the trip page makes, arranged to be read rather than
 * edited: what it was for and when, who was there, where it worked and what it did to what, what
 * it measured, what it left behind, and its pictures. Nothing is re-derived here — every rule
 * about who may see what has already been applied by the time this receives the trip, and a
 * second reading of the same rule on this side is how two surfaces come to disagree about who may
 * see what.
 * </p>
 * <p>
 * That is why this page asks no permission question of its own. The account of what went wrong is
 * withheld by the server from anybody who may not change the trip — it arrives as nothing at all
 * rather than as an empty object — so the report simply prints what it was given, and a reader
 * who was not given that account gets a report without that part rather than an empty heading
 * where it would have been.
 * </p>
 */
export default function TripReportPage() {
  const { t, i18n } = useTranslation();
  const { id } = useParams<{ id: string }>();
  const { data: trip, isPending } = useTripLog(id);
  const { data: tripTypes } = useTripTypes();
  const { data: cavingGroups } = useCavingGroups();
  const { data: participantRoles } = useTripParticipantRoles();
  const { data: cavers } = useCavers();
  const { data: capabilities } = useCapabilities();
  const { data: templates } = useTripReportTemplates();
  // Per-object capabilities once they arrive; the domain check only bridges the first render.
  // It decides nothing — filing a document against the trip is refused by the server for anyone
  // who may not change it, whatever this page offers.
  const { data: effective } = useEffectiveAccess('tripLog', id);
  const domainFallback = useCan('tripLogs', 'write');
  const held = effective ? parseAccessActions(effective.actions) : null;
  const canKeep = held ? held.has('write') : domainFallback;
  const [templateId, setTemplateId] = useState<string | undefined>(undefined);
  const [downloading, setDownloading] = useState(false);
  const keepReport = useKeepTripReport();
  const { message } = App.useApp();
  // The pictures come the way the gallery gets them, through the photographs request, which
  // answers with what this caller may read and nothing else.
  const mayReadPhotos = hasAccessAction(capabilities?.domains.documents, 'read');
  const photosQuery = usePhotos({ tripLogId: id, pageSize: PlateCount }, mayReadPhotos && !!id);

  // Printing has to leave the application's chrome behind, and the rules that do it are scoped to
  // this page rather than let loose over every screen that might one day be printed.
  useEffect(() => {
    document.body.classList.add('trip-report-page');
    return () => document.body.classList.remove('trip-report-page');
  }, []);

  const tripType = tripTypes?.find((row) => row.id === trip?.tripTypeId);
  const schemas = useMemo(
    () => ({
      fieldData: parsePropertiesSchema(tripType?.fieldDataSchema),
      logistics: parsePropertiesSchema(tripType?.logisticsSchema),
      safety: parsePropertiesSchema(tripType?.safetySchema),
    }),
    [tripType],
  );

  if (isPending || !trip) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%' }}>
        <Spin size="large" />
      </Flex>
    );
  }

  const tripTypeLabel = tripTypeLabelOf(trip.tripTypeId, tripTypes, t);
  const organizingCavingGroup = cavingGroups?.find((g) => g.id === trip.organizingCavingGroupId);
  const dateText = formatTripDates(trip.tripDate, trip.tripDateEnd, i18n.resolvedLanguage);
  const timeText = formatUndergroundTime(
    trip.tripDate,
    trip.tripDateEnd,
    trip.entryTime,
    trip.exitTime,
  );
  const roster = [...trip.proposers, ...trip.participants];
  const measured = [
    { key: 'depthReachedM', value: trip.depthReachedM },
    { key: 'lengthSurveyedM', value: trip.lengthSurveyedM },
    { key: 'surveyStations', value: trip.surveyStations },
    { key: 'ropeMetres', value: trip.ropeMetres },
  ].filter((row) => row.value != null);
  const sketch = tripGeometrySummary(trip.geom);
  const photos = photosQuery.data?.items ?? [];

  /** The values a section actually holds, in the order its purpose declares them. */
  const written = (section: SectionKey): { field: SchemaField; text: string }[] => {
    const bag = asBag(trip[section]);
    return schemas[section]
      .map((field) => ({
        field,
        text: isCaverReferenceField(field)
          ? (cavers?.find((caver) => caver.id === bag[field.key])?.name ??
            (bag[field.key] == null ? '—' : String(bag[field.key])))
          : tripSectionValueText(field, bag[field.key], t),
      }))
      .filter((row) => row.text !== '—');
  };

  return (
    <div className="trip-report">
      <Flex
        className="trip-report-noprint"
        justify="space-between"
        align="center"
        gap={8}
        style={{ marginBottom: 16 }}
      >
        <Link to={`/trip-logs/${trip.id}`}>
          <Button icon={<ArrowLeftOutlined />}>{t('trips.report.backToTrip')}</Button>
        </Link>
        <Flex align="center" gap={8} wrap>
          {/* Offered only once a club has written a layout of its own: with nothing to choose
              between, a chooser is a control that can only be left alone. */}
          {(templates?.length ?? 0) > 0 && (
            <Select
              style={{ minWidth: 200 }}
              value={templateId}
              onChange={(value) => setTemplateId(value)}
              data-testid="trip-report-template"
              aria-label={t('trips.report.template')}
              options={[
                { value: undefined, label: t('trips.report.templateDefault') },
                ...(templates ?? []).map((row) => ({ value: row.id, label: row.name })),
              ]}
            />
          )}
          <Button
            icon={<FileWordOutlined />}
            loading={downloading}
            data-testid="trip-report-download"
            onClick={() => {
              setDownloading(true);
              downloadFile(tripReportUrl(trip.id, templateId))
                .catch(() => void message.error(t('trips.report.documentFailed')))
                .finally(() => setDownloading(false));
            }}
          >
            {t('trips.report.download')}
          </Button>
          {canKeep && (
            /* Said before the button rather than after it: what is filed against the trip is
               readable by everybody who may read the trip, so the server builds that copy for
               that audience — which is a narrower document than the one on this screen. */
            <Tooltip title={t('trips.report.keepAudience')}>
              <Button
                icon={<SaveOutlined />}
                loading={keepReport.isPending}
                data-testid="trip-report-keep"
                onClick={() => {
                  keepReport.mutate(
                    { id: trip.id, templateId },
                    {
                      onSuccess: () => void message.success(t('trips.report.kept')),
                      onError: () => void message.error(t('trips.report.documentFailed')),
                    },
                  );
                }}
              >
                {t('trips.report.keep')}
              </Button>
            </Tooltip>
          )}
          <Button
            type="primary"
            icon={<PrinterOutlined />}
            onClick={() => window.print()}
            data-testid="trip-report-print"
          >
            {t('trips.report.print')}
          </Button>
        </Flex>
      </Flex>

      <article data-testid="trip-report">
        <Typography.Title level={2} style={{ marginBottom: 4 }}>
          {trip.title}
        </Typography.Title>
        <Flex align="center" gap={8} wrap style={{ marginBottom: 16 }}>
          <Typography.Text type="secondary">
            {[tripTypeLabel, dateText, organizingCavingGroup?.name].filter(Boolean).join(' · ')}
          </Typography.Text>
          {/* Said on the document itself, because a draft printed and handed round is exactly how
              an unfinished write-up comes to be read as the club's record of the trip. */}
          {trip.state === 'draft' && <TripStateTag state={trip.state} />}
        </Flex>

        <TripCover tripId={trip.id} tripTitle={trip.title} />

        <Descriptions column={1} size="small" bordered style={{ marginTop: 16 }}>
          <Descriptions.Item label={isMultiDay(trip.tripDate, trip.tripDateEnd) ? t('trips.dates') : t('trips.date')}>
            {dateText}
          </Descriptions.Item>
          {timeText && <Descriptions.Item label={t('trips.duration')}>{timeText}</Descriptions.Item>}
          {trip.locationText && (
            <Descriptions.Item label={t('trips.location')}>{trip.locationText}</Descriptions.Item>
          )}
          {/* The caves this trip names, as the trip itself gives them: a cave whose position this
              reader may not place is not in that list at all, and asking for one by another route
              is how it would come back. */}
          {trip.caveIds.length > 0 && (
            <Descriptions.Item label={t('trips.caves')}>
              <Flex gap={8} wrap>
                {trip.caveIds.map((caveId) => (
                  <TripCaveLink key={caveId} caveId={caveId} />
                ))}
              </Flex>
            </Descriptions.Item>
          )}
          {trip.weatherConditions && (
            <Descriptions.Item label={t('trips.weather')}>{trip.weatherConditions}</Descriptions.Item>
          )}
          {trip.hadIncident && (
            <Descriptions.Item label={t('trips.incident')}>
              <span data-testid="trip-report-incident">{t('trips.hadIncidentYes')}</span>
            </Descriptions.Item>
          )}
          {trip.publishedAt && (
            <Descriptions.Item label={t('trips.publishedAt')}>
              {new Date(trip.publishedAt).toLocaleString(i18n.resolvedLanguage)}
            </Descriptions.Item>
          )}
        </Descriptions>

        {(trip.results || trip.description) && (
          <Part title={t('trips.report.account')}>
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
          </Part>
        )}

        {roster.length > 0 && (
          <Part title={t('trips.report.whoWasThere')}>
            {/* People, not rows: somebody who led the trip and surveyed it is two entries on the
                roster and one person underground. */}
            <Typography.Paragraph type="secondary" style={{ marginBottom: 4 }}>
              {t('trips.report.peopleCount', { count: countPeople(roster) })}
            </Typography.Paragraph>
            <ul style={{ margin: 0, paddingInlineStart: 20 }} data-testid="trip-report-roster">
              {roster.map((person) => (
                <li key={`${person.caverId}-${person.roleId}`}>
                  {personLine(person, participantRoles, t)}
                </li>
              ))}
            </ul>
          </Part>
        )}

        {trip.geom && (
          <Part title={t('trips.geometry')}>
            <div className="trip-report-map">
              <TripGeometryField value={trip.geom} readOnly height={280} />
            </div>
            {/* What the printer gets in its place. A trip's own sketch is exact for everyone who
                may read the trip, so writing its position down states nothing the map did not. */}
            {sketch && (
              <div className="trip-report-map-fallback" data-testid="trip-report-sketch">
                <Typography.Text>
                  {t(
                    sketch.positions === 1
                      ? 'trips.report.sketchPoint'
                      : 'trips.report.sketchShape',
                    {
                      shape: shapeLabel(sketch.type, t),
                      position: formatPosition(sketch.center, {
                        north: t('trips.report.north'),
                        south: t('trips.report.south'),
                        east: t('trips.report.east'),
                        west: t('trips.report.west'),
                      }),
                      count: sketch.positions,
                    },
                  )}
                </Typography.Text>
              </div>
            )}
            <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
              {t('trips.geometryWarning')}
            </Typography.Paragraph>
          </Part>
        )}

        {/* What the trip did to what it names — the same reading of the links list the trip page
            draws, with nothing on it to write with. */}
        <div className="trip-report-section">
          <TripRoleFields tripId={trip.id} tripTitle={trip.title} canEdit={false} />
        </div>

        {measured.length > 0 && (
          <Part title={t('trips.sections.measured')}>
            <Descriptions column={1} size="small" bordered>
              {measured.map((row) => (
                <Descriptions.Item key={row.key} label={t(`trips.${row.key}`)}>
                  {row.key === 'surveyStations'
                    ? row.value
                    : t('trips.metres', { value: row.value })}
                </Descriptions.Item>
              ))}
            </Descriptions>
          </Part>
        )}

        {SECTIONS.map((section) => {
          // The account of what went wrong arrives as nothing at all for a reader who may not
          // change the trip — not as an empty object — so this prints no heading for it rather
          // than an empty one, which on a circulated document would read as "nothing happened".
          if (section === 'safety' && trip.safety === null) {
            return null;
          }
          const rows = written(section);
          if (rows.length === 0) {
            return null;
          }
          return (
            <Part key={section} title={t(`trips.sections.${section}`)}>
              <Descriptions column={1} size="small" bordered data-testid={`trip-report-${section}`}>
                {rows.map((row) => (
                  <Descriptions.Item key={row.field.key} label={tripSectionFieldLabel(row.field, t)}>
                    {row.text}
                  </Descriptions.Item>
                ))}
              </Descriptions>
            </Part>
          );
        })}

        {mayReadPhotos && (
          <Part title={t('trips.gallery')}>
            {photosQuery.isPending ? (
              <Spin />
            ) : photos.length === 0 ? (
              <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('trips.noPhotographs')} />
            ) : (
              <>
                {/* Two people printing the same report get two sets of pictures, and the document
                    says so rather than letting the difference read as a fault. */}
                <Typography.Paragraph type="secondary">
                  {t('trips.galleryVisibleToYou')}
                </Typography.Paragraph>
                <div className="trip-report-plates" data-testid="trip-report-plates">
                  {photos.map((photo) => (
                    <figure key={photo.documentId} className="trip-report-plate">
                      {/* The rendering, never the stored file: a caller who may see the trip is
                          not thereby entitled to the original, which carries where it was taken. */}
                      {photo.thumbnailUrl && (
                        <img src={photo.thumbnailUrl} alt={photo.credit.caption ?? photo.title} />
                      )}
                      <figcaption>
                        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                          {[photo.credit.caption ?? photo.title, photo.credit.photographerName]
                            .filter(Boolean)
                            .join(' — ')}
                        </Typography.Text>
                      </figcaption>
                    </figure>
                  ))}
                </div>
              </>
            )}
          </Part>
        )}

        <Divider />
        <Alert
          className="trip-report-noprint"
          type="info"
          showIcon
          title={t('trips.report.audienceNotice')}
        />
      </article>
    </div>
  );
}
