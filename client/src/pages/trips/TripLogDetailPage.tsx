// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  CameraOutlined,
  DeleteOutlined,
  EditOutlined,
  FileTextOutlined,
  LockOutlined,
} from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Card,
  Descriptions,
  Flex,
  Popconfirm,
  Spin,
  Tabs,
  Tag,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
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
import PermissionsModal from '../../components/permissions/PermissionsModal.tsx';
import TripLibraryPhotoPanel from '../../components/photolibrary/TripLibraryPhotoPanel.tsx';
import LinksSection from '../../components/reslinks/LinksSection.tsx';
import { TRIP_ROLE_CODES } from '../../components/reslinks/relations.ts';
import TagChips from '../../components/tags/TagChips.tsx';
import TripCaveList from '../../components/trips/TripCaveList.tsx';
import TripCover from '../../components/trips/TripCover.tsx';
import TripGallerySection from '../../components/trips/TripGallerySection.tsx';
import TripStateTag from '../../components/trips/TripStateTag.tsx';
import { participantRoleLabel } from '../../components/trips/participantRoles.ts';
import { formatTripDates, formatUndergroundTime, isMultiDay } from '../../components/trips/tripDates.ts';
import { tripTypeLabelOf } from '../../components/trips/tripTypes.ts';
import TripFormModal from './TripFormModal.tsx';
import TripGeometryField from './TripGeometryField.tsx';
import TripChecklistTab from './TripChecklistTab.tsx';
import TripInvitationsTab from './TripInvitationsTab.tsx';
import TripStateControl from './TripStateControl.tsx';
import TripCalloutPanel from '../../components/trips/TripCalloutPanel.tsx';
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

/**
 * The tab keys this page answers to, in the order they are offered. The first is the page's own
 * address and is never written into the URL; every other one is, so a section of a trip can be
 * linked to and survives a reload. Adding a section to a trip is one more entry here and one more
 * component — nothing else about the page has to move.
 *
 * What a trip is — its title, its dates, who was on it, what it names — stays above the strip
 * rather than becoming a tab of its own: it is what a reader came for, and it is what every tab
 * below is about.
 */
const TAB_KEYS = ['report', 'invitations', 'links', 'photos', 'files', 'history'] as const;
type TabKey = (typeof TAB_KEYS)[number];
const DEFAULT_TAB: TabKey = 'report';

const isTabKey = (value: string | null): value is TabKey =>
  value !== null && (TAB_KEYS as readonly string[]).includes(value);

