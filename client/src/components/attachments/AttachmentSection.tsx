// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  DeleteOutlined, DownloadOutlined, FileOutlined, InboxOutlined, StarFilled, StarOutlined,
} from '@ant-design/icons';
import { ApiError, isConcurrencyConflict } from '../../api/client.ts';
import {
  App, Card, Empty, Flex, Image, Popconfirm, Tooltip, Typography, Upload, theme,
} from 'antd';
import Button from '../Button.tsx';
import List from '../List.tsx';
import { useTranslation } from 'react-i18next';
import {
  useAttachments,
  useCreateAttachment,
  useDeleteAttachment,
  useFileConfig,
  useSetPrimaryAttachment,
  useUploadFile,
  type AttachedEntityType,
  type AttachmentInfo,
  type AttachmentRole,
} from '../../api/hooks.ts';
import { displayableImageUrl } from '../documents/derivativeUrl.ts';
import OpenDocument from '../documents/OpenDocument.tsx';
import AttachmentDetails from './AttachmentDetails.tsx';
import DocumentMetadata from './DocumentMetadata.tsx';
import FileVersions from './FileVersions.tsx';
import PhotoFactsPanel from './PhotoFactsPanel.tsx';
import PhotoPositionAction from './PhotoPositionAction.tsx';
import { formatSize } from './fileFormat.ts';
import LocationRemovedTag from './LocationRemovedTag.tsx';
import { useDuplicatePrompt } from '../uploads/useDuplicatePrompt.ts';
import { uploadReasonKey } from '../uploads/uploadReasons.ts';
import { downloadTitle } from './downloadTitle.ts';
import PlaceMapAction from '../../rastermap/PlaceMapAction.tsx';

interface AttachmentSectionProps {
  entityType: AttachedEntityType;
  entityId: string;
  canEdit: boolean;
  /**
   * When set, a distinct "Report document" slot is shown for the report-role attachments (the
   * trip's completed report) and they are kept out of the generic lists.
   */
  reportSlot?: boolean;
  /**
   * Role given to image uploads without an explicit role. Cave pages pass
   * photoEntrance; everything else photographs the surface.
   */
  defaultPhotoRole?: AttachmentRole;
  /**
   * 'bare' drops the card chrome for a host that draws its own header — the selection panel's
   * section shell. A card inside a section header is two borders around one gallery.
   */
  variant?: 'card' | 'bare';
  /** False asks for nothing at all, which is what a collapsed panel section passes. */
  enabled?: boolean;
}

/**
 * Photo gallery + documents list for one entity. Delivery URLs carry short-lived
 * tokens minted by the server, so plain img/src and anchor downloads work without
 * auth headers; the query refreshes them before expiry.
 */
