// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { ArrowLeftOutlined, UndoOutlined } from '@ant-design/icons';
import { App, Button, Empty, Flex, Popconfirm, Table, Typography } from 'antd';
import type { TablePaginationConfig } from 'antd';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router-dom';
import { ApiError } from '../../api/client.ts';
import {
  useDeletedFeatures,
  useFeatureTypes,
  useRestoreFeature,
  type DeletedFeature,
  type FeatureEnvelope,
} from '../../api/hooks.ts';
import { surfaceFeaturesChanged } from '../../workspace/surfaceFeatureRefresh.ts';

/**
 * The deletions of caves, entrances and surface features this reader may undo, most recent first.
 *
 * One row is one deletion, not one deleted object: deleting a cave takes its entrances with it,
 * and they come back with it, so the row for the cave says how many went and none of them is a
 * row of its own. Which deletions are listed is the server's decision, made row by row — the
 * right that deletes is the right that restores — so nothing here filters, and an account that
 * may delete nothing sees an empty list rather than a refusal.
 *
 * The list says what a thing is called and where it sat, and never where it is: a deleted cave's
 * position is no more this page's to show than it was the cave's own page's.
 *
 * The one act offered is restoring. A deleted feature answers as not found everywhere else, so no
 * row is a link; once something is back the page opens it, which is what somebody who restores a
 * cave is about to do and the quickest proof that it came back whole.
 */
export default function DeletedFeaturesPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const [page, setPage] = useState(1);
  const { data, isFetching, isError } = useDeletedFeatures(page);
  const { data: featureTypes } = useFeatureTypes();
  const restore = useRestoreFeature();
  const language = i18n.resolvedLanguage;

  const onRestore = async (feature: DeletedFeature) => {
    try {
      const envelope = await restore.mutateAsync(feature.id);
      // The map draws what it last fetched for the box it is showing and holds no cached key a
      // write could mark stale, so it is told: what came back belongs on it again.
      surfaceFeaturesChanged();
      message.success(t('features.deleted.restored'));
      navigate(pageOf(envelope));
    } catch (e) {
      message.error(restoreProblem(e, t));
    }
  };

  const kindText = (feature: DeletedFeature): string => {
    if (feature.kind !== 'generic') {
      return t(`features.kinds.${feature.kind}`);
    }
    const code = feature.featureTypeCode;
    return featureTypes?.find((x) => x.code === code)?.name ?? code ?? t('features.kinds.generic');
  };

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 8 }} gap={8} wrap>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('features.deleted.title')}
        </Typography.Title>
        <Flex gap={8}>
          <Link to="/caves">
            <Button icon={<ArrowLeftOutlined />}>{t('caves.title')}</Button>
          </Link>
          <Link to="/features">
            <Button icon={<ArrowLeftOutlined />}>{t('features.title')}</Button>
          </Link>
        </Flex>
      </Flex>
      <Typography.Paragraph type="secondary" style={{ maxWidth: 760 }}>
        {t('features.deleted.intro')}
      </Typography.Paragraph>
      <Table<DeletedFeature>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="middle"
        data-testid="deleted-features"
        loading={isFetching && !data}
        dataSource={data?.items}
        onChange={(pagination: TablePaginationConfig) => setPage(pagination.current ?? 1)}
        locale={{
          emptyText: (
            <Empty
              data-testid="deleted-features-empty"
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description={isError ? t('features.deleted.loadFailed') : t('features.deleted.empty')}
            />
          ),
        }}
        pagination={{
          current: data?.page,
          pageSize: data?.pageSize,
          total: data?.totalItems,
          showSizeChanger: false,
          hideOnSinglePage: true,
        }}
        columns={[
          {
            title: t('features.deleted.columns.name'),
            key: 'name',
            render: (_, feature) => (
              <Flex vertical>
                <Typography.Text>{feature.name ?? t('features.unnamed')}</Typography.Text>
                {feature.parents.length > 0 && (
                  // Where it sat, so that two entrances of one name are told apart by their caves.
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    {feature.parents.map((parent) => parent.name ?? t('features.unnamed')).join(' › ')}
                  </Typography.Text>
                )}
              </Flex>
            ),
          },
          {
            title: t('features.deleted.columns.kind'),
            key: 'kind',
            width: 170,
            render: (_, feature) => kindText(feature),
          },
          {
            title: t('features.deleted.columns.deletedAt'),
            dataIndex: 'deletedAt',
            width: 190,
            render: (value: string) => new Date(value).toLocaleString(language),
          },
          {
            title: t('features.deleted.columns.took'),
            key: 'took',
            width: 220,
            render: (_, feature) => (
              <span data-testid="deleted-feature-took">{tookText(feature, t)}</span>
            ),
          },
          {
            title: '',
            key: 'actions',
            width: 130,
            render: (_, feature) => (
              <Popconfirm
                title={t('features.deleted.restoreConfirm')}
                description={t('features.deleted.restoreConfirmDetail')}
                okText={t('features.deleted.restore')}
                onConfirm={() => void onRestore(feature)}
              >
                <Button
                  size="small"
                  icon={<UndoOutlined />}
                  loading={restore.isPending && restore.variables === feature.id}
                  data-testid="deleted-feature-restore"
                >
                  {t('features.deleted.restore')}
                </Button>
              </Popconfirm>
            ),
          },
        ]}
      />
    </div>
  );
}

