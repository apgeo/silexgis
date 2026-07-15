// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, DownloadOutlined, HistoryOutlined, UploadOutlined } from '@ant-design/icons';
import { App, Button, List, Popconfirm, Popover, Tag, Upload } from 'antd';
import { useTranslation } from 'react-i18next';
import { useDeleteFileVersion, useFileVersions, useUploadFileVersion } from '../../api/hooks.ts';
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
  const { data: versions, isLoading } = useFileVersions(fileId, open);
  const uploadVersion = useUploadFileVersion();
  const deleteVersion = useDeleteFileVersion();

  if (!canEdit) {
    return versionNumber > 1 ? <Tag>{`v${versionNumber}`}</Tag> : null;
  }

  const onUpload = async (file: File) => {
    try {
      await uploadVersion.mutateAsync({ fileId, file });
      message.success(t('common.saved'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const onDelete = async (id: string) => {
    try {
      await deleteVersion.mutateAsync(id);
      message.success(t('common.deleted'));
    } catch {
      message.error(t('common.saveFailed'));
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
      <List
        size="small"
        loading={isLoading}
        style={{ marginTop: 8 }}
        dataSource={versions ?? []}
        locale={{ emptyText: t('attachments.noVersions') }}
        renderItem={(version) => (
          <List.Item
            actions={[
              <Button
                key="download"
                size="small"
                type="text"
                icon={<DownloadOutlined />}
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
                      <Button size="small" type="text" danger icon={<DeleteOutlined />} />
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
      <Button size="small" type="text" icon={<HistoryOutlined />}>
        {versionNumber > 1 ? `v${versionNumber}` : null}
      </Button>
    </Popover>
  );
}
