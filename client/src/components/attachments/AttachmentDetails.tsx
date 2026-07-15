// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState, type ReactNode } from 'react';
import { EditOutlined } from '@ant-design/icons';
import { App, Button, DatePicker, Flex, Input, Popover, Select, Typography } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import {
  useUpdateAttachment,
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
  const dirty =
    captionValue !== (attachment.caption ?? null) ||
    role !== attachment.role ||
    dateValue !== (attachment.file.documentDate ?? null);

  const save = async () => {
    try {
      if (captionValue !== (attachment.caption ?? null) || role !== attachment.role) {
        await updateAttachment.mutateAsync({
          id: attachment.id,
          role,
          caption: captionValue,
          sortOrder: attachment.sortOrder,
        });
      }
      if (dateValue !== (attachment.file.documentDate ?? null)) {
        await updateFile.mutateAsync({ id: attachment.file.id, documentDate: dateValue });
      }
      message.success(t('common.saved'));
      setOpen(false);
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
