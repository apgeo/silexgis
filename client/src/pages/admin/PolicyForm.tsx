// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  App,
  Form,
  InputNumber,
  Switch,
} from 'antd';
import Button from '../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client.ts';
import { type SecuritySettings } from '../../api/hooks.ts';
import { useSectionFields } from './useSectionFields.ts';
import { settingsSaveProblem } from './settingsProblems.ts';
import { type SectionProps } from './settingsSection.tsx';

export default function PolicyForm({ settings, onSaved }: SectionProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<SecuritySettings>();
  const [saving, setSaving] = useState(false);

  useSectionFields(form, settings.security);

  const save = async (values: SecuritySettings) => {
    setSaving(true);
    try {
      const { data, error } = await api.PUT('/api/v1/admin/settings/security', { body: values });
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
      <Form.Item
        name="sendConfirmationOnRegistration"
        label={t('admin.messaging.sendConfirmation')}
        valuePropName="checked"
      >
        <Switch />
      </Form.Item>
      <Form.Item
        name="requireConfirmedEmail"
        label={t('admin.messaging.requireConfirmed')}
        valuePropName="checked"
        extra={
          settings.mailConfigured
            ? t('admin.messaging.requireConfirmedHint')
            : t('admin.messaging.requireConfirmedInert')
        }
      >
        <Switch />
      </Form.Item>
      <Form.Item
        name="authenticatorTwoFactorEnabled"
        label={t('admin.messaging.allowAuthenticator')}
        valuePropName="checked"
      >
        <Switch />
      </Form.Item>
      <Form.Item
        name="emailTwoFactorEnabled"
        label={t('admin.messaging.allowEmail')}
        valuePropName="checked"
        extra={settings.mailConfigured ? undefined : t('admin.messaging.needsMail')}
      >
        <Switch />
      </Form.Item>
      <Form.Item
        name="smsTwoFactorEnabled"
        label={t('admin.messaging.allowSms')}
        valuePropName="checked"
        extra={settings.smsConfigured ? t('admin.messaging.smsWeakest') : t('admin.messaging.needsSms')}
      >
        <Switch />
      </Form.Item>
      <Form.Item name="twoFactorCodeLifetimeMinutes" label={t('admin.messaging.codeLifetime')}>
        <InputNumber min={1} max={60} />
      </Form.Item>
      <Form.Item name="twoFactorResendIntervalSeconds" label={t('admin.messaging.resendInterval')}>
        <InputNumber min={0} max={600} />
      </Form.Item>
      <Button type="primary" htmlType="submit" loading={saving}>
        {t('common.save')}
      </Button>
    </Form>
  );
}
