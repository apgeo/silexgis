// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { UndoOutlined } from '@ant-design/icons';
import { App, Button, Drawer, Flex, Popconfirm, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import {
  useImportBatch,
  useImportBatches,
  useRevertImportBatch,
  type ImportBatch,
} from '../../api/hooks.ts';

/**
 * The confirmations this account has made, newest first, each with the one action a
 * confirmation still has: undoing it.
 *
 * Reverting soft-deletes everything the confirmation created, as one unit — for the case
 * where the mapping was wrong and nobody noticed until the map looked odd.
 */
export default function ImportBatchesTab() {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [open, setOpen] = useState<string | null>(null);

  const batches = useImportBatches({ page, pageSize });
  const detail = useImportBatch(open ?? undefined);
  const revert = useRevertImportBatch();

  /**
   * What to call a batch in the file column. A batch that never had a file must not claim its
   * file was deleted — that lie was written for photographs and is now equally available to a
   * phone's upload, which has no file either.
   *
   * The switch is exhaustive on purpose. Nothing about adding a source on the server makes a
   * label appear here, and the missing arm is silent: it falls through to "the file has been
   * deleted" and reaches a caver as a statement about a file that never existed. With the
   * check below, a new source stops the build instead.
   */
  const batchLabel = (source: ImportBatch['source'], fileName: string | null): string => {
    switch (source) {
      case 'photos':
        return t('vectorImport.batchFromPhotos');
      case 'deviceSync':
        return t('vectorImport.batchFromDevice');
      case 'externalCatalogue':
        return t('vectorImport.batchFromCatalogue');
      case 'vectorFile':
        return fileName ?? t('vectorImport.fileGone');
      default: {
        const unhandled: never = source;
        return unhandled;
      }
    }
  };

  const onRevert = async (id: string) => {
    try {
      await revert.mutateAsync(id);
      message.success(t('vectorImport.reverted'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <>
      <Table<ImportBatch>
        rowKey="id"
        size="middle"
        scroll={{ x: 'max-content' }}
        loading={batches.isLoading}
        dataSource={batches.data?.items}
        data-testid="import-batches"
        onChange={(pagination) => {
          setPage(pagination.current ?? 1);
          setPageSize(pagination.pageSize ?? 20);
        }}
        pagination={{
          current: batches.data?.page,
          pageSize: batches.data?.pageSize,
          total: batches.data?.totalItems,
          showSizeChanger: true,
        }}
        columns={[
          {
            title: t('vectorImport.batchFile'),
            dataIndex: 'geofileName',
            render: (name: string | null, row) => (
              <Flex vertical>
                <Typography.Link onClick={() => setOpen(row.id)}>
                  {batchLabel(row.source, name)}
                </Typography.Link>
                {row.termRuleSetName && (
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    {row.termRuleSetName}
                  </Typography.Text>
                )}
              </Flex>
            ),
          },
          {
            title: t('vectorImport.batchMode'),
            dataIndex: 'mode',
            width: 150,
            render: (mode: string) => (
              <Tag color={mode === 'autoCreated' ? 'orange' : 'default'}>
                {t(`vectorImport.modes.${mode}`)}
              </Tag>
            ),
          },
          { title: t('vectorImport.created'), dataIndex: 'createdCount', width: 90, align: 'right' },
          { title: t('vectorImport.attached'), dataIndex: 'attachedCount', width: 90, align: 'right' },
          { title: t('vectorImport.skipped'), dataIndex: 'skippedCount', width: 90, align: 'right' },
          {
            title: t('vectorImport.confirmedAt'),
            dataIndex: 'confirmedAt',
            width: 180,
            render: (value: string) => new Date(value).toLocaleString(i18n.resolvedLanguage),
          },
          {
            title: '',
            key: 'actions',
            width: 140,
            render: (_: unknown, row) =>
              row.revertedAt ? (
                <Tag>{t('vectorImport.revertedTag')}</Tag>
              ) : (
                row.canRevert && (
                  <Popconfirm
                    title={t('vectorImport.revertConfirm', { count: row.createdCount })}
                    onConfirm={() => void onRevert(row.id)}
                    okButtonProps={{ danger: true }}
                  >
                    <Button size="small" danger icon={<UndoOutlined />}>
                      {t('vectorImport.revert')}
                    </Button>
                  </Popconfirm>
                )
              ),
          },
        ]}
      />

      <Drawer
        open={open !== null}
        size="min(760px, 96vw)"
        title={t('vectorImport.batchDetail')}
        onClose={() => setOpen(null)}
        destroyOnHidden
      >
        <Table
          rowKey="id"
          size="small"
          loading={detail.isLoading}
          dataSource={detail.data?.items}
          pagination={{ pageSize: 50 }}
          columns={[
            {
              title: t('vectorImport.columns.name'),
              dataIndex: 'featureName',
              render: (name: string | null, row: { featureId?: string | null; featureDeleted?: boolean }) =>
                row.featureId ? (
                  <Flex gap={8} align="center">
                    <Link to={`/features/${row.featureId}`}>{name ?? t('vectorImport.unnamed')}</Link>
                    {row.featureDeleted && <Tag>{t('vectorImport.deletedTag')}</Tag>}
                  </Flex>
                ) : (
                  <Typography.Text type="secondary">{t('vectorImport.notCreated')}</Typography.Text>
                ),
            },
            { title: t('vectorImport.columns.rule'), dataIndex: 'ruleName', width: 200 },
            {
              title: t('vectorImport.columns.decision'),
              dataIndex: 'action',
              width: 130,
              render: (action: string) => <Tag>{t(`vectorImport.actions.${action}`)}</Tag>,
            },
          ]}
        />
      </Drawer>
    </>
  );
}
