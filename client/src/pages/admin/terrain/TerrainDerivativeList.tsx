// SPDX-License-Identifier: AGPL-3.0-or-later
import { App, Button, Card, Empty, Flex, Popconfirm, Space, Table, Tag, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import i18n from '../../../i18n';
import {
  useDeleteTerrainDerivative,
  useTerrainBuilds,
  useTerrainDerivatives,
  type TerrainDerivativeLayerInfo,
} from '../../../api/hooks.ts';
import { formatBbox } from './terrainArea.ts';
import { formatBuildSize, geometryBbox } from './terrainBuild.ts';
import { DERIVATIVE_BUILD_CHOICES, describeDerivativeSettings } from './terrainDerivative.ts';
import { terrainProblemMessage } from './terrainProblems.ts';

/** Only a failure is an alarm, only a finished picture is a result; the rest is in progress. */
function statusColour(status: TerrainDerivativeLayerInfo['status']): string {
  return status === 'failed' ? 'error' : status === 'ready' ? 'success' : 'processing';
}

interface Props {
  canDelete: boolean;
}

/**
 * Every picture of the ground this installation has computed, and what each was computed from.
 *
 * The map's layer list shows the same pictures to anybody who may read terrain, but it shows them
 * as things to switch on. This is the register: what was asked for, from which build, with which
 * settings, what it cost on disk and whether the ground beneath it has since been replaced. A
 * picture that is out of date is still listed and still drawn — withdrawing it would leave a
 * reader with nothing over ground that has probably not changed — but it is never shown as
 * current, and the badge says so in the same words the layer list uses.
 *
 * The query re-reads itself while any picture is still being computed and slowly afterwards; the
 * rule is the hook's own, so a register left open costs what the hook decided and nothing more.
 */
export default function TerrainDerivativeList({ canDelete }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data, isLoading } = useTerrainDerivatives();
  // The same page of builds the form above reads, so naming the build a picture came from costs
  // no request of its own.
  const { data: builds } = useTerrainBuilds({ page: 1, pageSize: DERIVATIVE_BUILD_CHOICES });
  const remove = useDeleteTerrainDerivative();

  const pictures = data ?? [];

  const buildArea = (buildId: string): string => {
    const build = (builds?.items ?? []).find((each) => each.id === buildId);
    const bbox = build === undefined ? null : geometryBbox(build.extent);
    return bbox === null ? t('terrain.derivativeList.buildGone') : formatBbox(bbox);
  };

  const erase = async (id: string) => {
    try {
      await remove.mutateAsync(id);
      message.success(t('common.deleted'));
    } catch (error) {
      message.error(terrainProblemMessage(error, t));
    }
  };

  return (
    <Card size="small" title={t('terrain.derivativeList.title')} styles={{ body: { padding: 0 } }}>
      <Table
        rowKey="id"
        size="small"
        data-testid="terrain-derivatives"
        loading={isLoading}
        dataSource={pictures}
        pagination={false}
        scroll={{ x: true }}
        locale={{
          emptyText: isLoading ? (
            <span />
          ) : (
            <Empty
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              data-testid="terrain-no-derivatives"
              description={
                <Flex vertical gap={4}>
                  <span>{t('terrain.derivativeList.empty')}</span>
                  <Typography.Text type="secondary">
                    {t('terrain.derivativeList.emptyHint')}
                  </Typography.Text>
                </Flex>
              }
            />
          ),
        }}
        columns={[
          {
            title: t('terrain.derivativeList.picture'),
            dataIndex: 'name',
            render: (name: string, picture: TerrainDerivativeLayerInfo) => (
              <Flex vertical>
                <span>{name}</span>
                <Typography.Text type="secondary" data-testid={`terrain-derivative-kind-${picture.id}`}>
                  {t(`terrain.derivatives.kinds.${picture.derivative}`)}
                </Typography.Text>
              </Flex>
            ),
          },
          {
            title: t('terrain.derivativeList.parameters'),
            dataIndex: 'settings',
            render: (settings: string, picture: TerrainDerivativeLayerInfo) => (
              <Typography.Text type="secondary" data-testid={`terrain-derivative-params-${picture.id}`}>
                {describeDerivativeSettings(picture.derivative, settings, t)}
              </Typography.Text>
            ),
          },
          {
            title: t('terrain.derivativeList.build'),
            dataIndex: 'terrainBuildId',
            render: (buildId: string) => buildArea(buildId),
          },
          {
            title: t('terrain.derivativeList.state'),
            dataIndex: 'status',
            render: (status: TerrainDerivativeLayerInfo['status'], picture: TerrainDerivativeLayerInfo) => (
              <Space size={4} wrap>
                <Tag color={statusColour(status)}>{t(`terrain.derivatives.statuses.${status}`)}</Tag>
                {picture.stale && (
                  <Tooltip title={t('terrain.derivatives.staleHint')}>
                    <Tag color="warning" data-testid={`terrain-derivative-stale-${picture.id}`}>
                      {t('terrain.derivatives.stale')}
                    </Tag>
                  </Tooltip>
                )}
                {/* A failure explains itself in the server's words: the only ones there are. */}
                {status === 'failed' && picture.message && (
                  <Typography.Text type="danger" data-testid={`terrain-derivative-failure-${picture.id}`}>
                    {picture.message}
                  </Typography.Text>
                )}
              </Space>
            ),
          },
          {
            title: (
              <Tooltip title={t('terrain.derivativeList.sizeHint')}>
                <span>{t('terrain.derivativeList.size')}</span>
              </Tooltip>
            ),
            dataIndex: 'sizeBytes',
            align: 'right',
            // Nothing is on disk until the picture is computed, and "0 B" beside a queued row
            // reads as a picture that came out empty.
            render: (bytes: number) => (bytes > 0 ? formatBuildSize(bytes) : ''),
          },
          {
            title: t('terrain.derivativeList.created'),
            dataIndex: 'createdAt',
            render: (createdAt: string) => new Date(createdAt).toLocaleString(i18n.resolvedLanguage),
          },
          ...(canDelete
            ? [
                {
                  title: t('terrain.derivativeList.actions'),
                  key: 'actions',
                  render: (_: unknown, picture: TerrainDerivativeLayerInfo) => (
                    // Offered on every row, a picture still being computed included: the server
                    // answers that with a code of its own, and the state can change in another
                    // browser between this list being read and the button being pressed.
                    <Popconfirm
                      title={t('terrain.derivativeList.deleteConfirm', {
                        size:
                          picture.sizeBytes > 0
                            ? formatBuildSize(picture.sizeBytes)
                            : t('terrain.list.sizeUnknown'),
                      })}
                      okButtonProps={{ danger: true }}
                      onConfirm={() => void erase(picture.id)}
                    >
                      <Button size="small" danger data-testid={`terrain-derivative-delete-${picture.id}`}>
                        {t('terrain.derivativeList.delete')}
                      </Button>
                    </Popconfirm>
                  ),
                },
              ]
            : []),
        ]}
      />
    </Card>
  );
}
