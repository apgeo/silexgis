// SPDX-License-Identifier: AGPL-3.0-or-later
import { Button, Card, Descriptions, Flex, Result, Spin, Tabs, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  parseAccessActions,
  useCan,
  useCavingGroups,
  useEffectiveAccess,
  useExpedition,
} from '../../api/hooks.ts';
import HistoryPanel from '../../components/history/HistoryPanel.tsx';
import LinksSection from '../../components/reslinks/LinksSection.tsx';
import TagChips from '../../components/tags/TagChips.tsx';
// The camp shares the trips' lifecycle vocabulary and their date convention — an absent end
// means "it did not run on past its first day" on both — so the badge and the range are read by
// the same code rather than by a second copy that could drift from it.
import TripStateTag from '../../components/trips/TripStateTag.tsx';
import { formatTripDates, isMultiDay } from '../../components/trips/tripDates.ts';
import ExpeditionFilesTab from './ExpeditionFilesTab.tsx';
import ExpeditionLeadsTab from './ExpeditionLeadsTab.tsx';
import ExpeditionMapTab from './ExpeditionMapTab.tsx';
import ExpeditionRosterTab from './ExpeditionRosterTab.tsx';
import ExpeditionTripsTab from './ExpeditionTripsTab.tsx';

/**
 * The tab keys this page answers to, in the order they are offered. The first is the page's own
 * address and is never written into the URL; every other one is, so a tab can be linked to and
 * survives a reload. Adding a section to the camp is one more entry here and one more component —
 * nothing else about the page has to move.
 */
const TAB_KEYS = ['trips', 'map', 'leads', 'roster', 'files', 'history'] as const;
type TabKey = (typeof TAB_KEYS)[number];
const DEFAULT_TAB: TabKey = 'trips';

const isTabKey = (value: string | null): value is TabKey =>
  value !== null && (TAB_KEYS as readonly string[]).includes(value);

/**
 * One camp: what it is, and the sections that hang off it.
 *
 * A camp the caller may not read and a camp that does not exist are the same answer here, on
 * purpose — the server spells both of them `expedition.not_found`, because an address that
 * answered differently for the two would be an address anybody could probe for the existence of a
 * camp they are not admitted to. So this page never says "you may not read this": it says the
 * camp is not there, which is all it has been told.
 */
export default function ExpeditionDetailPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
  const { data: camp, isPending, isError } = useExpedition(id);
  const { data: cavingGroups } = useCavingGroups();
  // Per-object capabilities once the answer arrives; the coarse domain-level check only bridges
  // the first render (the server enforces regardless).
  const { data: effective } = useEffectiveAccess('expedition', id);
  const domainFallback = useCan('expeditions', 'write');
  const held = effective ? parseAccessActions(effective.actions) : null;

  // An unrecognised key in the address falls back to the page's own tab rather than leaving antd
  // with an activeKey matching no pane, which renders the page with nothing under the tab strip.
  const requested = searchParams.get('tab');
  const activeTab: TabKey = isTabKey(requested) ? requested : DEFAULT_TAB;

  if (isError) {
    return (
      <Result
        status="404"
        title={t('expeditions.notFound')}
        subTitle={t('expeditions.notFoundDetail')}
        extra={
          <Button type="primary" onClick={() => void navigate('/')}>
            {t('common.back')}
          </Button>
        }
      />
    );
  }

  if (isPending || !camp) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%' }}>
        <Spin size="large" />
      </Flex>
    );
  }

  const canEdit = held ? held.has('write') : domainFallback;
  const organizingCavingGroup = cavingGroups?.find((group) => group.id === camp.cavingGroupId);
  const spansDays = isMultiDay(camp.startDate, camp.endDate);
  const dateText = formatTripDates(camp.startDate, camp.endDate, i18n.resolvedLanguage);

  return (
    <div style={{ padding: 24, maxWidth: 1000 }}>
      <Flex justify="space-between" align="center" gap={12} style={{ marginBottom: 12 }}>
        <Flex align="center" gap={8} wrap>
          <Typography.Title level={3} style={{ margin: 0 }} data-testid="expedition-name">
            {camp.name}
          </Typography.Title>
          <TripStateTag state={camp.state} />
        </Flex>
      </Flex>

      <Card size="small">
        <Descriptions column={1} size="small">
          <Descriptions.Item label={spansDays ? t('expeditions.dates') : t('expeditions.date')}>
            <span data-testid="expedition-dates">{dateText}</span>
          </Descriptions.Item>
          {organizingCavingGroup && (
            <Descriptions.Item label={t('expeditions.cavingGroup')}>
              {organizingCavingGroup.name}
            </Descriptions.Item>
          )}
          <Descriptions.Item label={t('features.visibility')}>
            <Tag>{t(`caves.visibilityValues.${camp.visibility}`)}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label={t('tags.title')}>
            <TagChips entityType="expedition" entityId={camp.id} canEdit={canEdit} />
          </Descriptions.Item>
        </Descriptions>
        {camp.description && (
          <>
            <Typography.Text strong>{t('features.description')}</Typography.Text>
            <Typography.Paragraph style={{ marginTop: 4, whiteSpace: 'pre-wrap' }}>
              {camp.description}
            </Typography.Paragraph>
          </>
        )}
      </Card>

      {/* Everything else the camp is tied to — the partner club, the permit, the write-up filed
          somewhere else. Kept beside the camp's own facts rather than inside a tab: a link is how
          a reader leaves this page, and burying it costs the reader the reason they came. */}
      <LinksSection entityType="expedition" entityId={camp.id} canAdd entityTitle={camp.name} />

      <Tabs
        // The tab is in the address, so a section of a camp is a place somebody can link to and
        // one that survives a reload. Switching replaces rather than pushes, matching the one
        // other tab set in this application that is addressable: the back button leaves the camp
        // instead of walking back through the tabs the reader opened on the way.
        activeKey={activeTab}
        onChange={(key) =>
          setSearchParams(key === DEFAULT_TAB ? {} : { tab: key }, { replace: true })
        }
        items={[
          {
            key: 'trips',
            label: t('expeditions.tabTrips'),
            children: <ExpeditionTripsTab expeditionId={camp.id} />,
          },
          {
            key: 'map',
            label: t('expeditions.tabMap'),
            // The map is told when its pane is the one on screen. A map built against a
            // container that is not being shown measures nothing and draws a blank tile grid
            // which never repairs itself, and the strip keeps a pane mounted once it has been
            // opened — so being mounted is not the same question as being visible, and this is
            // the answer to the second one.
            children: <ExpeditionMapTab expeditionId={camp.id} active={activeTab === 'map'} />,
          },
          {
            key: 'leads',
            label: t('expeditions.tabLeads'),
            children: <ExpeditionLeadsTab expeditionId={camp.id} />,
          },
          {
            key: 'roster',
            label: t('expeditions.tabRoster'),
            children: <ExpeditionRosterTab expeditionId={camp.id} />,
          },
          {
            key: 'files',
            label: t('expeditions.tabFiles'),
            children: (
              <ExpeditionFilesTab
                expeditionId={camp.id}
                expeditionName={camp.name}
                canEdit={canEdit}
              />
            ),
          },
          {
            key: 'history',
            label: t('expeditions.tabHistory'),
            children: <HistoryPanel entityType="expedition" entityId={camp.id} variant="bare" />,
          },
        ]}
      />
    </div>
  );
}
