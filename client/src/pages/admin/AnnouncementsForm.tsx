// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  App,
  Alert,
  Form,
  InputNumber,
  Switch,
} from 'antd';
import Button from '../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client.ts';
import { type AnnouncementSettings } from '../../api/hooks.ts';
import { useSectionFields } from './useSectionFields.ts';
import { isSettingsConflict, sectionIfMatch, settingsSaveProblem } from './settingsProblems.ts';
import { type SectionProps } from './settingsSection.tsx';

/**
 * What an announcement to a whole caving group may cost this installation.
 *
 * Its own form rather than two more fields on the retention one, because saving a section
 * replaces the whole stored document: a form posting only the retention window would reset a
 * switch about spending money back to its default, and a switch that turns itself off when
 * somebody edits an unrelated field is worse than no switch.
 */
export default function AnnouncementsForm({ settings, onSaved, onStale }: SectionProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<AnnouncementSettings>();
  const [saving, setSaving] = useState(false);

  useSectionFields(form, settings.announcements);

  const save = async (values: AnnouncementSettings) => {
    setSaving(true);
    try {
      const { data, error } = await api.PUT('/api/v1/admin/settings/announcements', {
        headers: sectionIfMatch(settings.versions?.announcements),
        body: values,
      });
      if (error !== undefined || !data) {
        message.error(settingsSaveProblem(error, t));
        if (isSettingsConflict(error)) {
          onStale?.();
        }
        return;
      }
      onSaved(data);
      message.success(t('common.saved'));
    } catch {
      // The request never reached the server (a dropped connection): said, not left unhandled.
      message.error(t('common.saveFailed'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Form form={form} layout="vertical" onFinish={save} requiredMark={false} style={{ maxWidth: 640 }}>
      <Alert type="info" showIcon title={t('admin.messaging.announcementsIntro')} style={{ marginBottom: 16 }} />
      <Form.Item
        name="paidChannelsEnabled"
        label={t('admin.messaging.paidChannelsEnabled')}
        valuePropName="checked"
        extra={t('admin.messaging.paidChannelsEnabledHint')}
      >
        <Switch />
      </Form.Item>
      <Form.Item
        name="dailyPaidMessageCap"
        label={t('admin.messaging.dailyPaidMessageCap')}
        extra={t('admin.messaging.dailyPaidMessageCapHint')}
      >
        {/* The same bounds the server refuses outside of, so a number nobody could have meant is
            caught at the form rather than after a round trip. */}
        <InputNumber min={1} max={1000} step={10} />
      </Form.Item>
      <Button type="primary" htmlType="submit" loading={saving}>
        {t('common.save')}
      </Button>
    </Form>
  );
}
