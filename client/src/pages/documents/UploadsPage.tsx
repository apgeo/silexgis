// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { CloudServerOutlined } from '@ant-design/icons';
import {
  Alert, App, Button, Card, Descriptions, Flex, Form, Input, Modal, Select, Space, Table, Tag,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  useCabinets,
  useImportDirectory,
  useImportRoots,
  useUploadBatch,
  useUploadBatchItems,
  useUploadBatches,
  type UploadBatchInfo,
  type UploadBatchItemInfo,
} from '../../api/hooks.ts';
import { useIsFullAdmin } from '../../components/reslinks/permissions.ts';
import { uploadReasonKey } from '../../components/uploads/uploadReasons.ts';
import { formatSize } from '../../components/attachments/fileFormat.ts';

/**
 * Drops: what arrived together, from where, and what became of each file.
 *
 * <p>
 * A batch is a record rather than a thing anybody owns, so this shows the caller their own.
 * The report is the part that earns the page: "12 of 500 failed" is useless, and a list naming
 * each file with a reason is something somebody can act on.
 * </p>
 * <p>
 * Importing from a directory on the server lives here too, because it produces exactly the
 * same record as a browser drop and is read in the same place. It is a full administrator's
 * button, and even for them it only reaches directories the operator listed at deployment —
 * an administrator account is not the same thing as the operator who owns the machine.
 * </p>
 */
export default function UploadsPage() {
  const { t } = useTranslation();
  const isFullAdmin = useIsFullAdmin();

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [openBatchId, setOpenBatchId] = useState<string>();
  const [importing, setImporting] = useState(false);

  const { data: batches, isFetching } = useUploadBatches(page, pageSize);

  return (
    <div style={{ padding: 24, overflow: 'auto', height: '100%' }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('uploads.batches')}
        </Typography.Title>
        {isFullAdmin && (
          <Button icon={<CloudServerOutlined />} onClick={() => setImporting(true)}>
            {t('uploads.importDirectory')}
          </Button>
        )}
      </Flex>

      <Table<UploadBatchInfo>
        rowKey="id"
        size="middle"
        scroll={{ x: 'max-content' }}
        loading={isFetching && !batches}
        dataSource={batches?.items}
        locale={{ emptyText: t('uploads.batchesEmpty') }}
        onChange={(pagination) => {
          setPage(pagination.current ?? 1);
          setPageSize(pagination.pageSize ?? 20);
        }}
        pagination={{
          current: batches?.page,
          pageSize: batches?.pageSize,
          total: batches?.totalItems,
          showSizeChanger: true,
        }}
        columns={[
          {
            title: t('uploads.label'),
            dataIndex: 'label',
            ellipsis: true,
            render: (value: string | null, row: UploadBatchInfo) => (
              <Typography.Link onClick={() => setOpenBatchId(row.id)}>
                {value ?? row.sourceDescription ?? t('uploads.title')}
              </Typography.Link>
            ),
          },
          {
            title: t('uploads.batchSource'),
            dataIndex: 'source',
            width: 150,
            render: (value: string) => <Tag>{t(`uploads.batchSourceValues.${value}`)}</Tag>,
          },
          {
            title: t('uploads.batchStatus'),
            dataIndex: 'status',
            width: 130,
            render: (value: string, row: UploadBatchInfo) => (
              <Tag color={value === 'failed' ? 'error' : value === 'completed' ? 'success' : 'processing'}>
                {t(`uploads.batchStatusValues.${value}`)}
                {row.error ? ` · ${row.error}` : ''}
              </Tag>
            ),
          },
          {
            title: t('uploads.summary', { stored: '', total: '', skipped: '', failed: '' }).split('·')[0].trim(),
            key: 'counts',
            width: 240,
            render: (_: unknown, row: UploadBatchInfo) =>
              t('uploads.batchCounts', {
                stored: row.storedCount,
                skipped: row.skippedCount,
                failed: row.failedCount,
              }),
          },
          {
            title: t('uploads.batchStarted'),
            dataIndex: 'createdAt',
            width: 180,
            render: (value: string) => new Date(value).toLocaleString(),
          },
        ]}
      />

      <BatchReport batchId={openBatchId} onClose={() => setOpenBatchId(undefined)} />
      {isFullAdmin && (
        <DirectoryImportModal
          open={importing}
          onClose={() => setImporting(false)}
          onStarted={setOpenBatchId}
        />
      )}
    </div>
  );
}

