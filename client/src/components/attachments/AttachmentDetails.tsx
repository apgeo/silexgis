// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState, type ReactNode } from 'react';
import { EditOutlined } from '@ant-design/icons';
import { App, DatePicker, Flex, Input, Popover, Select, Switch, Typography } from 'antd';
import Button from '../Button.tsx';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import {
  useUpdateAttachment,
  useSetDeviceDefaultSet,
  useUpdateFile,
  type AttachmentInfo,
  type AttachmentRole,
} from '../../api/hooks.ts';
import TagChips from '../tags/TagChips.tsx';

const ROLES: AttachmentRole[] = [
  'photoEntrance',
  'photoInterior',
  'photoSurface',
  'document',
  'report',
  'map2d',
  'surveyData',
  'other',
];

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <Flex vertical gap={2}>
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        {label}
      </Typography.Text>
      {children}
    </Flex>
  );
}

/**
 * Edit an attachment's metadata (caption, role, the document's own date) and its tags, in a
 * click popover. Editor-only — every field routes through a Write-gated endpoint; the tags
 * point at the file's head id so they follow the document across versions. The document date
 * lives on the file (shared by every attachment of it); caption/role live on this attachment.
 */
export default function AttachmentDetails({ attachment }: { attachment: AttachmentInfo }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [open, setOpen] = useState(false);
  const [caption, setCaption] = useState(attachment.caption ?? '');
  const [role, setRole] = useState<AttachmentRole>(attachment.role);
  const [documentDate, setDocumentDate] = useState<Dayjs | null>(
    attachment.file.documentDate ? dayjs(attachment.file.documentDate) : null,
  );
  const updateAttachment = useUpdateAttachment();
  const updateFile = useUpdateFile();
  const setDefaultSet = useSetDeviceDefaultSet();

  // Re-sync from server state whenever the popover opens (another edit may have landed).
  useEffect(() => {
    if (open) {
      setCaption(attachment.caption ?? '');
      setRole(attachment.role);
      setDocumentDate(attachment.file.documentDate ? dayjs(attachment.file.documentDate) : null);
    }
  }, [open, attachment]);

  const captionValue = caption.trim() ? caption.trim() : null;
  const dateValue = documentDate?.format('YYYY-MM-DD') ?? null;
  // The caption/role live on the attachment; the document date on the file — each saves through
  // its own endpoint, so track their dirtiness separately and reuse it to gate both.
  const attachmentDirty = captionValue !== (attachment.caption ?? null) || role !== attachment.role;
  const dateDirty = dateValue !== (attachment.file.documentDate ?? null);
  const dirty = attachmentDirty || dateDirty;

  const save = async () => {
    try {
      if (attachmentDirty) {
        await updateAttachment.mutateAsync({
          id: attachment.id,
          role,
          caption: captionValue,
          sortOrder: attachment.sortOrder,
        });
      }
      if (dateDirty) {
        await updateFile.mutateAsync({ id: attachment.file.id, documentDate: dateValue });
      }
      message.success(t('common.saved'));
      setOpen(false);
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  // Saved the moment it is flipped, not with the fields above: it is not an edit of this
  // attachment's description but a choice about what phones fetch, and it has its own route.
  const flipDefaultSet = async (included: boolean) => {
    try {
      await setDefaultSet.mutateAsync({ id: attachment.id, included });
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const content = (
    <Flex vertical gap={10} style={{ width: 280 }}>
      <Field label={t('attachments.caption')}>
        <Input value={caption} onChange={(e) => setCaption(e.target.value)} maxLength={500} />
      </Field>
      <Field label={t('attachments.role')}>
        <Select
          value={role}
          onChange={setRole}
          options={ROLES.map((r) => ({ value: r, label: t(`attachments.roles.${r}`) }))}
        />
      </Field>
      <Field label={t('attachments.documentDate')}>
        <DatePicker
          value={documentDate}
          onChange={setDocumentDate}
          style={{ width: '100%' }}
          // "When the document/photo is from" — a future date makes no sense.
          disabledDate={(d) => d.isAfter(dayjs(), 'day')}
        />
      </Field>
      <Field label={t('tags.title')}>
        <TagChips entityType="storedFile" entityId={attachment.file.id} canEdit />
      </Field>
      <Flex vertical gap={2}>
        <Flex align="center" gap={8}>
          <Switch
            size="small"
            checked={attachment.inDeviceDefaultSet}
            loading={setDefaultSet.isPending}
            onChange={(checked) => void flipDefaultSet(checked)}
            aria-label={t('attachments.inDeviceDefaultSet')}
          />
          <Typography.Text>{t('attachments.inDeviceDefaultSet')}</Typography.Text>
        </Flex>
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {t('attachments.inDeviceDefaultSetHint')}
        </Typography.Text>
      </Flex>
      <Button
        type="primary"
        size="small"
        disabled={!dirty}
        loading={updateAttachment.isPending || updateFile.isPending}
        onClick={() => void save()}
      >
        {t('common.save')}
      </Button>
    </Flex>
  );

  return (
    <Popover content={content} title={t('attachments.details')} trigger="click" open={open} onOpenChange={setOpen}>
      <Button size="small" type="text" icon={<EditOutlined />} aria-label={t('attachments.details')} />
    </Popover>
  );
}
