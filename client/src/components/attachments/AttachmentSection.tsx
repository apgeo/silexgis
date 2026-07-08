// SPDX-License-Identifier: AGPL-3.0-or-later
import { DeleteOutlined, DownloadOutlined, FileOutlined, InboxOutlined } from '@ant-design/icons';
import { App, Button, Card, Empty, Flex, Image, List, Popconfirm, Typography, Upload } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useAttachments,
  useCreateAttachment,
  useDeleteAttachment,
  useUploadFile,
  type AttachedEntityType,
  type AttachmentInfo,
} from '../../api/hooks.ts';

interface AttachmentSectionProps {
  entityType: AttachedEntityType;
  entityId: string;
  canEdit: boolean;
}

function formatSize(bytes: number): string {
  if (bytes >= 1024 * 1024) {
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
  return `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

/**
 * Photo gallery + documents list for one entity. Delivery URLs carry short-lived
 * tokens minted by the server, so plain img/src and anchor downloads work without
 * auth headers; the query refreshes them before expiry.
 */
export default function AttachmentSection({ entityType, entityId, canEdit }: AttachmentSectionProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: attachments } = useAttachments(entityType, entityId);
  const uploadFile = useUploadFile();
  const createAttachment = useCreateAttachment();
  const deleteAttachment = useDeleteAttachment();

  const photos = (attachments ?? []).filter((a) => a.file.kind === 'image');
  const documents = (attachments ?? []).filter((a) => a.file.kind !== 'image');

  const onUpload = async (file: File) => {
    try {
      const stored = await uploadFile.mutateAsync(file);
      await createAttachment.mutateAsync({
        fileId: stored.id,
        entityType,
        entityId,
        // Sensible default roles; richer role/caption editing comes with the media polish.
        role: stored.kind === 'image'
          ? (entityType === 'cave' ? 'photoEntrance' : 'photoSurface')
          : 'document',
        caption: null,
        sortOrder: (attachments?.length ?? 0) + 1,
      });
      message.success(t('common.saved'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const onDelete = async (attachment: AttachmentInfo) => {
    try {
      await deleteAttachment.mutateAsync(attachment.id);
      message.success(t('common.deleted'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Card title={t('attachments.title')} size="small" style={{ marginTop: 16 }}>
      {canEdit && (
        <Upload.Dragger
          multiple
          showUploadList={false}
          customRequest={({ file, onSuccess, onError }) => {
            onUpload(file as File).then(() => onSuccess?.(null), (e: Error) => onError?.(e));
          }}
          style={{ marginBottom: 16 }}
        >
          <p className="ant-upload-drag-icon">
            <InboxOutlined />
          </p>
          <p className="ant-upload-text">{t('attachments.uploadPrompt')}</p>
        </Upload.Dragger>
      )}

      {photos.length === 0 && documents.length === 0 && (
        <Empty description={t('attachments.empty')} image={Empty.PRESENTED_IMAGE_SIMPLE} />
      )}

      {photos.length > 0 && (
        <>
          <Typography.Text strong>{t('attachments.photos')}</Typography.Text>
          <Image.PreviewGroup>
            <Flex gap={12} wrap style={{ marginTop: 8, marginBottom: 12 }}>
              {photos.map((attachment) => (
                <figure key={attachment.id} style={{ margin: 0, width: 160 }}>
                  <Image
                    src={attachment.file.thumbnailUrl ?? attachment.file.contentUrl}
                    preview={{ src: attachment.file.contentUrl }}
                    width={160}
                    height={120}
                    style={{ objectFit: 'cover', borderRadius: 6 }}
                    alt={attachment.caption ?? attachment.file.originalName}
                  />
                  <Flex justify="space-between" align="center">
                    <Typography.Text type="secondary" style={{ fontSize: 12 }} ellipsis>
                      {attachment.caption ?? attachment.file.originalName}
                    </Typography.Text>
                    {canEdit && (
                      <Popconfirm
                        title={t('attachments.deleteConfirm')}
                        onConfirm={() => void onDelete(attachment)}
                        okButtonProps={{ danger: true }}
                      >
                        <Button size="small" type="text" danger icon={<DeleteOutlined />} />
                      </Popconfirm>
                    )}
                  </Flex>
                </figure>
              ))}
            </Flex>
          </Image.PreviewGroup>
        </>
      )}

      {documents.length > 0 && (
        <>
          <Typography.Text strong>{t('attachments.documents')}</Typography.Text>
          <List
            size="small"
            dataSource={documents}
            renderItem={(attachment) => (
              <List.Item
                actions={[
                  <Button
                    key="download"
                    size="small"
                    icon={<DownloadOutlined />}
                    href={attachment.file.contentUrl}
                    download={attachment.file.originalName}
                  />,
                  ...(canEdit
                    ? [
                        <Popconfirm
                          key="delete"
                          title={t('attachments.deleteConfirm')}
                          onConfirm={() => void onDelete(attachment)}
                          okButtonProps={{ danger: true }}
                        >
                          <Button size="small" danger type="text" icon={<DeleteOutlined />} />
                        </Popconfirm>,
                      ]
                    : []),
                ]}
              >
                <List.Item.Meta
                  avatar={<FileOutlined />}
                  title={attachment.caption ?? attachment.file.originalName}
                  description={`${attachment.file.originalName} · ${formatSize(attachment.file.sizeBytes)}`}
                />
              </List.Item>
            )}
          />
        </>
      )}
    </Card>
  );
}
