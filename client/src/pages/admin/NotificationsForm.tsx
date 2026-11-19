// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  App,
  Alert,
  Form,
  InputNumber,
} from 'antd';
import Button from '../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client.ts';
import { type NotificationSettings } from '../../api/hooks.ts';
import { useSectionFields } from './useSectionFields.ts';
import { settingsSaveProblem } from './settingsProblems.ts';
import { type SectionProps } from './settingsSection.tsx';

/**
 * How long the installation keeps what it has told people. This was a deployment key alone and is
 * still readable as one: an installation that never opens this tab keeps whatever its environment
 * says, and what is saved here replaces it from the next prune onwards.
 */
export default function NotificationsForm({ settings, onSaved }: SectionProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<NotificationSettings>();
  const [saving, setSaving] = useState(false);

  useSectionFields(form, settings.notifications);

  const save = async (values: NotificationSettings) => {
    setSaving(true);
    try {
      const { data, error } = await api.PUT('/api/v1/admin/settings/notifications', { body: values });
      if (error !== undefined || !data) {
        message.error(settingsSaveProblem(error, t));
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
      <Alert type="info" showIcon title={t('admin.messaging.notificationsIntro')} style={{ marginBottom: 16 }} />
      <Form.Item
        name="retentionDays"
        label={t('admin.messaging.retentionDays')}
        extra={t('admin.messaging.retentionDaysHint')}
      >
        {/* The unit is in the label rather than an addon: antd deprecated addonAfter here, and
            the warning it prints is a console error the browser run refuses. */}
        <InputNumber min={1} max={3650} step={30} />
      </Form.Item>
      <Button type="primary" htmlType="submit" loading={saving}>
        {t('common.save')}
      </Button>
    </Form>
  );
}