export default function AttachmentSection({
  entityType,
  entityId,
  canEdit,
  reportSlot,
  defaultPhotoRole = 'photoSurface',
  variant = 'card',
  enabled = true,
}: AttachmentSectionProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: attachments } = useAttachments(entityType, entityId, enabled);
  const { data: fileConfig } = useFileConfig();
  const uploadFile = useUploadFile();
  const createAttachment = useCreateAttachment();
  const deleteAttachment = useDeleteAttachment();
  const setPrimary = useSetPrimaryAttachment();
  const { token } = theme.useToken();

  // Starring is one click with no dialog around it, so its outcome is said either way: a
  // refusal (a second editor removed the photo, the right was withdrawn) would otherwise pass
  // as nothing happening at all.
  const togglePrimary = async (attachment: AttachmentInfo) => {
    try {
      await setPrimary.mutateAsync({ id: attachment.id, primary: !attachment.isPrimary });
      message.success(t(attachment.isPrimary ? 'attachments.primaryCleared' : 'attachments.primarySet'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  // The report documents live in their own slot and are kept out of the generic lists. Usually
  // one, but a club that uploaded the report it wrote keeps it when a write-up is generated
  // beside it — the generated one replaces the generated one, and nothing silently removes a
  // document somebody put here by hand.
  const reports = reportSlot ? (attachments ?? []).filter((a) => a.role === 'report') : [];
  const inSlot = new Set(reports.map((a) => a.id));
  const photos = (attachments ?? []).filter((a) => a.file.kind === 'image' && !inSlot.has(a.id));
  const documents = (attachments ?? []).filter((a) => a.file.kind !== 'image' && !inSlot.has(a.id));

  // Attaching is one of the commonest reasons somebody genuinely wants a second copy of bytes
  // the archive already holds; the refusal names a copy this caller can see.
  const askAboutDuplicate = useDuplicatePrompt();

  const onUpload = async (file: File, roleOverride?: AttachmentRole) => {
    // Checked before the transfer starts, against the limit the server publishes rather
    // than a number compiled in here — an installation may raise it, and transferring half
    // a gigabyte only to be refused at the end is the worst way to find that out.
    if (fileConfig && file.size > fileConfig.maxUploadBytes) {
      message.error(t('attachments.tooLarge', { max: formatSize(fileConfig.maxUploadBytes) }));
      throw new Error('file too large');
    }

    try {
      let stored;
      try {
        stored = await uploadFile.mutateAsync({ file });
      } catch (error) {
        if (!(error instanceof ApiError) || error.code !== 'file.duplicate') {
          throw error;
        }

        if (!(await askAboutDuplicate(true))) {
          return;
        }

        stored = await uploadFile.mutateAsync({ file, allowDuplicate: true });
      }

      await createAttachment.mutateAsync({
        fileId: stored.id,
        entityType,
        entityId,
        // Sensible default roles; richer role/caption editing comes with the media polish.
        role: roleOverride ?? (stored.kind === 'image' ? defaultPhotoRole : 'document'),
        caption: null,
        sortOrder: (attachments?.length ?? 0) + 1,
      });
      message.success(t('common.saved'));
    } catch (error) {
      // Worded by the catalogue every upload uses — too large, a type this installation does not
      // take, no room left, the connection lost — rather than as a bare "save failed".
      message.error(
        error instanceof ApiError
          ? t(uploadReasonKey(error.code, 'failed', error.status))
          : t(uploadReasonKey(undefined, 'failed', 0)),
      );
    }
  };

  const onDelete = async (attachment: AttachmentInfo) => {
    try {
      await deleteAttachment.mutateAsync(attachment.id);
      message.success(t('common.deleted'));
    } catch (error) {
      if (!isConcurrencyConflict(error)) {
        message.error(t('common.deleteFailed'));
      }
    }
  };

  const body = (
    <>
      {reportSlot && (
        <div style={{ marginBottom: 16 }}>
          <Typography.Text strong>{t('attachments.reportDocument')}</Typography.Text>
          {reports.length > 0 ? (
            <List
              size="small"
              dataSource={reports}
              renderItem={(attachment) => (
                <AttachmentRow
                  attachment={attachment}
                  canEdit={canEdit}
                  emphasiseDownload
                  onDelete={onDelete}
                />
              )}
            />
          ) : canEdit ? (
            <Upload.Dragger
              showUploadList={false}
              customRequest={({ file, onSuccess, onError }) => {
                onUpload(file as File, 'report').then(() => onSuccess?.(null), (e: Error) => onError?.(e));
              }}
              style={{ marginTop: 8 }}
            >
              <p className="ant-upload-drag-icon">
                <FileOutlined />
              </p>
              <p className="ant-upload-text">{t('attachments.uploadReport')}</p>
            </Upload.Dragger>
          ) : (
            <div style={{ marginTop: 8 }}>
              <Empty description={t('attachments.noReport')} image={Empty.PRESENTED_IMAGE_SIMPLE} />
            </div>
          )}
        </div>
      )}

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
                    // Enlarging is not a way past the byte rule: a caller who may not be
                    // told where a photo was taken is given the largest *rendering*
                    // instead of the stored bytes, which the original link would have
                    // answered as a missing file anyway.
                    preview={{ src: displayableImageUrl(attachment.file) ?? undefined }}
                    width={160}
                    height={120}
                    style={{ objectFit: 'cover', borderRadius: 6 }}
                    alt={attachment.caption ?? attachment.file.originalName}
                  />
                  <Flex justify="space-between" align="center">
                    <Typography.Text type="secondary" style={{ fontSize: 12 }} ellipsis>
                      {attachment.caption ?? attachment.file.originalName}
                    </Typography.Text>
                    <LocationRemovedTag file={attachment.file} />
                    <Flex align="center">
                      {/* The one picture that stands for this object wherever it is named — a
                          list row, a card, the panel beside the map. Clicking the current one
                          again clears it, so an object can go back to having no headline. */}
                      {canEdit && (
                        <Tooltip
                          title={t(attachment.isPrimary ? 'attachments.clearPrimary' : 'attachments.makePrimary')}
                        >
                          <Button
                            size="small"
                            type="text"
                            aria-label={t(attachment.isPrimary ? 'attachments.clearPrimary' : 'attachments.makePrimary')}
                            aria-pressed={attachment.isPrimary}
                            loading={setPrimary.isPending && setPrimary.variables?.id === attachment.id}
                            icon={
                              attachment.isPrimary ? (
                                <StarFilled style={{ color: token.colorWarning }} />
                              ) : (
                                <StarOutlined />
                              )
                            }
                            onClick={() => void togglePrimary(attachment)}
                          />
                        </Tooltip>
                      )}
                      <OpenDocument documentId={attachment.file.documentId} />
                      {/* A map filed on a cave opens as a map, with the places phones pinned on it. */}
                      {attachment.role === 'map2d' && entityType === 'feature' && (
                        <PlaceMapAction
                          documentId={attachment.file.documentId}
                          title={attachment.caption ?? null}
                          canEdit={canEdit}
                        />
                      )}
                      <PhotoFactsPanel file={attachment.file} />
                      <PhotoPositionAction
                        file={attachment.file}
                        featureId={entityType === 'feature' ? entityId : undefined}
                        canEdit={canEdit}
                      />
                      {canEdit && <AttachmentDetails attachment={attachment} />}
                      {canEdit && <DocumentMetadata documentId={attachment.file.documentId} />}
                      <FileVersions
                        fileId={attachment.file.id}
                        versionNumber={attachment.file.versionNumber}
                        canEdit={canEdit}
                      />
                      {canEdit && (
                        <Popconfirm
                          title={t('attachments.deleteConfirm')}
                          onConfirm={() => void onDelete(attachment)}
                          okButtonProps={{ danger: true }}
                        >
                          <Button size="small" type="text" danger icon={<DeleteOutlined />} aria-label={t('iconButtons.delete')} />
                        </Popconfirm>
                      )}
                    </Flex>
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
              <AttachmentRow attachment={attachment} canEdit={canEdit} onDelete={onDelete} />
            )}
          />
        </>
      )}
    </>
  );

  return variant === 'bare' ? (
    body
  ) : (
    <Card title={t('attachments.title')} size="small" style={{ marginTop: 16 }}>
      {body}
    </Card>
  );
}

