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
  useTripLogConfig,
  type ImportBatch,
} from '../../api/hooks.ts';
import { formatDays } from '../../components/trips/tripRestoreWindow.ts';

/**
 * The confirmations this account has made, newest first, each with the one action a
 * confirmation still has: undoing it.
 *
 * Reverting deletes everything the confirmation created, as one unit — for the case where the
 * mapping was wrong and nobody noticed until the map looked odd. Nothing is removed by it: a
 * trip it deleted is on the list of deleted trips, a cave or feature on the list of deleted
 * caves and features, each can be put back from there, and the confirmation and the lines of an
 * undone batch both say so.
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
  // How long a deleted trip can be put back, for the undo of a batch that created trips.
  const { data: tripConfig } = useTripLogConfig();

  /**
   * What undoing a batch does to the trips it created, for the two sources that create any.
   * Said in the installation's own number, and not at all until the server has said it.
   */
  const tripsRestorable = (source: ImportBatch['source']): string | undefined => {
    if ((source !== 'tripCsv' && source !== 'speleolocArchive') || tripConfig === undefined) {
      return undefined;
    }
    return tripConfig.deletedRetentionDays === null
      ? t('vectorImport.revertTripsRestorable')
      : t('vectorImport.revertTripsRestorableFor', {
          period: formatDays(tripConfig.deletedRetentionDays, i18n.resolvedLanguage),
        });
  };

  /**
   * What the confirmation says an undo leaves recoverable. The caves and features first, since
   * every source can create those; the trips after, for the sources that create any.
   */
  const restorable = (source: ImportBatch['source']): string =>
    [t('vectorImport.revertFeaturesRestorable'), tripsRestorable(source)]
      .filter((sentence) => sentence !== undefined)
      .join(' ');

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
      // A spreadsheet of trips arrives as an ordinary stored file rather than as a geofile, so
      // there is no file name on the batch to show. Saying where it came from is the whole of
      // what this column can honestly say about it.
      case 'tripCsv':
        return t('vectorImport.batchFromTrips');
      // A trip recorded on a phone and exported, and the same again: the archive is a stored file
      // rather than a geofile, so there is no file name on the batch to show.
      case 'speleolocArchive':
        return t('vectorImport.batchFromSpeleoloc');
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
                    description={restorable(row.source)}
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
        data-testid="import-batch-detail"
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
              // Two kinds of line end up in one drawer: a batch of caves and areas read off a
              // map file, and a batch of trips read off a spreadsheet. Branching on the feature
              // alone made every trip in a five-hundred-trip batch read "Nothing created",
              // directly under a header row saying five hundred were.
              render: (
                name: string | null,
                row: {
                  featureId?: string | null;
                  featureDeleted?: boolean;
                  tripLogId?: string | null;
                  tripTitle?: string | null;
                  tripDeleted?: boolean;
                },
              ) => {
                // A deleted cave or feature answers as not found at its own address, as a deleted
                // trip does, so the line does not link there either: it says the object is
                // deleted and sends the reader to where it can be put back.
                if (row.featureId && row.featureDeleted) {
                  return (
                    <Flex gap={8} align="center" wrap>
                      <Typography.Text>{name ?? t('vectorImport.unnamed')}</Typography.Text>
                      <Tag>{t('vectorImport.deletedTag')}</Tag>
                      <Link to="/features/deleted" data-testid="import-batch-feature-restore">
                        {t('vectorImport.featureDeletedRestore')}
                      </Link>
                    </Flex>
                  );
                }
                if (row.featureId) {
                  return <Link to={`/features/${row.featureId}`}>{name ?? t('vectorImport.unnamed')}</Link>;
                }
                // A deleted trip answers as not found at its own address, so the line does not
                // link there: it says the trip is deleted and sends the reader to where it can
                // be put back.
                if (row.tripLogId && row.tripDeleted) {
                  return (
                    <Flex gap={8} align="center" wrap>
                      <Typography.Text>{row.tripTitle ?? t('vectorImport.unnamed')}</Typography.Text>
                      <Tag>{t('vectorImport.deletedTag')}</Tag>
                      <Link to="/trip-logs/deleted" data-testid="import-batch-trip-restore">
                        {t('vectorImport.tripDeletedRestore')}
                      </Link>
                    </Flex>
                  );
                }
                if (row.tripLogId) {
                  return (
                    <Link to={`/trip-logs/${row.tripLogId}`}>
                      {row.tripTitle ?? t('vectorImport.unnamed')}
                    </Link>
                  );
                }
                // A trip removed for good keeps its title here and loses its pointer. Saying what
                // it was named is the whole reason the title is recorded on the line.
                if (row.tripTitle) {
                  return (
                    <Flex gap={8} align="center">
                      <Typography.Text>{row.tripTitle}</Typography.Text>
                      <Tag>{t('vectorImport.deletedTag')}</Tag>
                    </Flex>
                  );
                }
                return <Typography.Text type="secondary">{t('vectorImport.notCreated')}</Typography.Text>;
              },
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