/**
 * What went with a deletion, in words: "1 entrance", "2 entrances, 3 other objects", or nothing
 * where the feature went alone.
 */
function tookText(feature: DeletedFeature, t: TFunction): string {
  const parts: string[] = [];
  if (feature.entranceCount === 1) {
    parts.push(t('features.deleted.tookEntranceOne'));
  } else if (feature.entranceCount > 1) {
    parts.push(t('features.deleted.tookEntrances', { count: feature.entranceCount }));
  }
  if (feature.otherCount === 1) {
    parts.push(t('features.deleted.tookOtherOne'));
  } else if (feature.otherCount > 1) {
    parts.push(t('features.deleted.tookOthers', { count: feature.otherCount }));
  }
  return parts.length > 0 ? parts.join(', ') : t('features.deleted.tookNothing');
}

/**
 * The page a restored feature opens on: a cave's own page, an entrance's cave — an entrance has
 * no page of its own and is read on its cave's — and the feature page for everything else.
 */
function pageOf(envelope: FeatureEnvelope): string {
  if (envelope.kind === 'cave') {
    return `/caves/${envelope.feature.id}`;
  }
  if (envelope.kind === 'caveEntrance' && envelope.entrance) {
    return `/caves/${envelope.entrance.caveFeatureId}`;
  }
  return `/features/${envelope.feature.id}`;
}

/**
 * The sentence for a restore that was refused. Each refusal the route names means something a
 * reader can act on — what contains it is deleted and has to come back first, somebody else
 * already restored it, it is not theirs — and anything else is the ordinary failed save.
 */
function restoreProblem(error: unknown, t: TFunction): string {
  if (!(error instanceof ApiError)) {
    return t('common.saveFailed');
  }
  if (error.code === 'feature.restore_container_deleted') {
    // Named only when the server named it: a container this reader could not see is in the way
    // all the same, and is not named to them for being so.
    const first = error.problem?.restoreFirst;
    const name =
      typeof first === 'object' && first !== null && 'name' in first && typeof first.name === 'string'
        ? first.name
        : undefined;
    return name
      ? t('features.deleted.containerDeletedNamed', { name })
      : t('features.deleted.containerDeleted');
  }
  if (error.code === 'feature.not_deleted' || error.status === 404) {
    return t('features.deleted.gone');
  }
  return error.status === 403 ? t('features.deleted.forbidden') : t('common.saveFailed');
}
