// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { CameraOutlined, DeleteOutlined, EditOutlined, FileTextOutlined } from '@ant-design/icons';
import { Alert, App, Button, Card, Descriptions, Flex, Popconfirm, Spin, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router-dom';
import {
  parseAccessActions,
  useCan,
  useCavingGroups,
  useDeleteTripLog,
  useEffectiveAccess,
  useTripLog,
  useTripParticipantRoles,
  useTripTypes,
  useUpdateTripLog,
  type TripLogInfo,
  type TripLogWrite,
  type TripParticipantRole,
} from '../../api/hooks.ts';
import AttachmentSection from '../../components/attachments/AttachmentSection.tsx';
import HistoryPanel, { type HistoryRestore } from '../../components/history/HistoryPanel.tsx';
import { applyTripRestore } from '../../components/history/historyModel.ts';
import LinksSection from '../../components/reslinks/LinksSection.tsx';
import { TRIP_ROLE_CODES } from '../../components/reslinks/relations.ts';
import TagChips from '../../components/tags/TagChips.tsx';
import TripCaveLink from '../../components/trips/TripCaveLink.tsx';
import TripCover from '../../components/trips/TripCover.tsx';
import TripGallerySection from '../../components/trips/TripGallerySection.tsx';
import TripStateTag from '../../components/trips/TripStateTag.tsx';
import { participantRoleLabel } from '../../components/trips/participantRoles.ts';
import { formatTripDates, formatUndergroundTime, isMultiDay } from '../../components/trips/tripDates.ts';
import { tripTypeLabelOf } from '../../components/trips/tripTypes.ts';
import TripFormModal from './TripFormModal.tsx';
import TripGeometryField from './TripGeometryField.tsx';
import TripStateControl from './TripStateControl.tsx';
import TripRoleFields from './TripRoleFields.tsx';
import TripSections from './TripSections.tsx';

/**
 * One person on the roster: their name, and only what distinguishes them from everybody else on
 * the trip. Having simply been there is what the roster already says, so that role is left
 * unwritten; hours are written only when they were not the party's, and the note is written on
 * the tag itself rather than spent as a line of its own.
 *
 * The note is written out, not hidden behind a hover. "Turned back at the pitch head" is the
 * reason the hours read the way they do, and a reader who cannot edit the trip has no other way
 * to reach it — on a touch screen, no way at all. A long one is clipped to keep the row of tags
 * readable, and the full text is then a tap or a hover away.
 */
function PersonTag({
  person,
  roles,
}: {
  person: TripLogInfo['participants'][number];
  roles: TripParticipantRole[] | undefined;
}) {
  const { t } = useTranslation();
  const role = roles?.find((r) => r.id === person.roleId);
  const named = role && role.code !== 'participant' && role.code !== 'proposer';
  // Wall-clock strings, shown to the minute and never through a date: they carry no zone and
  // must not be read as though they did.
  const times =
    person.entryTime || person.exitTime
      ? `${person.entryTime?.slice(0, 5) ?? '—'} – ${person.exitTime?.slice(0, 5) ?? '—'}`
      : null;
  return (
    <Tag>
      {person.name}
      {named && ` · ${participantRoleLabel(role, t)}`}
      {times && ` · ${times}`}
      {person.note && (
        <>
          {' · '}
          <Typography.Text
            type="secondary"
            ellipsis={{ tooltip: person.note }}
            style={{ maxWidth: 220, display: 'inline-block', verticalAlign: 'bottom' }}
            data-testid="roster-note"
          >
            {person.note}
          </Typography.Text>
        </>
      )}
    </Tag>
  );
}

export default function TripLogDetailPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const { id } = useParams<{ id: string }>();
  const { data: trip, isPending } = useTripLog(id);
  const { data: cavingGroups } = useCavingGroups();
  const { data: tripTypes } = useTripTypes();
  const { data: participantRoles } = useTripParticipantRoles();
  const organizingCavingGroup = cavingGroups?.find((g) => g.id === trip?.organizingCavingGroupId);
  const deleteTrip = useDeleteTripLog();
  const updateTrip = useUpdateTripLog();
  // Per-object capabilities once the answer arrives; the coarse domain-level check only
  // bridges the first render (the server enforces regardless).
  const { data: effective } = useEffectiveAccess('tripLog', id);
  const domainFallback = useCan('tripLogs', 'write');
  const held = effective ? parseAccessActions(effective.actions) : null;
  const [editing, setEditing] = useState(false);

  if (isPending || !trip) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%' }}>
        <Spin size="large" />
      </Flex>
    );
  }

  const canEdit = held ? held.has('write') : domainFallback;
  const canDelete = held ? held.has('delete') : domainFallback;
  // Absent while the vocabulary is still loading, which is right: an identity is not a label,
  // and showing the raw number would be worse than showing nothing for the moment it takes.
  const tripTypeLabel = tripTypeLabelOf(trip.tripTypeId, tripTypes, t);
  const spansDays = isMultiDay(trip.tripDate, trip.tripDateEnd);
  const dateText = formatTripDates(trip.tripDate, trip.tripDateEnd, i18n.resolvedLanguage);
  const timeText = formatUndergroundTime(
    trip.tripDate,
    trip.tripDateEnd,
    trip.entryTime,
    trip.exitTime,
  );

  const onDelete = async () => {
    try {
      await deleteTrip.mutateAsync(trip.id);
      message.success(t('common.deleted'));
      navigate('/trip-logs');
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <div style={{ padding: 24, maxWidth: 900 }}>
      <Flex justify="space-between" align="center" gap={12} style={{ marginBottom: 12 }}>
        {/* The badge sits with the title rather than down among the details: whether this has
            gone out yet is the first thing an author needs from the page. */}
        <Flex align="center" gap={8} wrap>
          <Typography.Title level={3} style={{ margin: 0 }}>
            {trip.title}
          </Typography.Title>
          <TripStateTag state={trip.state} />
        </Flex>
        <Flex gap={8}>
          {/* The write-up as a document rather than a form, for anybody who may read the trip:
              circulating one is not an act of editing it. */}
          <Link to={`/trip-logs/${trip.id}/report`}>
            <Button icon={<FileTextOutlined />} data-testid="trip-open-report">
              {t('trips.report.open')}
            </Button>
          </Link>
          {(canEdit || canDelete) && (
            <>
              <TripStateControl tripId={trip.id} state={trip.state} canEdit={canEdit} />
              {canEdit && (
                <Button icon={<EditOutlined />} onClick={() => setEditing(true)}>
                  {t('trips.edit')}
                </Button>
              )}
              {canDelete && (
                <Popconfirm title={t('trips.deleteConfirm')} onConfirm={() => void onDelete()}>
                  <Button danger icon={<DeleteOutlined />}>
                    {t('features.delete')}
                  </Button>
                </Popconfirm>
              )}
            </>
          )}
        </Flex>
      </Flex>

      {/* Said in words as well as shown as a badge, and only to somebody who could act on it.
          A draft is not hidden from anyone its visibility admits — being unfinished is not a
          permission — so the author is told plainly that nobody has been notified yet, rather
          than being left to infer it from a grey tag. */}
      {trip.state === 'draft' && canEdit && (
        <Alert
          type="info"
          showIcon
          title={t('trips.draftNotice')}
          style={{ marginBottom: 12 }}
          data-testid="trip-draft-notice"
        />
      )}

      {/* The one picture the trip is known by, where a reader meets it first. Chosen by the star
          on the attachments section below rather than here — one control, one answer — and read
          through that section's own request, so a cover cannot survive a rule that hides the
          picture it is made of. */}
      <TripCover tripId={trip.id} tripTitle={trip.title} />

      <Card size="small">
        <Descriptions column={1} size="small">
          {tripTypeLabel && (
            <Descriptions.Item label={t('trips.type')}>
              <Tag>{tripTypeLabel}</Tag>
            </Descriptions.Item>
          )}
          <Descriptions.Item label={spansDays ? t('trips.dates') : t('trips.date')}>
            {dateText}
          </Descriptions.Item>
          {timeText && <Descriptions.Item label={t('trips.duration')}>{timeText}</Descriptions.Item>}
          {trip.locationText && (
            <Descriptions.Item label={t('trips.location')}>{trip.locationText}</Descriptions.Item>
          )}
          {organizingCavingGroup && (
            <Descriptions.Item label={t('trips.organizingCavingGroup')}>
              {organizingCavingGroup.name}
            </Descriptions.Item>
          )}
          {trip.caveIds.length > 0 && (
            <Descriptions.Item label={t('trips.caves')}>
              <Flex gap={8} wrap>
                {trip.caveIds.map((caveId) => (
                  <TripCaveLink key={caveId} caveId={caveId} />
                ))}
              </Flex>
            </Descriptions.Item>
          )}
          {trip.participants.length > 0 && (
            <Descriptions.Item label={t('trips.participants')}>
              <Flex gap={4} wrap>
                {trip.participants.map((p) => (
                  <PersonTag key={`${p.caverId}-${p.roleId}`} person={p} roles={participantRoles} />
                ))}
              </Flex>
            </Descriptions.Item>
          )}
          {trip.proposers.length > 0 && (
            <Descriptions.Item label={t('trips.proposers')}>
              <Flex gap={4} wrap>
                {trip.proposers.map((p) => (
                  <PersonTag key={`${p.caverId}-${p.roleId}`} person={p} roles={participantRoles} />
                ))}
              </Flex>
            </Descriptions.Item>
          )}
          {trip.weatherConditions && (
            <Descriptions.Item label={t('trips.weather')}>{trip.weatherConditions}</Descriptions.Item>
          )}
          {/* Metres, always: the number is stored in one unit and written in the reader's
              locale, so nothing here has to be trusted to say which unit it meant. */}
          {trip.depthReachedM != null && (
            <Descriptions.Item label={t('trips.depthReachedM')}>
              <span data-testid="trip-depth-reached">
                {t('trips.metres', { value: trip.depthReachedM })}
              </span>
            </Descriptions.Item>
          )}
          {trip.lengthSurveyedM != null && (
            <Descriptions.Item label={t('trips.lengthSurveyedM')}>
              {t('trips.metres', { value: trip.lengthSurveyedM })}
            </Descriptions.Item>
          )}
          {trip.surveyStations != null && (
            <Descriptions.Item label={t('trips.surveyStations')}>{trip.surveyStations}</Descriptions.Item>
          )}
          {trip.ropeMetres != null && (
            <Descriptions.Item label={t('trips.ropeMetres')}>
              {t('trips.metres', { value: trip.ropeMetres })}
            </Descriptions.Item>
          )}
          {/* Said only when it is true. That something went wrong is on the trip's own
              visibility so a club can count it; the account of what went wrong is not, and
              lives in the safety section below where its narrower audience is drawn. */}
          {trip.hadIncident && (
            <Descriptions.Item label={t('trips.incident')}>
              <Tag color="warning" data-testid="trip-had-incident">
                {t('trips.hadIncidentYes')}
              </Tag>
            </Descriptions.Item>
          )}
          {/* Kept from the first announcement even after the trip goes back to draft, so
              "when did this go out" keeps the answer the people who were told would give. */}
          {trip.publishedAt && (
            <Descriptions.Item label={t('trips.publishedAt')}>
              {new Date(trip.publishedAt).toLocaleString(i18n.resolvedLanguage)}
            </Descriptions.Item>
          )}
          <Descriptions.Item label={t('features.visibility')}>
            <Tag>{t(`caves.visibilityValues.${trip.visibility}`)}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label={t('tags.title')}>
            <TagChips entityType="tripLog" entityId={trip.id} canEdit={canEdit} />
          </Descriptions.Item>
        </Descriptions>
        {trip.results && (
          <>
            <Typography.Text strong>{t('trips.results')}</Typography.Text>
            <Typography.Paragraph style={{ marginTop: 4, whiteSpace: 'pre-wrap' }}>
              {trip.results}
            </Typography.Paragraph>
          </>
        )}
        {trip.description && (
          <>
            <Typography.Text strong>{t('features.description')}</Typography.Text>
            <Typography.Paragraph style={{ marginTop: 4, whiteSpace: 'pre-wrap' }}>
              {trip.description}
            </Typography.Paragraph>
          </>
        )}
      </Card>

      {/* The trip's own sketch. Editing it goes through the trip form, so the map here draws and
          does nothing else — but it carries the same warning the editor does, because this is
          where a reader meets the shape. */}
      {trip.geom && (
        <Card size="small" title={t('trips.geometry')} style={{ marginTop: 16 }}>
          <TripGeometryField value={trip.geom} readOnly height={280} />
        </Card>
      )}

      {/* What the trip found, what it took to get in, and what went wrong — each measured
          against a schema the trip's purpose carries, so what a club asks a report to record
          is a club's decision. */}
      <TripSections trip={trip} canEdit={canEdit} />

      {/* What the trip did to what it names, role by role — a reading of the one links list,
          not a second store. */}
      <TripRoleFields tripId={trip.id} tripTitle={trip.title} canEdit={canEdit} />

      {/* Everything else the trip is tied to. The roles above are left out of it: they are the
          same links, already shown where they say more, and repeating each one here as a bare
          chip is a duplicate a reader has no way to recognise as one. */}
      <LinksSection
        entityType="tripLog"
        entityId={trip.id}
        canAdd
        entityTitle={trip.title}
        excludeRelations={TRIP_ROLE_CODES}
      />

      {/* The trip's photographs and the albums made from them. The cover is not chosen here: it
          is the starred attachment, drawn at the head of the page and set from the attachments
          section below, and giving the same choice a second control would leave two places
          disagreeing about which picture the trip is known by. */}
      <TripGallerySection tripId={trip.id} tripTitle={trip.title} />

      <AttachmentSection entityType="tripLog" entityId={trip.id} canEdit={canEdit} reportSlot />

      {canEdit && (
        <Flex justify="flex-end" style={{ marginBottom: 16 }}>
          {/* "These photographs are from this trip" as the way in, so everything the drop
              creates is filed under the trip in one action rather than a second pass. */}
          <Button
            icon={<CameraOutlined />}
            onClick={() => navigate(`/geodata/photo-import?tripLogId=${trip.id}`)}
            data-testid="trip-photo-import"
          >
            {t('photoImport.openFromTrip')}
          </Button>
        </Flex>
      )}

      <HistoryPanel
        entityType="tripLog"
        entityId={trip.id}
        restore={
          canEdit
            ? ({
                entityType: 'TripLog',
                // Only what the restore names: a section it does not name is left out of the
                // write entirely rather than sent back as it stands, so putting an old title
                // back cannot be refused over a report section nobody opened.
                onRestore: async (event, props) => {
                  await updateTrip.mutateAsync({
                    id: trip.id,
                    body: applyTripRestore(trip as unknown as TripLogWrite, event.changes, props),
                  });
                },
              } satisfies HistoryRestore)
            : undefined
        }
      />

      <TripFormModal open={editing} trip={trip} onClose={() => setEditing(false)} />
    </div>
  );
}
