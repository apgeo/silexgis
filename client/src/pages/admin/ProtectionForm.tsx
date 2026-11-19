// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  App,
  Alert,
  Form,
  Switch,
} from 'antd';
import Button from '../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client.ts';
import { type ProtectionSettings } from '../../api/hooks.ts';
import { useSectionFields } from './useSectionFields.ts';
import { settingsSaveProblem } from './settingsProblems.ts';
import { type SectionProps } from './settingsSection.tsx';

/**
 * What the installation gives away about a protected cave's surroundings. The documents
 * themselves are never affected by this — only whether a caller who may not see where the cave
 * is gets told which documents point at it. The photo switches decide what travels inside the
 * files handed out; which reader may be told a location stays the server's rule either way.
 */
export default function ProtectionForm({ settings, onSaved }: SectionProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<ProtectionSettings>();
  const [saving, setSaving] = useState(false);

  useSectionFields(form, settings.protection);

  const save = async (values: ProtectionSettings) => {
    setSaving(true);
    try {
      const { data, error } = await api.PUT('/api/v1/admin/settings/protection', { body: values });
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
      <Alert
        type="info"
        showIcon
        title={t('admin.messaging.protectionIntro')}
        style={{ marginBottom: 16 }}
      />
      <Form.Item
        name="revealProtectedAssociations"
        label={t('admin.messaging.revealAssociations')}
        valuePropName="checked"
        extra={t('admin.messaging.revealAssociationsHint')}
      >
        <Switch />
      </Form.Item>
      <Form.Item
        name="calendarFeedEnabled"
        label={t('admin.messaging.calendarFeedEnabled')}
        valuePropName="checked"
        extra={t('admin.messaging.calendarFeedEnabledHint')}
      >
        <Switch />
      </Form.Item>
      <Form.Item
        name="keepLocationInPhotoFiles"
        label={t('admin.messaging.keepLocationInPhotoFiles')}
        valuePropName="checked"
        extra={t('admin.messaging.keepLocationInPhotoFilesHint')}
      >
        <Switch />
      </Form.Item>
      <Form.Item
        name="serveUnstrippableOriginalsToLocationReaders"
        label={t('admin.messaging.serveUnstrippableOriginalsToLocationReaders')}
        valuePropName="checked"
        extra={t('admin.messaging.serveUnstrippableOriginalsToLocationReadersHint')}
      >
        <Switch />
      </Form.Item>
      <Form.Item
        name="describeLocationFreeCopies"
        label={t('admin.messaging.describeLocationFreeCopies')}
        valuePropName="checked"
        extra={t('admin.messaging.describeLocationFreeCopiesHint')}
      >
        <Switch />
      </Form.Item>
      <Button type="primary" htmlType="submit" loading={saving}>
        {t('common.save')}
      </Button>
    </Form>
  );
}
