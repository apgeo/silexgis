// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  CopyOutlined,
  DeleteOutlined,
  EditOutlined,
  EnvironmentOutlined,
  LineChartOutlined,
  PlayCircleOutlined,
  PlusOutlined,
} from '@ant-design/icons';
import { Alert, App, Card, Flex, Popconfirm, Space, Table, Tag, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router-dom';
import { useCan, useCreateDoor } from '../../api/hooks/access.ts';
import { useCavingGroups } from '../../api/hooks/people.ts';
import {
  useCavePotentialModels,
  useCavePotentialResults,
  useCloneCavePotentialModel,
  useDeleteCavePotentialModel,
  useDeleteCavePotentialResult,
  useEvidenceLayers,
  type CavePotentialModelInfo,
  type CavePotentialResultInfo,
} from '../../api/hooks/cavePotential.ts';
import Button from '../../components/Button.tsx';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';
import CavePotentialModelEditor from './CavePotentialModelEditor.tsx';
import CavePotentialRunForm from './CavePotentialRunForm.tsx';
import CavePotentialTrainingView from './CavePotentialTrainingView.tsx';
import { formatAuc } from './cavePotentialCurves.ts';
import { mapAddressOf } from './cavePotentialModel.ts';
import { isDataDriven } from './cavePotentialNames.ts';
import { cavePotentialFailureText } from '../../components/map/cavePotentialFailure.ts';

/**
 * Cave-potential models and the maps computed from them.
 *
 * A model is a recipe — which evidence is weighed and how — and holds no place, so the list of
 * models is every signed-in reader's to read, like their saved filters. Running one and reading the
 * maps is another matter: both need the right to read the terrain, and this page asks for neither
 * list nor button without it, so a reader who lacks the right is told so once instead of being
 * refused by a request every few seconds.
 *
 * Which maps are listed is decided by the server for each reader: one weighed from recorded
 * positions is listed only for somebody who may place every one of them. Nothing here hides a row
 * the server sent, and nothing here could show one it did not.
 */
export default function CavePotentialPage() {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const navigate = useNavigate();
  const mayReadTerrain = useCan('terrain', 'read');
  const { canCreate } = useCreateDoor('mapViews');
  const { data: evidenceLayers } = useEvidenceLayers();
  // Every club, to name the one a listed model is shared with.
  const { data: cavingGroups } = useCavingGroups();
  const { data: models, isLoading: modelsLoading } = useCavePotentialModels();
  const { data: results, isLoading: resultsLoading } = useCavePotentialResults(mayReadTerrain);
  const clone = useCloneCavePotentialModel();
  const removeModel = useDeleteCavePotentialModel();
  const removeResult = useDeleteCavePotentialResult();
  const setCavePotentialVisible = useWorkspaceStore((s) => s.setCavePotentialVisible);

  // `undefined` is closed, `null` a new model, a model the one being changed.
  const [editing, setEditing] = useState<CavePotentialModelInfo | null | undefined>(undefined);
  const [running, setRunning] = useState<CavePotentialModelInfo | null>(null);
  // The results whose weight tables and curves are open under their row.
  const [opened, setOpened] = useState<string[]>([]);

  /**
   * Whether a model can be offered for a run. One weighed by judgment has its weights worked out
   * by the server exactly when it is valid. One that learns from known entrances has no weights
   * before it is run, so it is offered whenever its definition can be read, and the server says
   * what is wrong with it when the run is asked for.
   */
  const runnable = (model: CavePotentialModelInfo) =>
    model.weighting !== null || (isDataDriven(model.method) && model.definition !== null);

  const layerName = (code: string) => t(`cavePotential.evidence.${code}`, { defaultValue: code });
  const date = (iso: string) => new Date(iso).toLocaleDateString(i18n.resolvedLanguage);

  const copy = async (model: CavePotentialModelInfo) => {
    try {
      await clone.mutateAsync({ id: model.id, name: t('cavePotential.page.copyName', { name: model.name }).slice(0, 200) });
      message.success(t('cavePotential.page.copied'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const deleteModel = async (model: CavePotentialModelInfo) => {
    try {
      await removeModel.mutateAsync(model.id);
      message.success(t('common.deleted'));
    } catch {
      message.error(t('common.deleteFailed'));
    }
  };

  const deleteResult = async (result: CavePotentialResultInfo) => {
    try {
      await removeResult.mutateAsync(result.id);
      setCavePotentialVisible(result.id, false);
      message.success(t('common.deleted'));
    } catch {
      message.error(t('common.deleteFailed'));
    }
  };

  const showOnMap = (result: CavePotentialResultInfo) => {
    setCavePotentialVisible(result.id, true);
    navigate(mapAddressOf(result));
  };

  const positionsTag = (
    <Tooltip title={t('cavePotential.page.positionsTagHint')}>
      <Tag color="gold" data-testid="cave-potential-positions">{t('cavePotential.page.positionsTag')}</Tag>
    </Tooltip>
  );

  return (
    <div style={{ padding: 24 }} data-testid="cave-potential-page">
      <Typography.Title level={3} style={{ marginTop: 0 }}>
        {t('cavePotential.page.title')}
      </Typography.Title>
      <Typography.Paragraph type="secondary">{t('cavePotential.page.intro')}</Typography.Paragraph>

      <Card
        size="small"
        style={{ marginBottom: 16 }}
        title={t('cavePotential.page.models')}
        extra={
          canCreate && evidenceLayers ? (
            <Button
              type="primary"
              icon={<PlusOutlined />}
              data-testid="cave-potential-new-model"
              onClick={() => setEditing(null)}
            >
              {t('cavePotential.page.newModel')}
            </Button>
          ) : null
        }
      >
        <Table<CavePotentialModelInfo>
          size="small"
          rowKey="id"
          loading={modelsLoading}
          dataSource={models ?? []}
          pagination={false}
          scroll={{ x: 'max-content' }}
          data-testid="cave-potential-models"
          locale={{ emptyText: t('cavePotential.page.noModels') }}
          columns={[
            {
              title: t('cavePotential.page.columns.name'),
              key: 'name',
              render: (_, model) => (
                <Flex vertical>
                  <Typography.Text strong>{model.name}</Typography.Text>
                  {model.description && (
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>{model.description}</Typography.Text>
                  )}
                </Flex>
              ),
            },
            {
              title: t('cavePotential.page.columns.method'),
              key: 'method',
              render: (_, model) => t(`cavePotential.methods.${model.method}`),
            },
            {
              title: t('cavePotential.page.columns.layers'),
              key: 'layers',
              render: (_, model) =>
                model.weighting ? (
                  <Flex gap={4} wrap>
                    {model.weighting.weights.map((weight) => (
                      <Tag key={weight.layer}>
                        {layerName(weight.layer)}
                        {' '}
                        {(weight.weight * 100).toFixed(0)}
                        {' %'}
                      </Tag>
                    ))}
                    {model.usesPositions && positionsTag}
                  </Flex>
                ) : isDataDriven(model.method) && model.definition ? (
                  // No weights to show: they are counted on each run and are on its result.
                  <Flex gap={4} wrap>
                    {model.definition.layers.map((setting) => (
                      <Tag key={setting.layer}>{layerName(setting.layer)}</Tag>
                    ))}
                    <Tooltip title={t('cavePotential.page.learnsTagHint')}>
                      <Tag color="gold" data-testid="cave-potential-learns">{t('cavePotential.page.learnsTag')}</Tag>
                    </Tooltip>
                  </Flex>
                ) : (
                  <Typography.Text type="warning">{t('cavePotential.page.unreadableModel')}</Typography.Text>
                ),
            },
            {
              title: t('cavePotential.page.columns.author'),
              key: 'author',
              render: (_, model) =>
                model.mine ? t('cavePotential.page.you') : (model.authorName ?? t('cavePotential.page.someone')),
            },
            {
              title: t('cavePotential.page.columns.audience'),
              key: 'audience',
              render: (_, model) =>
                model.visibility === 'cavingGroup'
                  ? (() => {
                      const group = cavingGroups?.find((candidate) => candidate.id === model.cavingGroupId);
                      return group
                        ? t('cavePotential.audiences.cavingGroup', { name: group.name })
                        : t('cavePotential.audiences.someGroup');
                    })()
                  : model.visibility === 'authenticated'
                    ? t('cavePotential.audiences.authenticated')
                    : t('cavePotential.audiences.private'),
            },
            {
              title: t('cavePotential.page.columns.actions'),
              key: 'actions',
              render: (_, model) => (
                <Space size={4}>
                  {mayReadTerrain && runnable(model) && (
                    <Button
                      size="small"
                      icon={<PlayCircleOutlined />}
                      data-testid="cave-potential-run"
                      onClick={() => setRunning(model)}
                    >
                      {t('cavePotential.page.run')}
                    </Button>
                  )}
                  {model.mayEdit && evidenceLayers && (
                    <Button
                      size="small"
                      icon={<EditOutlined />}
                      aria-label={t('cavePotential.page.editOf', { name: model.name })}
                      data-testid="cave-potential-edit"
                      onClick={() => setEditing(model)}
                    />
                  )}
                  {canCreate && (
                    <Button
                      size="small"
                      icon={<CopyOutlined />}
                      aria-label={t('cavePotential.page.copyOf', { name: model.name })}
                      data-testid="cave-potential-copy"
                      onClick={() => void copy(model)}
                    />
                  )}
                  {model.mayDelete && (
                    <Popconfirm
                      title={t('cavePotential.page.deleteModelConfirm')}
                      okText={t('common.ok')}
                      cancelText={t('common.cancel')}
                      onConfirm={() => void deleteModel(model)}
                    >
                      <Button
                        size="small"
                        danger
                        icon={<DeleteOutlined />}
                        aria-label={t('cavePotential.page.deleteOf', { name: model.name })}
                        data-testid="cave-potential-delete"
                      />
                    </Popconfirm>
                  )}
                </Space>
              ),
            },
          ]}
        />
      </Card>

      <Card size="small" title={t('cavePotential.page.results')}>
        {!mayReadTerrain ? (
          <Alert type="info" showIcon data-testid="cave-potential-needs-terrain" title={t('cavePotential.page.needsTerrain')} />
        ) : (
          <Table<CavePotentialResultInfo>
            size="small"
            rowKey="id"
            loading={resultsLoading}
            dataSource={results ?? []}
            pagination={false}
            scroll={{ x: 'max-content' }}
            data-testid="cave-potential-results"
            locale={{ emptyText: t('cavePotential.page.noResults') }}
            expandable={{
              // Opened by the row's own button, which says what it opens.
              showExpandColumn: false,
              expandedRowKeys: opened,
              rowExpandable: (result) => result.status === 'ready' && Boolean(result.summary?.training),
              expandedRowRender: (result) =>
                result.summary?.training ? (
                  <CavePotentialTrainingView
                    result={result}
                    training={result.summary.training}
                    evidenceLayers={evidenceLayers}
                  />
                ) : null,
            }}
            columns={[
              {
                title: t('cavePotential.page.columns.model'),
                key: 'model',
                render: (_, result) => (
                  <Flex gap={4} wrap align="center">
                    <Typography.Text strong>{result.modelName ?? t('cavePotential.page.modelHidden')}</Typography.Text>
                    {result.usesPositions && positionsTag}
                    {result.terrainBuildActive === false && (
                      <Tooltip title={t('cavePotential.page.groundReplacedHint')}>
                        <Tag data-testid="cave-potential-ground-replaced">{t('cavePotential.page.groundReplaced')}</Tag>
                      </Tooltip>
                    )}
                  </Flex>
                ),
              },
              {
                title: t('cavePotential.page.columns.area'),
                key: 'area',
                render: (_, result) => (
                  <Link to={`/features/${result.areaFeatureId}`}>{result.areaName ?? t('cavePotential.page.unnamedArea')}</Link>
                ),
              },
              {
                title: t('cavePotential.page.columns.cell'),
                key: 'cell',
                render: (_, result) => t('cavePotential.layers.cell', { meters: result.cellMeters }),
              },
              {
                title: t('cavePotential.page.columns.status'),
                key: 'status',
                render: (_, result) => (
                  <Flex vertical>
                    <span>
                      <Tag
                        color={result.status === 'failed' ? 'error' : result.status === 'ready' ? 'success' : 'processing'}
                        data-testid="cave-potential-result-status"
                      >
                        {t(`cavePotential.statuses.${result.status}`)}
                      </Tag>
                    </span>
                    {result.status === 'failed' && (
                      <Typography.Text type="secondary" style={{ fontSize: 12 }} data-testid="cave-potential-result-failure">
                        {cavePotentialFailureText(t, result)}
                      </Typography.Text>
                    )}
                    {result.status === 'ready' && result.summary && (
                      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                        {t('cavePotential.page.summary', {
                          scored: result.summary.scoredCells,
                          cells: result.summary.areaCells,
                          mean: result.summary.mean === null ? '–' : result.summary.mean.toFixed(2),
                        })}
                      </Typography.Text>
                    )}
                    {result.status === 'ready' && result.summary?.training && (
                      <Typography.Text type="secondary" style={{ fontSize: 12 }} data-testid="cave-potential-result-training">
                        {t('cavePotential.page.trainingSummary', {
                          training: result.summary.training.trainingEntrances,
                          heldOut: result.summary.training.heldOutEntrances,
                          success: formatAuc(result.summary.training.successRate.areaUnderCurve),
                          prediction: formatAuc(result.summary.training.predictionRate.areaUnderCurve),
                        })}
                      </Typography.Text>
                    )}
                  </Flex>
                ),
              },
              {
                title: t('cavePotential.page.columns.computed'),
                key: 'computed',
                render: (_, result) => date(result.computedAt ?? result.createdAt),
              },
              {
                title: t('cavePotential.page.columns.requestedBy'),
                key: 'requestedBy',
                render: (_, result) =>
                  result.mine ? t('cavePotential.page.you') : (result.requestedByName ?? t('cavePotential.page.someone')),
              },
              {
                title: t('cavePotential.page.columns.actions'),
                key: 'actions',
                render: (_, result) => (
                  <Space size={4}>
                    {result.status === 'ready' && (
                      <Button
                        size="small"
                        icon={<EnvironmentOutlined />}
                        data-testid="cave-potential-show"
                        onClick={() => showOnMap(result)}
                      >
                        {t('cavePotential.page.showOnMap')}
                      </Button>
                    )}
                    {result.status === 'ready' && result.summary?.training && (
                      <Button
                        size="small"
                        icon={<LineChartOutlined />}
                        aria-expanded={opened.includes(result.id)}
                        data-testid="cave-potential-training-toggle"
                        onClick={() =>
                          setOpened((ids) =>
                            ids.includes(result.id) ? ids.filter((id) => id !== result.id) : [...ids, result.id],
                          )
                        }
                      >
                        {t('cavePotential.page.weightsAndCurves')}
                      </Button>
                    )}
                    {result.mayDelete && (
                      <Popconfirm
                        title={t('cavePotential.page.deleteResultConfirm')}
                        okText={t('common.ok')}
                        cancelText={t('common.cancel')}
                        onConfirm={() => void deleteResult(result)}
                      >
                        <Button
                          size="small"
                          danger
                          icon={<DeleteOutlined />}
                          aria-label={t('cavePotential.page.deleteResultOf', { name: result.modelName ?? t('cavePotential.page.modelHidden') })}
                          data-testid="cave-potential-result-delete"
                        />
                      </Popconfirm>
                    )}
                  </Space>
                ),
              },
            ]}
          />
        )}
      </Card>

      {editing !== undefined && evidenceLayers && (
        <CavePotentialModelEditor
          key={editing?.id ?? 'new'}
          model={editing}
          evidenceLayers={evidenceLayers}
          onClose={() => setEditing(undefined)}
        />
      )}
      {running && <CavePotentialRunForm model={running} onClose={() => setRunning(null)} />}
    </div>
  );
}
