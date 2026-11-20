// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, DownloadOutlined, HistoryOutlined, UploadOutlined } from '@ant-design/icons';
import { App, Popconfirm, Popover, Tag, Upload } from 'antd';
import Button from '../Button.tsx';
import ReadFailedAlert from '../ReadFailedAlert.tsx';
import List from '../List.tsx';
import { useTranslation } from 'react-i18next';
import { ApiError, isConcurrencyConflict } from '../../api/client.ts';
import { useDeleteFileVersion, useFileConfig, useFileVersions, useUploadFileVersion } from '../../api/hooks.ts';
import { uploadReasonKey } from '../uploads/uploadReasons.ts';
import { formatSize } from './fileFormat.ts';

/**
 * Version chain of a file, in a click popover. Editor-only — the server returns 403 to
 * read-only callers, so the interactive trigger renders only when `canEdit`; read-only viewers
 * see just a "v2" badge on files that have been superseded. Uploading a new version repoints
 * every attachment of the file to the new head (server-side, in one transaction).
 */
export default function FileVersions({
  fileId,
  versionNumber,
  canEdit,
}: {
  fileId: string;
  versionNumber: number;
  canEdit: boolean;
}) {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const [open, setOpen] = useState(false);
  const { data: versions, isLoading, isError, error, refetch } = useFileVersions(fileId, open);
  const uploadVersion = useUploadFileVersion();
  const deleteVersion = useDeleteFileVersion();
  const { data: fileConfig } = useFileConfig();

  if (!canEdit) {
    return versionNumber > 1 ? <Tag>{`v${versionNumber}`}</Tag> : null;
  }

  const onUpload = async (file: File) => {
    // Checked before sending, as a first upload is: a cap learned only after the whole file has
    // crossed the network is a wasted transfer.
    if (fileConfig && file.size > fileConfig.maxUploadBytes) {
      message.error(t('attachments.tooLarge', { max: formatSize(fileConfig.maxUploadBytes) }));
      return;
    }
    try {
      await uploadVersion.mutateAsync({ fileId, file });
      message.success(t('common.saved'));
    } catch (error) {
      // A refusal is worded by the same catalogue as any other upload's — too large, a type
      // this installation does not take, no room left — rather than as a bare "save failed".
      message.error(
        error instanceof ApiError
          ? t(uploadReasonKey(error.code, 'failed', error.status))
          : t(uploadReasonKey(undefined, 'failed', 0)),
      );
    }
  };

  const onDelete = async (id: string) => {
    try {
      await deleteVersion.mutateAsync(id);
      message.success(t('common.deleted'));
    } catch (error) {
      if (isConcurrencyConflict(error)) {
        return;
      }
      const code = error instanceof ApiError ? error.code : undefined;
      message.error(
        t(
          code === 'file.version_in_use'
            ? 'attachments.versionInUse'
            : code === 'file.head_undeletable'
              ? 'attachments.headUndeletable'
              : 'common.deleteFailed',
        ),
      );
    }
  };

  const content = (
    <div style={{ width: 320 }}>
      <Upload
        showUploadList={false}
        customRequest={({ file, onSuccess, onError }) => {
          onUpload(file as File).then(() => onSuccess?.(null), (e: Error) => onError?.(e));
        }}
      >
        <Button size="small" icon={<UploadOutlined />} loading={uploadVersion.isPending}>
          {t('attachments.uploadVersion')}
        </Button>
      </Upload>
      <ReadFailedAlert
        isError={isError}
        error={error}
        held={versions !== undefined}
        onRetry={refetch}
        title={t('attachments.versionsLoadFailed')}
        testId="file-versions-load-failed"
        style={{ marginTop: 8, marginBottom: 0 }}
      />
      <List
        size="small"
        loading={isLoading}
        style={{ marginTop: 8 }}
        dataSource={versions ?? []}
        locale={{ emptyText: isError && !versions ? <span /> : t('attachments.noVersions') }}
        renderItem={(version) => (
          <List.Item
            actions={[
              <Button
                key="download"
                size="small"
                type="text"
                icon={<DownloadOutlined />}
                aria-label={t('iconButtons.download')}
                href={version.contentUrl}
                download={version.originalName}
              />,
              ...(version.isHead
                ? []
                : [
                    <Popconfirm
                      key="delete"
                      title={t('attachments.deleteVersionConfirm')}
                      okButtonProps={{ danger: true }}
                      onConfirm={() => void onDelete(version.id)}
                    >
                      <Button size="small" type="text" danger icon={<DeleteOutlined />} aria-label={t('iconButtons.delete')} />
                    </Popconfirm>,
                  ]),
            ]}
          >
            <List.Item.Meta
              title={
                <span>
                  {`v${version.versionNumber}`}
                  {version.isHead && (
                    <Tag color="green" style={{ marginLeft: 6 }}>
                      {t('attachments.currentVersion')}
                    </Tag>
                  )}
                </span>
              }
              description={`${formatSize(version.sizeBytes)} · ${version.uploaderName ?? t('history.systemUser')} · ${new Date(version.createdAt).toLocaleDateString(i18n.resolvedLanguage)}`}
            />
          </List.Item>
        )}
      />
    </div>
  );

  return (
    <Popover content={content} title={t('attachments.versions')} trigger="click" open={open} onOpenChange={setOpen}>
      <Button size="small" type="text" icon={<HistoryOutlined />} aria-label={t('attachments.versions')}>
        {versionNumber > 1 ? `v${versionNumber}` : null}
      </Button>
    </Popover>
  );
}
