// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useRef, useState } from 'react';
import {
  CheckCircleTwoTone, CloseCircleTwoTone, FolderOpenOutlined, InboxOutlined,
  MinusCircleTwoTone, ReloadOutlined,
} from '@ant-design/icons';
import {
  Alert, App, Button, Checkbox, Drawer, Flex, Form, Input, Progress, Space, Tag, Typography,
  Upload,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { useQueryClient } from '@tanstack/react-query';
import { useFileConfig, useOpenUploadBatch, useCloseUploadBatch } from '../../api/hooks.ts';
import { formatSize } from '../attachments/fileFormat.ts';
import List from '../List.tsx';
import type { UploadItem } from './uploadPlan.ts';
import { uploadReasonKey } from './uploadReasons.ts';
import { useUploadQueue } from './useUploadQueue.ts';
import type { UploadTarget } from './uploadTransport.ts';

export interface UploadDrawerProps {
  open: boolean;
  onClose: () => void;
  /** The shelf everything is aimed at, when the drop names one. */
  cabinetId?: string;
  /** Shown so somebody can see where their files are about to go. */
  cabinetName?: string;
  /** The keys this shelf expects, so the checklist is visible before anything is uploaded. */
  requiredMetadataKeys?: readonly string[];
  /** The object to attach to, when the drop was started from one. */
  attach?: { entityType: string; entityId: string; role?: string };
  /** Called once the drop has finished and something actually landed. */
  onUploaded?: () => void;
}

/**
 * A drop: files or a whole folder, sent a few at a time, with a row per file saying what
 * became of it.
 *
 * <p>
 * The three things this exists to fix, in order of how often they bite: uploading is one drop
 * rather than one dialog per file; a folder keeps its structure instead of being flattened;
 * and a failure names the file it happened to, so twelve failures out of four hundred are
 * twelve things somebody can look at rather than a number.
 * </p>
 * <p>
 * The limits are shown before anything is chosen — size cap, accepted types, room left. A
 * quota discovered at the end of a 400 MB transfer is the same as no warning at all.
 * </p>
 */
export default function UploadDrawer({
  open,
  onClose,
  cabinetId,
  cabinetName,
  requiredMetadataKeys,
  attach,
  onUploaded,
}: UploadDrawerProps) {
  const { t } = useTranslation();
  const { modal } = App.useApp();
  const queryClient = useQueryClient();
  const { data: config } = useFileConfig();
  const openBatch = useOpenUploadBatch();
  const closeBatch = useCloseUploadBatch();

  const [label, setLabel] = useState('');
  const [tagName, setTagName] = useState('');
  const [expandArchives, setExpandArchives] = useState(true);
  const [batchId, setBatchId] = useState<string>();

  // Held in a ref as well: the queue's target function is called from the upload loop, which
  // does not re-read React state.
  const batchRef = useRef<string | undefined>(undefined);
  batchRef.current = batchId;

  const limits = useMemo(
    () => ({
      chunkBytes: config?.chunkBytes ?? 8 * 1024 * 1024,
      resumableThresholdBytes: config?.resumableThresholdBytes ?? 0,
    }),
    [config],
  );

  const queue = useUploadQueue(
    limits,
    (item: UploadItem): UploadTarget => ({
      cabinetId,
      relativePath: item.relativePath,
      attachEntityType: attach?.entityType,
      attachEntityId: attach?.entityId,
      attachRole: attach?.role,
      batchId: batchRef.current,
      expandArchive: expandArchives && item.file.name.toLowerCase().endsWith('.zip'),
    }),
    {
      onDuplicate: (documentId) =>
        new Promise<boolean>((resolve) => {
          modal.confirm({
            title: t('uploads.duplicateTitle'),
            content: documentId
              ? t('uploads.duplicateKnown')
              : t('uploads.duplicateUnknown'),
            okText: t('uploads.duplicateStoreAnyway'),
            cancelText: t('uploads.duplicateSkip'),
            onOk: () => resolve(true),
            onCancel: () => resolve(false),
          });
        }),
    },
  );

  const { summary, items, add, retryFailed, clear } = queue;

  // A drop is opened lazily, on the first file: opening one for a drawer somebody closed
  // without uploading anything would leave an empty record behind every time.
  const ensureBatch = async () => {
    if (batchRef.current) {
      return;
    }

    try {
      const created = await openBatch.mutateAsync({
        label: label.trim() || null,
        cabinetId: cabinetId ?? null,
        tagName: tagName.trim() || null,
      });
      batchRef.current = created.id;
      setBatchId(created.id);
    } catch {
      // A drop that could not be opened is not a reason to refuse the upload: the files land
      // exactly as they would have, they simply carry no batch reference.
    }
  };

  // Everything the archive touched — the tree, its listings, the inbox — is stale once
  // anything lands.
  useEffect(() => {
    if (!summary.running && summary.stored > 0) {
      void queryClient.invalidateQueries({ queryKey: ['cabinets'] });
      void queryClient.invalidateQueries({ queryKey: ['documents'] });
      void queryClient.invalidateQueries({ queryKey: ['attachments'] });
      void queryClient.invalidateQueries({ queryKey: ['upload-batches'] });
      void queryClient.invalidateQueries({ queryKey: ['file-config'] });
      onUploaded?.();
    }
    // Deliberately keyed on the finished-ness rather than on every progress tick, so a drop
    // of four hundred files does not invalidate the tree four hundred times.
  }, [summary.running, summary.stored, queryClient, onUploaded]);

  const finish = () => {
    if (batchRef.current) {
      void closeBatch.mutateAsync(batchRef.current).catch(() => undefined);
    }
    batchRef.current = undefined;
    setBatchId(undefined);
    clear();
    setLabel('');
    setTagName('');
    onClose();
  };

  const accept = async (file: File) => {
    await ensureBatch();
    add([file]);
    // False keeps antd from doing an upload of its own: the queue owns the transfer.
    return false as const;
  };

  return (
    <Drawer
      open={open}
      onClose={finish}
      width={560}
      destroyOnHidden
      title={cabinetName ? t('uploads.titleInto', { cabinet: cabinetName }) : t('uploads.title')}
      extra={
        <Space>
          {summary.failed > 0 && (
            <Button icon={<ReloadOutlined />} onClick={retryFailed} disabled={summary.running}>
              {t('uploads.retryFailed', { count: summary.failed })}
            </Button>
          )}
          <Button type="primary" onClick={finish} disabled={summary.running}>
            {t('common.close')}
          </Button>
        </Space>
      }
    >
      {config && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          message={t('uploads.limitsTitle')}
          description={
            <Space direction="vertical" size={2}>
              <span>{t('uploads.limitMaxSize', { size: formatSize(config.maxUploadBytes) })}</span>
              {config.remainingBytes !== null && config.remainingBytes !== undefined && (
                <span>{t('uploads.limitRemaining', { size: formatSize(config.remainingBytes) })}</span>
              )}
              <span>
                {config.acceptedExtensions.length > 0
                  ? t('uploads.limitAccepted', { types: config.acceptedExtensions.join(', ') })
                  : t('uploads.limitAcceptsAnything')}
              </span>
              {config.refusedExtensions.length > 0 && (
                <span>{t('uploads.limitRefused', { types: config.refusedExtensions.join(', ') })}</span>
              )}
            </Space>
          }
        />
      )}

      {requiredMetadataKeys && requiredMetadataKeys.length > 0 && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          message={t('uploads.expectsMetadata')}
          description={
            <Space wrap size={4}>
              {requiredMetadataKeys.map((key) => (
                <Tag key={key}>{key}</Tag>
              ))}
            </Space>
          }
        />
      )}

      <Form layout="vertical" size="small" disabled={Boolean(batchId)}>
        <Form.Item label={t('uploads.label')} help={t('uploads.labelHint')}>
          <Input value={label} onChange={(e) => setLabel(e.target.value)} maxLength={200} />
        </Form.Item>
        <Form.Item label={t('uploads.tag')} help={t('uploads.tagHint')}>
          <Input value={tagName} onChange={(e) => setTagName(e.target.value)} maxLength={100} />
        </Form.Item>
      </Form>

      <Checkbox
        checked={expandArchives}
        onChange={(e) => setExpandArchives(e.target.checked)}
        style={{ marginBottom: 12 }}
      >
        {t('uploads.expandArchives')}
      </Checkbox>

      <Flex gap={8} style={{ marginBottom: 12 }}>
        {/* Two zones rather than one with a switch: a browser cannot offer files and folders
            from the same control, and hiding that behind a toggle makes the folder case
            undiscoverable — which is the case a club handing over its archive needs. */}
        <Upload.Dragger
          multiple
          showUploadList={false}
          beforeUpload={accept}
          data-testid="upload-files"
          style={{ flex: 1 }}
        >
          <p className="ant-upload-drag-icon">
            <InboxOutlined />
          </p>
          <p className="ant-upload-text">{t('uploads.dropFiles')}</p>
        </Upload.Dragger>
        <Upload
          directory
          multiple
          showUploadList={false}
          beforeUpload={accept}
          data-testid="upload-folder"
        >
          <Button icon={<FolderOpenOutlined />} style={{ height: '100%' }}>
            {t('uploads.chooseFolder')}
          </Button>
        </Upload>
      </Flex>

      {items.length > 0 && (
        <>
          <Typography.Text strong>
            {t('uploads.summary', {
              stored: summary.stored,
              total: summary.total,
              skipped: summary.skipped,
              failed: summary.failed,
            })}
          </Typography.Text>
          <List
            size="small"
            style={{ marginTop: 8 }}
            dataSource={items}
            renderItem={(item) => <UploadRow item={item} />}
          />
        </>
      )}
    </Drawer>
  );
}

/** One file's row: its path, where it got to, and why it did not get further. */
function UploadRow({ item }: { item: UploadItem }) {
  const { t } = useTranslation();

  const icon = {
    stored: <CheckCircleTwoTone twoToneColor="#52c41a" />,
    skipped: <MinusCircleTwoTone twoToneColor="#faad14" />,
    failed: <CloseCircleTwoTone twoToneColor="#ff4d4f" />,
    pending: null,
    uploading: null,
  }[item.status];

  return (
    <List.Item>
      <List.Item.Meta
        avatar={icon}
        title={
          <Typography.Text ellipsis style={{ maxWidth: 340 }}>
            {item.relativePath}
          </Typography.Text>
        }
        description={
          item.status === 'uploading' || item.status === 'pending' ? (
            <Progress
              percent={Math.round(item.progress * 100)}
              size="small"
              status={item.status === 'pending' ? 'normal' : 'active'}
            />
          ) : (
            <Typography.Text type={item.status === 'failed' ? 'danger' : 'secondary'}>
              {item.status === 'stored'
                ? formatSize(item.file.size)
                : t(uploadReasonKey(item.errorCode, item.status))}
            </Typography.Text>
          )
        }
      />
    </List.Item>
  );
}

