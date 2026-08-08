// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import {
  CarOutlined,
  DatabaseOutlined,
  EnvironmentOutlined,
  GoldOutlined,
  NodeIndexOutlined,
  PlusOutlined,
  TableOutlined,
  UploadOutlined,
} from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Card,
  Col,
  Empty,
  Flex,
  Row,
  Skeleton,
  Statistic,
  Switch,
  Typography,
} from 'antd';
import List from '../../components/List.tsx';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import {
  useCan,
  useDashboardSummary,
  useMapViews,
  type DashboardActivityItem,
} from '../../api/hooks.ts';
import { featureDetailPath } from '../../components/features/featureNavigation.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';

const activityIcon: Record<DashboardActivityItem['kind'], ReactNode> = {
  feature: <GoldOutlined />,
  cave: <TableOutlined />,
  caveEntrance: <EnvironmentOutlined />,
  centerline: <NodeIndexOutlined />,
  tripLog: <CarOutlined />,
};

export default function DashboardPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const { data: summary, isLoading, isError, refetch } = useDashboardSummary();
  const { data: views } = useMapViews();
  // Each quick action follows its own domain: a person who may log trips but not
  // create features still gets their button.
  const canCreateFeatures = useCan('features', 'create');
  const canCreateTrips = useCan('tripLogs', 'create');
  const canImportGeofiles = useCan('geofiles', 'create');
  const canCreate = canCreateFeatures || canCreateTrips || canImportGeofiles;
  const landingPage = useUiPrefsStore((s) => s.landingPage);
  const setLandingPage = useUiPrefsStore((s) => s.setLandingPage);

  const formatWhen = (iso: string) => new Date(iso).toLocaleString(i18n.resolvedLanguage);

  // Feed rows link where the record lives. Trip logs have their own pages; everything else
  // is a feature and resolves by kind (entrances/centerlines land on their parent cave).
  const openActivity = (item: DashboardActivityItem) => {
    if (item.kind === 'tripLog') {
      navigate(`/trip-logs/${item.id}`);
      return;
    }
    featureDetailPath(item.kind === 'feature' ? 'generic' : item.kind, item.id)
      .then((path) => navigate(path))
      .catch(() => message.error(t('search.openFailed')));
  };

  const tiles = [
    { key: 'caves', icon: <TableOutlined />, value: summary?.counts.caves, to: '/caves' },
    { key: 'features', icon: <GoldOutlined />, value: summary?.counts.features, to: '/features' },
    { key: 'trips', icon: <CarOutlined />, value: summary?.counts.tripLogs, to: '/trip-logs' },
    { key: 'geodata', icon: <DatabaseOutlined />, value: summary?.counts.geofiles, to: '/geodata' },
  ];

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" wrap gap={12} style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('dashboard.title')}
        </Typography.Title>
        <Flex align="center" gap={8}>
          <Typography.Text type="secondary">{t('dashboard.openOnStart')}</Typography.Text>
          <Switch
            checked={landingPage === 'dashboard'}
            onChange={(checked) => setLandingPage(checked ? 'dashboard' : 'map')}
            aria-label={t('dashboard.openOnStart')}
          />
        </Flex>
      </Flex>

      {isError && (
        <Alert
          type="error"
          showIcon
          style={{ marginBottom: 16 }}
          title={t('dashboard.loadFailed')}
          action={
            <Button size="small" onClick={() => void refetch()}>
              {t('common.retry')}
            </Button>
          }
        />
      )}

      <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
        {tiles.map((tile) => (
          <Col key={tile.key} xs={12} sm={12} md={6}>
            <Card hoverable onClick={() => navigate(tile.to)} styles={{ body: { padding: 20 } }}>
              <Statistic
                title={t(`dashboard.counts.${tile.key}`)}
                // A dash, not a zero: while loading the skeleton covers this, so the only way
                // a count is missing here is that the summary failed — and "0" would read as
                // an empty registry rather than as an unanswered question.
                value={tile.value ?? '—'}
                prefix={tile.icon}
                loading={isLoading}
              />
            </Card>
          </Col>
        ))}
      </Row>

      <Row gutter={[16, 16]}>
        <Col xs={24} lg={14}>
          <Card title={t('dashboard.recentActivity')}>
            {isLoading ? (
              <Skeleton active paragraph={{ rows: 4 }} />
            ) : isError ? (
              // The feed is unknown, not empty — the "nothing added yet" copy below would be
              // a claim about the data that the failed request never made.
              <Empty description={t('dashboard.loadFailed')} />
            ) : summary?.recentActivity.length ? (
              <List
                dataSource={summary.recentActivity}
                renderItem={(item) => (
                  <List.Item
                    style={{ cursor: 'pointer' }}
                    onClick={() => openActivity(item)}
                  >
                    <List.Item.Meta
                      avatar={activityIcon[item.kind]}
                      title={item.name ?? t('dashboard.untitled')}
                      description={`${t(`dashboard.kind.${item.kind}`)} · ${formatWhen(item.updatedAt)}`}
                    />
                  </List.Item>
                )}
              />
            ) : (
              <Empty description={t('dashboard.noActivity')} />
            )}
          </Card>
        </Col>

        <Col xs={24} lg={10}>
          <Flex vertical gap={16}>
            {canCreate && (
              <Card title={t('dashboard.quickActions')}>
                <Flex vertical gap={8} align="stretch">
                  {canCreateFeatures && (
                    <Button type="primary" icon={<PlusOutlined />} onClick={() => navigate('/caves/new')}>
                      {t('caves.newCave')}
                    </Button>
                  )}
                  {/* The trip form is a modal on the list page; the flag opens it on arrival. */}
                  {canCreateTrips && (
                    <Button
                      icon={<PlusOutlined />}
                      onClick={() => navigate('/trip-logs', { state: { create: true } })}
                    >
                      {t('trips.new')}
                    </Button>
                  )}
                  {canImportGeofiles && (
                    <Button icon={<UploadOutlined />} onClick={() => navigate('/geodata')}>
                      {t('dashboard.importFile')}
                    </Button>
                  )}
                </Flex>
              </Card>
            )}

            <Card title={t('dashboard.myViews')}>
              {views?.length ? (
                <Flex vertical gap={8} align="stretch">
                  {views.map((view) => (
                    <Button
                      key={view.id}
                      icon={<EnvironmentOutlined />}
                      onClick={() => navigate(`/map?view=${view.id}`)}
                    >
                      {view.name}
                    </Button>
                  ))}
                </Flex>
              ) : (
                <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('dashboard.noViews')} />
              )}
            </Card>
          </Flex>
        </Col>
      </Row>
    </div>
  );
}