/**
 * One document in a list: the report slot and the documents list draw the same row, so a
 * control added to one cannot quietly be missing from the other.
 */
function AttachmentRow({
  attachment,
  canEdit,
  emphasiseDownload = false,
  onDelete,
}: {
  attachment: AttachmentInfo;
  canEdit: boolean;
  /** The report slot's one document is what the trip is read for, so its download stands out. */
  emphasiseDownload?: boolean;
  onDelete: (attachment: AttachmentInfo) => Promise<void>;
}) {
  const { t } = useTranslation();
  return (
    <List.Item
      actions={[
        <OpenDocument key="open" documentId={attachment.file.documentId} />,
        ...(canEdit
          ? [
              <AttachmentDetails key="details" attachment={attachment} />,
              <DocumentMetadata key="document" documentId={attachment.file.documentId} />,
            ]
          : []),
        <FileVersions
          key="versions"
          fileId={attachment.file.id}
          versionNumber={attachment.file.versionNumber}
          canEdit={canEdit}
        />,
        // A photo whose own coordinates this caller may not be given is delivered as renderings
        // only; the link would answer as a missing file, so the control says why instead of
        // failing when followed.
        <Tooltip key="download" title={downloadTitle(attachment.file, t)}>
          <Button
            type={emphasiseDownload ? 'primary' : undefined}
            size="small"
            icon={<DownloadOutlined />}
            aria-label={t('iconButtons.download')}
            disabled={!attachment.file.mayDownloadOriginal}
            href={attachment.file.mayDownloadOriginal ? attachment.file.contentUrl : undefined}
            download={attachment.file.originalName}
          />
        </Tooltip>,
        ...(canEdit
          ? [
              <Popconfirm
                key="delete"
                title={t('attachments.deleteConfirm')}
                onConfirm={() => void onDelete(attachment)}
                okButtonProps={{ danger: true }}
              >
                <Button size="small" danger type="text" icon={<DeleteOutlined />} aria-label={t('iconButtons.delete')} />
              </Popconfirm>,
            ]
          : []),
      ]}
    >
      <List.Item.Meta
        avatar={<FileOutlined />}
        title={attachment.caption ?? attachment.file.originalName}
        description={[attachment.file.originalName, formatSize(attachment.file.sizeBytes), attachment.file.documentDate]
          .filter(Boolean)
          .join(' · ')}
      />
    </List.Item>
  );
}
