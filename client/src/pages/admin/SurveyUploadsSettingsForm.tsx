// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { Alert, App, Form, Switch, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import Button from '../../components/Button.tsx';
import { api } from '../../api/client.ts';
import type { AdminSettings, SurveyUploadSettings } from '../../api/hooks.ts';

/**
 * What the installation decides about surveys sent from a field device.
 *
 * One switch today: whether a cave an upload itself created takes that upload's first model as
 * its shape and current model. The whole section is sent on save, because the server replaces it
 * whole. How many versions keep their results, and the size the pages warn at, are deployment
 * settings read at start; the form names them rather than pretending to edit them.
 */
export default function SurveyUploadsSettingsForm({
  settings,
  onSaved,
}: {
  settings: AdminSettings;
  onSaved: (next: AdminSettings) => void;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<SurveyUploadSettings>();
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    form.setFieldsValue(settings.surveyUploads);
  }, [settings, form]);

  const save = async (values: SurveyUploadSettings) => {
    setSaving(true);
    try {
      const { data, error } = await api.PUT('/api/v1/admin/settings/survey-uploads', {
        body: { ...settings.surveyUploads, ...values },
      });
      if (error !== undefined || !data) {
        message.error(t('common.saveFailed'));
        return;
      }
      onSaved(data);
      message.success(t('common.saved'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Form form={form} layout="vertical" onFinish={save} requiredMark={false} style={{ maxWidth: 640 }}>
      <Alert type="info" showIcon title={t('admin.messaging.surveyUploadsIntro')} style={{ marginBottom: 16 }} />
      <Form.Item
        name="firstModelOfCreatedCave"
        label={t('admin.messaging.firstModelOfCreatedCave')}
        valuePropName="checked"
        extra={t('admin.messaging.firstModelOfCreatedCaveHint')}
      >
        <Switch data-testid="admin-survey-uploads-first-model" />
      </Form.Item>
      <Typography.Paragraph type="secondary">{t('admin.messaging.surveyUploadsEnvironmentNote')}</Typography.Paragraph>
      <Button type="primary" htmlType="submit" loading={saving} data-testid="admin-survey-uploads-save">
        {t('common.save')}
      </Button>
    </Form>
  );
}