/** One drop's per-file report, polled while the work is still running. */
function BatchReport({ batchId, onClose }: { batchId?: string; onClose: () => void }) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);

  const { data: batch } = useUploadBatch(batchId, true);
  const running = batch?.status === 'running' || batch?.status === 'open';
  const { data: items } = useUploadBatchItems(batchId, { page, pageSize: 50 }, Boolean(batchId));

  return (
    <Modal
      open={Boolean(batchId)}
      onCancel={onClose}
      footer={null}
      width={840}
      destroyOnHidden
      title={batch?.label ?? batch?.sourceDescription ?? t('uploads.batchReport')}
    >
      {batch && (
        <Descriptions size="small" column={2} style={{ marginBottom: 12 }}>
          <Descriptions.Item label={t('uploads.batchSource')}>
            {t(`uploads.batchSourceValues.${batch.source}`)}
          </Descriptions.Item>
          <Descriptions.Item label={t('uploads.batchStatus')}>
            {t(`uploads.batchStatusValues.${batch.status}`)}
          </Descriptions.Item>
          <Descriptions.Item label={t('uploads.batchReport')} span={2}>
            {t('uploads.batchCounts', {
              stored: batch.storedCount,
              skipped: batch.skippedCount,
              failed: batch.failedCount,
            })}
          </Descriptions.Item>
        </Descriptions>
      )}

      {batch?.error && <Alert type="error" showIcon style={{ marginBottom: 12 }} message={batch.error} />}

      <Table<UploadBatchItemInfo>
        rowKey="id"
        size="small"
        scroll={{ x: 'max-content' }}
        dataSource={items?.items}
        loading={running && !items}
        onChange={(pagination) => setPage(pagination.current ?? 1)}
        pagination={{
          current: items?.page,
          pageSize: items?.pageSize,
          total: items?.totalItems,
          showSizeChanger: false,
        }}
        columns={[
          { title: t('uploads.importPath'), dataIndex: 'sourcePath', ellipsis: true },
          {
            title: t('cabinets.size'),
            dataIndex: 'sizeBytes',
            width: 100,
            align: 'right',
            render: (value: number) => formatSize(value),
          },
          {
            title: t('uploads.batchStatus'),
            dataIndex: 'outcome',
            width: 220,
            render: (value: string, row: UploadBatchItemInfo) =>
              value === 'stored' ? (
                <Tag color="success">{t('uploads.batchStatusValues.completed')}</Tag>
              ) : (
                <Tag color={value === 'failed' ? 'error' : 'warning'}>
                  {t(uploadReasonKey(row.reason ?? undefined, value === 'failed' ? 'failed' : 'skipped'))}
                </Tag>
              ),
          },
        ]}
      />
    </Modal>
  );
}

/** Starting a walk over a directory the server itself can reach. */
function DirectoryImportModal({
  open,
  onClose,
  onStarted,
}: {
  open: boolean;
  onClose: () => void;
  onStarted: (batchId: string) => void;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: roots } = useImportRoots(open);
  const { data: cabinets } = useCabinets(open);
  const start = useImportDirectory();
  const [form] = Form.useForm<{ path: string; label?: string; cabinetId?: string; tagName?: string }>();

  const configured = (roots?.roots ?? []).length > 0;

  const submit = async () => {
    const values = await form.validateFields();
    try {
      const batch = await start.mutateAsync({
        path: values.path,
        label: values.label ?? null,
        cabinetId: values.cabinetId ?? null,
        tagName: values.tagName ?? null,
      });
      message.success(t('uploads.importStarted'));
      onStarted(batch.id);
      onClose();
    } catch (error) {
      const code = error instanceof ApiError ? error.code : undefined;
      const named: Record<string, string> = {
        'import.path_not_allowed': 'uploads.importRoots',
        'import.no_roots_configured': 'uploads.importNoRoots',
        'import.directory_not_found': 'uploads.reason.unreadable',
      };
      message.error(t(code && named[code] ? named[code] : 'common.saveFailed'));
    }
  };

  return (
    <Modal
      open={open}
      title={t('uploads.importDirectory')}
      onCancel={onClose}
      onOk={() => void submit()}
      okButtonProps={{ disabled: !configured }}
      confirmLoading={start.isPending}
      destroyOnHidden
    >
      <Typography.Paragraph type="secondary">{t('uploads.importDirectoryHint')}</Typography.Paragraph>

      {configured ? (
        <Card size="small" style={{ marginBottom: 12 }} title={t('uploads.importRoots')}>
          <Space direction="vertical" size={2}>
            {roots!.roots.map((root) => (
              <Typography.Text key={root} code>
                {root}
              </Typography.Text>
            ))}
          </Space>
        </Card>
      ) : (
        <Alert type="warning" showIcon style={{ marginBottom: 12 }} message={t('uploads.importNoRoots')} />
      )}

      <Form form={form} layout="vertical" disabled={!configured}>
        <Form.Item name="path" label={t('uploads.importPath')} rules={[{ required: true }, { max: 1000 }]}>
          <Input placeholder={roots?.roots[0]} />
        </Form.Item>
        <Form.Item name="cabinetId" label={t('cabinets.title')}>
          <Select
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder={t('cabinets.pickCabinet')}
            options={(cabinets ?? []).map((cabinet) => ({ value: cabinet.id, label: cabinet.name }))}
          />
        </Form.Item>
        <Form.Item name="label" label={t('uploads.label')} rules={[{ max: 200 }]}>
          <Input />
        </Form.Item>
        <Form.Item name="tagName" label={t('uploads.tag')} rules={[{ max: 100 }]}>
          <Input />
        </Form.Item>
      </Form>
    </Modal>
  );
}
