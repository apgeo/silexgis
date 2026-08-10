// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  DeleteOutlined,
  EditOutlined,
  EnvironmentOutlined,
  LockOutlined,
  PlusOutlined,
  ShareAltOutlined,
} from '@ant-design/icons';
import {
  Alert,
  App,
  Breadcrumb,
  Button,
  Card,
  Descriptions,
  Flex,
  Popconfirm,
  Spin,
  Table,
  Tag,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import {
  useCave,
  useCaveSummary,
  useCaveTypes,
  useDeleteCave,
  useDeleteEntrance,
  useEntranceTypes,
  useEntrances,
  useCan,
  useRockTypes,
  useUpdateCave,
  useUpdateEntrance,
  type CaveWrite,
  type Entrance,
  type EntranceWrite,
} from '../../api/hooks.ts';
import { formatLonLat } from '../../geo/coords.ts';
import AttachmentSection from '../../components/attachments/AttachmentSection.tsx';
import HistoryPanel, { type HistoryRestore } from '../../components/history/HistoryPanel.tsx';
import { applyRestore } from '../../components/history/historyModel.ts';
import PermissionsModal from '../../components/permissions/PermissionsModal.tsx';
import LinksSection from '../../components/reslinks/LinksSection.tsx';
import ShareLinksModal from '../../components/shares/ShareLinksModal.tsx';
import TagChips from '../../components/tags/TagChips.tsx';
import CenterlineSection from './CenterlineSection.tsx';
import EntranceEditorModal from '../../components/caves/EntranceEditorModal.tsx';
import SurveyModelSection from './SurveyModelSection.tsx';

export default function CaveDetailPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const { id } = useParams<{ id: string }>();

  const { data: cave, isPending } = useCave(id);
  const { data: summary } = useCaveSummary(id);
  const { data: entrances } = useEntrances(id);
  const { data: caveTypes } = useCaveTypes();
  const { data: rockTypes } = useRockTypes();
  const { data: entranceTypes } = useEntranceTypes();
  const deleteCave = useDeleteCave();
  const deleteEntrance = useDeleteEntrance(id ?? '');
  const updateCave = useUpdateCave(id ?? '');
  const updateEntrance = useUpdateEntrance(id ?? '');
  // Per-object capabilities from the summary once loaded; the coarse domain-level
  // capability only bridges the first render (the server enforces regardless).
  const domainFallback = useCan('features', 'write');
  const canEdit = summary?.permissions.canWrite ?? domainFallback;
  const canDelete = summary?.permissions.canDelete ?? domainFallback;
  const canShare = summary?.permissions.canShare ?? domainFallback;
  const canManagePermissions = summary?.permissions.canManagePermissions ?? domainFallback;

  const [editorOpen, setEditorOpen] = useState(false);
  const [editingEntrance, setEditingEntrance] = useState<Entrance | null>(null);
  const [permissionsOpen, setPermissionsOpen] = useState(false);
  const [shareOpen, setShareOpen] = useState(false);

  if (isPending || !cave) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%' }}>
        <Spin size="large" />
      </Flex>
    );
  }

  const caveTypeName = caveTypes?.find((x) => x.id === cave.caveTypeId)?.name;
  const rockTypeName = rockTypes?.find((x) => x.id === cave.rockTypeId)?.name;
  const entranceTypeName = (typeId: number) => entranceTypes?.find((x) => x.id === typeId)?.name ?? '';

  const detailItem = (label: string, value: unknown) =>
    value == null || value === '' ? null : (
      <Descriptions.Item key={label} label={label}>
        {String(value)}
      </Descriptions.Item>
    );

  // The write DTO addresses containment by primary-parent id, which the read DTO carries
  // as the parents breadcrumb — map it back so an update/restore keeps the cave where it is.
  const caveAsWrite = (): CaveWrite => ({
    ...(cave as unknown as CaveWrite),
    parentId: cave.parents.find((p) => p.isPrimary)?.id ?? null,
  });

  const parentCrumbs = [...cave.parents].sort((a, b) => Number(b.isPrimary) - Number(a.isPrimary));

  const onDeleteCave = async () => {
    try {
      await deleteCave.mutateAsync(cave.id);
      message.success(t('common.deleted'));
      navigate('/caves');
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <div style={{ padding: 24, maxWidth: 1100 }}>
      {parentCrumbs.length > 0 && (
        <Breadcrumb
          style={{ marginBottom: 4 }}
          items={[
            ...parentCrumbs.map((parent) => ({
              title: (
                <Typography.Link onClick={() => navigate(`/features/${parent.id}`)}>
                  {parent.name ?? t('features.unnamed')}
                </Typography.Link>
              ),
            })),
            { title: cave.name },
          ]}
        />
      )}

      <Flex justify="space-between" align="center" style={{ marginBottom: 12 }}>
        <Flex align="center" gap={12}>
          {/* The headline picture somebody chose, when there is one this reader may see. It sits
              beside the name rather than down among the attachments because that is what it is
              for: recognising the place at a glance. */}
          {summary?.headlinePicture && (
            <img
              src={summary.headlinePicture.thumbnailUrl}
              alt={summary.headlinePicture.caption ?? cave.name ?? ''}
              loading="lazy"
              style={{ width: 96, height: 72, objectFit: 'cover', borderRadius: 6 }}
            />
          )}
          <Typography.Title level={3} style={{ margin: 0 }}>
            {cave.name}
          </Typography.Title>
        </Flex>
        <Flex gap={8} wrap justify="end">
          {canShare && (
            <Button icon={<ShareAltOutlined />} onClick={() => setShareOpen(true)}>
              {t('shares.button')}
            </Button>
          )}
          {canManagePermissions && (
            <Button icon={<LockOutlined />} onClick={() => setPermissionsOpen(true)}>
              {t('permissions.button')}
            </Button>
          )}
          {canEdit && (
            <Button icon={<EditOutlined />} onClick={() => navigate(`/caves/${cave.id}/edit`)}>
              {t('caves.edit')}
            </Button>
          )}
          {canDelete && (
            <Popconfirm title={t('caves.deleteConfirm')} onConfirm={() => void onDeleteCave()}>
              <Button danger icon={<DeleteOutlined />}>
                {t('caves.delete')}
              </Button>
            </Popconfirm>
          )}
        </Flex>
      </Flex>

      {summary && (
        <Flex gap={16} wrap style={{ marginBottom: 12 }}>
          <Typography.Text type="secondary">
            {t('caves.summary.entrances', { count: summary.entranceCount })}
          </Typography.Text>
          <Typography.Text type="secondary">
            {t('caves.summary.centerlines', { count: summary.centerlineCount })}
          </Typography.Text>
          <Typography.Text type="secondary">
            {t('caves.summary.surveyModels', { count: summary.surveyModelCount })}
          </Typography.Text>
          <Typography.Text type="secondary">
            {t('caves.summary.attachments', { count: summary.attachmentCount })}
          </Typography.Text>
          <Typography.Text type="secondary">
            {t('caves.summary.tripLogs', { count: summary.tripLogCount })}
          </Typography.Text>
        </Flex>
      )}

      {cave.approximateLocation && (
        <Alert type="warning" showIcon title={t('map.approximate')} style={{ marginBottom: 12 }} />
      )}

      <Card style={{ marginBottom: 16 }}>
        {/* Three columns of label+value do not fit a phone: antd would keep them and let the
            values wrap to unreadable slivers. One column below sm, two through md. */}
        <Descriptions column={{ xs: 1, sm: 2, md: 3 }} size="small">
          {detailItem(t('caves.type'), caveTypeName)}
          {detailItem(t('caves.identificationCode'), cave.identificationCode)}
          {detailItem(t('caves.region'), cave.region)}
          {detailItem(t('caves.fields.otherToponyms'), cave.otherToponyms)}
          {detailItem(t('caves.fields.hydrographicBasin'), cave.hydrographicBasin)}
          {detailItem(t('caves.fields.valley'), cave.valley)}
          {detailItem(t('caves.fields.rockType'), rockTypeName)}
          {detailItem(t('caves.fields.surveyedLength'), cave.surveyedLength)}
          {detailItem(t('caves.depth'), cave.depth)}
          {detailItem(t('caves.fields.altitude'), cave.altitude)}
          {detailItem(t('caves.fields.discoveryDate'), cave.discoveryDate)}
          {detailItem(t('caves.fields.discoverer'), cave.discoverer)}
          <Descriptions.Item label={t('caves.visibility')}>
            <Tag>{t(`caves.visibilityValues.${cave.visibility}`)}</Tag>
          </Descriptions.Item>
        </Descriptions>
        {cave.description && (
          <Typography.Paragraph style={{ marginTop: 12, marginBottom: 0 }}>
            {cave.description}
          </Typography.Paragraph>
        )}
      </Card>

      <Card
        title={t('caves.entrances')}
        extra={
          canEdit && (
            <Button
              size="small"
              icon={<PlusOutlined />}
              onClick={() => {
                setEditingEntrance(null);
                setEditorOpen(true);
              }}
            >
              {t('entrances.add')}
            </Button>
          )
        }
      >
        <Table<Entrance>
          scroll={{ x: 'max-content' }}
          rowKey="id"
          size="small"
          dataSource={entrances}
          pagination={false}
          columns={[
            { title: t('caves.name'), dataIndex: 'name' },
            { title: t('caves.type'), dataIndex: 'entranceTypeId', render: entranceTypeName, width: 160 },
            {
              title: t('entrances.coordinates'),
              key: 'coords',
              render: (_, e) => (
                <span>
                  <EnvironmentOutlined /> {formatLonLat(e.geom.coordinates[0], e.geom.coordinates[1])}
                </span>
              ),
            },
            { title: t('caves.fields.altitude'), dataIndex: 'altitude', width: 100, align: 'right' },
            {
              title: t('entrances.main'),
              dataIndex: 'isMain',
              width: 80,
              render: (isMain: boolean) => (isMain ? <Tag color="green">✓</Tag> : null),
            },
            {
              title: t('entrances.positionQuality'),
              dataIndex: 'positionQuality',
              width: 130,
              render: (value: string) => t(`entrances.qualityValues.${value}`),
            },
            ...(canEdit
              ? [
                  {
                    key: 'actions',
                    width: 110,
                    render: (_: unknown, e: Entrance) => (
                      <Flex gap={4}>
                        <Button
                          size="small"
                          type="text"
                          icon={<EditOutlined />}
                          onClick={() => {
                            setEditingEntrance(e);
                            setEditorOpen(true);
                          }}
                        />
                        <Popconfirm
                          title={t('entrances.deleteConfirm')}
                          onConfirm={() => void deleteEntrance.mutateAsync(e.id)}
                        >
                          <Button size="small" type="text" danger icon={<DeleteOutlined />} />
                        </Popconfirm>
                      </Flex>
                    ),
                  },
                ]
              : []),
          ]}
        />
      </Card>

      {id && (
        <div style={{ marginTop: 12 }}>
          <TagChips entityType="feature" entityId={id} canEdit={canEdit} />
        </div>
      )}

      {/* A cave is a feature to the link vocabulary — the same identity its map row carries. */}
      {id && <LinksSection entityType="feature" entityId={id} canAdd entityTitle={cave.name} />}

      {id && <SurveyModelSection caveId={id} canEdit={canEdit} />}

      {id && <CenterlineSection caveId={id} canEdit={canEdit} />}

      {id && (
        <AttachmentSection
          entityType="feature"
          entityId={id}
          canEdit={canEdit}
          defaultPhotoRole="photoEntrance"
        />
      )}

      {id && (
        <HistoryPanel
          entityType="feature"
          entityId={id}
          restore={
            canEdit
              ? ([
                  {
                    // Cave events carry the kind-qualified feature audit identity.
                    entityType: 'Feature:Cave',
                    onRestore: async (event, props) => {
                      await updateCave.mutateAsync(applyRestore(caveAsWrite(), event.changes, props));
                    },
                  },
                  {
                    // Entrance events surface in the cave timeline (audit root = the cave feature);
                    // restore composes a PUT on the entrance the row belongs to.
                    entityType: 'Feature:CaveEntrance',
                    onRestore: async (event, props) => {
                      const entrance = entrances?.find((e) => e.id === event.entityId);
                      if (!entrance) {
                        // The row's entrance was deleted since; surface it as a failed restore
                        // rather than a silent no-op (HistoryPanel shows the error toast).
                        throw new Error('entrance no longer exists');
                      }
                      await updateEntrance.mutateAsync({
                        id: event.entityId,
                        body: applyRestore(entrance as unknown as EntranceWrite, event.changes, props),
                      });
                    },
                  },
                ] satisfies HistoryRestore[])
              : undefined
          }
        />
      )}

      {id && (
        <PermissionsModal
          entityType="feature"
          entityId={id}
          open={permissionsOpen}
          onClose={() => setPermissionsOpen(false)}
        />
      )}

      {id && <ShareLinksModal featureId={id} open={shareOpen} onClose={() => setShareOpen(false)} />}

      {id && (
        <EntranceEditorModal
          caveId={id}
          entrance={editingEntrance}
          defaultCenter={
            cave.geom ? [cave.geom.coordinates[0], cave.geom.coordinates[1]] : [25.3, 45.7]
          }
          open={editorOpen}
          onClose={() => setEditorOpen(false)}
        />
      )}
    </div>
  );
}
