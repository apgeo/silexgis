// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  App,
  Button,
  Card,
  Empty,
  Flex,
  Popconfirm,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import i18n from '../../../i18n';
import {
  terrainBuildUnsettled,
  useChooseTerrainBuild,
  useDeleteTerrainBuild,
  useStopDrawingTerrainBuild,
  useTerrainBuilds,
} from '../../../api/hooks.ts';
import type { TerrainBuild, TerrainBuildStatus } from '../../../api/hooks.ts';
import { formatBbox } from './terrainArea.ts';
import {
  TERRAIN_PAGE_SIZE,
  formatBuildDuration,
  formatBuildSize,
  geometryBbox,
  mayActivate,
} from './terrainBuild.ts';
import { terrainProblemMessage } from './terrainProblems.ts';
import TerrainBuildProgress from './TerrainBuildProgress.tsx';

/** Which colour a run's state is worth: only a failure is an alarm, only success is a result. */
function statusColour(status: TerrainBuildStatus): string {
  return status === 'failed' ? 'error' : status === 'succeeded' ? 'success' : 'processing';
}

interface Props {
  canExecute: boolean;
  canDelete: boolean;
}

/**
 * Every build this installation has made, and what can still be done with each.
 *
 * This list is the feature's whole memory: nothing else records that a rectangle was ever built,
 * what it cost, or which of several attempts is the one the scene is drawing. It is therefore the
 * only place the size on disk appears, and that number covers everything a build left behind —
 * the published tiles and the converted rasters kept beside them — which is why the column says
 * so rather than saying "tiles".
 *
 * The scene is never the confirmation that an action worked. Choosing a build that is already the
 * one being drawn changes no address, so the engine correctly redraws nothing; a page that let the
 * map answer for it would report success as failure and vice versa. Every action here says what it
 * did in words of its own.
 */