export default function TripLogDetailPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const { id } = useParams<{ id: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
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
  const [permissionsOpen, setPermissionsOpen] = useState(false);

  // An unrecognised key in the address falls back to the page's own tab rather than leaving antd
  // with an activeKey matching no pane, which renders the page with nothing under the tab strip.
  // Read before the page decides it has nothing to draw, so that the hooks above run in the same
  // order on every render whatever the trip's load state.
  const requested = searchParams.get('tab');
  const activeTab: TabKey = isTabKey(requested) ? requested : DEFAULT_TAB;

  if (isPending || !trip) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%' }}>
        <Spin size="large" />
      </Flex>
    );
  }

  const canEdit = held ? held.has('write') : domainFallback;
  const canDelete = held ? held.has('delete') : domainFallback;
  // Naming who may read a trip is its own right, held by the person who made it and by anybody
  // they hand it to — not implied by being able to edit the write-up.
  const canManagePermissions = held ? held.has('managePermissions') : domainFallback;
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
          {/* Who may read this trip, narrowed person by person. Its own right rather than a
              consequence of being able to edit the write-up: handing somebody the text and
              handing them the reader list are two different decisions. A trip contains nothing,
              so the dialog offers reach over this trip alone. */}
          {canManagePermissions && (
            <Button
              icon={<LockOutlined />}
              onClick={() => setPermissionsOpen(true)}
              data-testid="trip-permissions"
            >
              {t('permissions.button')}
            </Button>
          )}
          {(canEdit || canDelete) && (
            <>
              <TripStateControl
                tripId={trip.id}
                state={trip.state}
                visibility={trip.visibility}
                canEdit={canEdit}
              />
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

      {/* Above everything the trip says about itself, because it is the only part of the page
          that can be urgent. Drawn for every reader of the trip and not only for the people on
          it: whether a party is overdue is news to whoever is reading, and the tap that says they
          are out is what is limited to the people who would know. */}
      <TripCalloutPanel
        tripId={trip.id}
        state={trip.calloutState}
        expectedReturnAt={trip.expectedReturnAt}
        calloutAlarmAt={trip.calloutAlarmAt}
        calloutLastCheckedAt={trip.calloutLastCheckedAt}
        canStandDown={trip.canStandDownCallout}
        canEdit={canEdit}
      />

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
          {(trip.caveIds.length > 0 || trip.cavesWithheld > 0) && (
            <Descriptions.Item label={t('trips.caves')}>
              <TripCaveList caveIds={trip.caveIds} withheld={trip.cavesWithheld} />
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

      <Tabs
        // The tab is in the address, so a section of a trip is a place somebody can link to and
        // one that survives a reload. Switching replaces rather than pushes, matching the other
        // addressable tab sets in this application: the back button leaves the trip instead of
        // walking back through the tabs the reader opened on the way.
        activeKey={activeTab}
        onChange={(key) =>
          setSearchParams(key === DEFAULT_TAB ? {} : { tab: key }, { replace: true })
        }
        items={[
          {
            key: 'report',
            label: t('trips.tabReport'),
            children: (
              <>
                {trip.meetingGeom && (
                  <Card
                    size="small"
                    title={t('trips.meetingGeometry')}
                    style={{ marginBottom: 16 }}
                  >
                    {/* Where the party gathers, read-only here for the same reason the sketch is:
                        editing goes through the trip form. It carries the meeting point's own
                        warning rather than the sketch's — this position is told exactly to
                        everybody who may read the trip, which is wider than the set of people the
                        trip will name its caves to, and this card is the only place a reader who
                        cannot edit the trip is told so. */}
                    <TripGeometryField
                      value={trip.meetingGeom}
                      readOnly
                      active={activeTab === 'report'}
                      height={280}
                      testId="trip-meeting-geometry"
                      warningTitle={t('trips.meetingGeometryWarning')}
                      warningDetail={t('trips.meetingGeometryWarningDetail')}
                    />
                  </Card>
                )}
                {/* The trip's own sketch. Editing it goes through the trip form, so the map here
                    draws and does nothing else — but it carries the same warning the editor does,
                    because this is where a reader meets the shape. */}
                {trip.geom && (
                  <Card size="small" title={t('trips.geometry')} style={{ marginBottom: 16 }}>
                    {/* The map is told when its pane is the one on screen. A map built against a
                        container that is not being shown measures nothing and draws a blank tile
                        grid which never repairs itself, and the strip keeps a pane mounted once it
                        has been opened — so being mounted is not the same question as being
                        visible, and this is the answer to the second one. */}
                    <TripGeometryField
                      value={trip.geom}
                      readOnly
                      active={activeTab === 'report'}
                      height={280}
                    />
                  </Card>
                )}

                {/* What the trip found, what it took to get in, and what went wrong — each
                    measured against a schema the trip's purpose carries, so what a club asks a
                    report to record is a club's decision. */}
                <TripSections trip={trip} canEdit={canEdit} />
              </>
            ),
          },
          {
            key: 'invitations',
            label: t('trips.tabInvitations'),
            // Who was asked and what each said, which is intent and never attendance: who
            // actually went is the trip's own list of people above, and one deliberate act turns
            // the first into the second rather than the two drifting into each other.
            children: <TripInvitationsTab trip={trip} canEdit={canEdit} />,
          },
          {
            key: 'checklist',
            label: t('trips.tabChecklist'),
            // What the party settles before it sets off, and how much of it is settled. The
            // figure is advisory: it gates nothing, it is not a state the trip is in, and it is
            // never consulted when working out who may read this page.
            children: <TripChecklistTab trip={trip} canEdit={canEdit} />,
          },
          {
            key: 'links',
            label: t('trips.tabLinks'),
            children: (
              <>
                {/* What the trip did to what it names, role by role — a reading of the one links
                    list, not a second store. */}
                <TripRoleFields tripId={trip.id} tripTitle={trip.title} canEdit={canEdit} />

                {/* Everything else the trip is tied to. The roles above are left out of it: they
                    are the same links, already shown where they say more, and repeating each one
                    here as a bare chip is a duplicate a reader has no way to recognise as one. */}
                <LinksSection
                  entityType="tripLog"
                  entityId={trip.id}
                  canAdd
                  entityTitle={trip.title}
                  excludeRelations={TRIP_ROLE_CODES}
                />
              </>
            ),
          },
          {
            key: 'photos',
            label: t('trips.tabPhotos'),
            children: (
              <>
                {/* The trip's photographs and the albums made from them. The cover is not chosen
                    here: it is the starred attachment, drawn at the head of the page and set from
                    the files tab, and giving the same choice a second control would leave two
                    places disagreeing about which picture the trip is known by. */}
                <TripGallerySection tripId={trip.id} tripTitle={trip.title} />

                {/* And what a neighbouring photo library holds from the days this trip was out.
                    Below the trip's own photographs and clearly separate from them, because they
                    are not the same thing and must not read as one: these are somebody else's
                    archive, nothing here has been filed against the trip, and the only reason they
                    are on this page is that the trip's dates and a camera's clock overlap. The
                    panel draws nothing at all on an installation with no such library.

                    Keyed by the trip, so moving from one trip straight to another starts the panel
                    afresh rather than carrying the page somebody had reached in the first one into
                    a library answer about the second. */}
                <TripLibraryPhotoPanel key={trip.id} tripId={trip.id} />

                {canEdit && (
                  <Flex justify="flex-end" style={{ marginBottom: 16 }}>
                    {/* "These photographs are from this trip" as the way in, so everything the
                        drop creates is filed under the trip in one action rather than a second
                        pass. */}
                    <Button
                      icon={<CameraOutlined />}
                      onClick={() => navigate(`/geodata/photo-import?tripLogId=${trip.id}`)}
                      data-testid="trip-photo-import"
                    >
                      {t('photoImport.openFromTrip')}
                    </Button>
                  </Flex>
                )}
              </>
            ),
          },
          {
            key: 'files',
            label: t('trips.tabFiles'),
            children: (
              <AttachmentSection
                entityType="tripLog"
                entityId={trip.id}
                canEdit={canEdit}
                reportSlot
              />
            ),
          },
          {
            key: 'history',
            label: t('trips.tabHistory'),
            children: (
              <HistoryPanel
                entityType="tripLog"
                entityId={trip.id}
                variant="bare"
                restore={
                  canEdit
                    ? ({
                        entityType: 'TripLog',
                        // Only what the restore names: a section it does not name is left out of
                        // the write entirely rather than sent back as it stands, so putting an old
                        // title back cannot be refused over a report section nobody opened. The
                        // cave list is left out for a stronger reason still — it is not
                        // restorable at all, and the copy loaded here is short of every cave this
                        // reader may not be told about, so echoing it back would read as an
                        // instruction to forget those.
                        onRestore: async (event, props) => {
                          await updateTrip.mutateAsync({
                            id: trip.id,
                            body: applyTripRestore(
                              trip as unknown as TripLogWrite,
                              event.changes,
                              props,
                            ),
                          });
                        },
                      } satisfies HistoryRestore)
                    : undefined
                }
              />
            ),
          },
        ]}
      />

      <TripFormModal open={editing} trip={trip} onClose={() => setEditing(false)} />

      <PermissionsModal
        entityType="tripLog"
        entityId={trip.id}
        open={permissionsOpen}
        onClose={() => setPermissionsOpen(false)}
      />
    </div>
  );
}