export default function TerrainBuildList({ canExecute, canDelete }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [page, setPage] = useState(1);
  const [chosen, setChosen] = useState<string | null>(null);
  const { data, isLoading } = useTerrainBuilds({ page, pageSize: TERRAIN_PAGE_SIZE });
  const choose = useChooseTerrainBuild();
  const stopDrawing = useStopDrawingTerrainBuild();
  const remove = useDeleteTerrainBuild();

  const builds = data?.items ?? [];
  // The newest build is what somebody arriving mid-run came to look at, so the pipeline view is
  // open on it until another row is picked; a row that has gone from the page falls back the same
  // way rather than leaving the panel showing a build that is no longer listed.
  const selected = builds.some((build) => build.id === chosen) ? chosen : (builds[0]?.id ?? null);

  const run = async (action: Promise<unknown>, success: string) => {
    try {
      await action;
      message.success(t(success));
    } catch (error) {
      message.error(terrainProblemMessage(error, t));
    }
  };

  return (
    <Flex vertical gap={16}>
      <Card size="small" title={t('terrain.list.title')} styles={{ body: { padding: 0 } }}>
        <Table
          rowKey="id"
          size="small"
          data-testid="terrain-builds"
          loading={isLoading}
          dataSource={builds}
          scroll={{ x: true }}
          locale={{
            // An installation that has never built anything is the state every installation
            // starts in, so it is answered with what to do next rather than with "no data".
            // Nothing is said while the first answer is still coming back: an emptiness that has
            // not been established yet is not a fact about the installation.
            emptyText: isLoading ? (
              <span />
            ) : (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                data-testid="terrain-no-builds"
                description={
                  <Flex vertical gap={4}>
                    <span>{t('terrain.noBuilds')}</span>
                    <Typography.Text type="secondary">{t('terrain.noBuildsHint')}</Typography.Text>
                  </Flex>
                }
              />
            ),
          }}
          onRow={(build) => ({ onClick: () => setChosen(build.id) })}
          rowClassName={(build) => (build.id === selected ? 'ant-table-row-selected' : '')}
          pagination={{
            current: page,
            pageSize: TERRAIN_PAGE_SIZE,
            total: data?.totalItems ?? 0,
            showSizeChanger: false,
            onChange: setPage,
          }}
          columns={[
            {
              title: t('terrain.list.state'),
              dataIndex: 'status',
              render: (status: TerrainBuildStatus, build: TerrainBuild) => (
                <Space size={4} wrap>
                  <Tag color={statusColour(status)}>{t(`terrain.statuses.${status}`)}</Tag>
                  {build.isActive && (
                    <Tag color="blue" data-testid={`terrain-active-${build.id}`}>
                      {t('terrain.list.current')}
                    </Tag>
                  )}
                  {/*
                    Which step a run is on belongs on the row and not only in the panel below:
                    builds are listed newest first and only one runs at a time, so the newest row
                    is often a queued build while an older one is the one actually working. Said
                    only while a build is unsettled, and only once it has left the queue — a
                    percentage against a build that has not started is a number about nothing.
                  */}
                  {terrainBuildUnsettled(status) && build.phase !== 'pending' && (
                    <Typography.Text type="secondary" data-testid={`terrain-row-phase-${build.id}`}>
                      {t(`terrain.phases.${build.phase}`)} {build.progress}%
                    </Typography.Text>
                  )}
                </Space>
              ),
            },
            {
              title: t('terrain.list.area'),
              dataIndex: 'extent',
              render: (_: unknown, build: TerrainBuild) => {
                const bbox = geometryBbox(build.extent);
                return bbox === null ? '' : formatBbox(bbox);
              },
            },
            {
              title: t('terrain.list.depth'),
              dataIndex: 'requestedMaxDepth',
              align: 'right',
            },
            {
              // Submitted, not started: this is the moment the build was asked for, and terrain
              // runs one build at a time, so a build queued behind an hours-long one started long
              // after this. How long it has been going is the figure beside it.
              title: t('terrain.list.when'),
              dataIndex: 'createdAt',
              render: (createdAt: string, build: TerrainBuild) => {
                const took = formatBuildDuration(build);
                return (
                  <Space size={4} wrap>
                    <span>{new Date(createdAt).toLocaleString(i18n.resolvedLanguage)}</span>
                    {took !== null && <Typography.Text type="secondary">{took}</Typography.Text>}
                  </Space>
                );
              },
            },
            {
              title: (
                <Tooltip title={t('terrain.list.sizeHint')}>
                  <span>{t('terrain.list.size')}</span>
                </Tooltip>
              ),
              dataIndex: 'sizeBytes',
              align: 'right',
              render: (bytes: number | null) => formatBuildSize(bytes),
            },
            {
              title: t('terrain.list.actions'),
              key: 'actions',
              render: (_: unknown, build: TerrainBuild) => (
                <Space size={4} wrap onClick={(event) => event.stopPropagation()}>
                  {canExecute && mayActivate(build) && (
                    <Button
                      size="small"
                      data-testid={`terrain-activate-${build.id}`}
                      loading={choose.isPending}
                      onClick={() =>
                        void run(choose.mutateAsync(build.id), 'terrain.list.activated')
                      }
                    >
                      {t('terrain.list.activate')}
                    </Button>
                  )}
                  {/*
                    Under execute rather than delete: stopping the scene drawing a build is the
                    same decision as choosing it, made in the other direction, and that is the
                    right the server asks for on it. Nothing is destroyed by it.
                  */}
                  {canExecute && build.isActive && (
                    <Button
                      size="small"
                      data-testid={`terrain-stop-${build.id}`}
                      loading={stopDrawing.isPending}
                      onClick={() =>
                        void run(stopDrawing.mutateAsync(build.id), 'terrain.list.stopped')
                      }
                    >
                      {t('terrain.list.stop')}
                    </Button>
                  )}
                  {canDelete && (
                    // Offered on every row, including the one being drawn and one still running:
                    // both are refused by the server with a code of its own, and the state can
                    // change in another browser between this list being read and the button being
                    // pressed. The refusal is the answer, and it is worded.
                    <Popconfirm
                      title={t('terrain.list.deleteConfirm', {
                        area: geometryBbox(build.extent)?.map((n) => n.toFixed(2)).join(', ') ?? '',
                        size: formatBuildSize(build.sizeBytes) || t('terrain.list.sizeUnknown'),
                      })}
                      okButtonProps={{ danger: true }}
                      onConfirm={() => void run(remove.mutateAsync(build.id), 'common.deleted')}
                    >
                      <Button size="small" danger data-testid={`terrain-delete-${build.id}`}>
                        {t('terrain.list.delete')}
                      </Button>
                    </Popconfirm>
                  )}
                </Space>
              ),
            },
          ]}
        />
      </Card>

      {selected !== null && <TerrainBuildProgress buildId={selected} />}
    </Flex>
  );
}
