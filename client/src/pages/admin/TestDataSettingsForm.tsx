// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { Alert, App, Form, Switch } from 'antd';
import { useTranslation } from 'react-i18next';
import Button from '../../components/Button.tsx';
import { api } from '../../api/client.ts';
import type { AdminSettings, TestDataSettings } from '../../api/hooks.ts';

/**
 * The administrators' half of the test-data generator's switch.
 *
 * The other half belongs to the deployment and is shown, not edited: where the deployment does not
 * allow the generator, the switch is drawn off and disabled with a sentence saying why, rather than
 * offered as a choice that would change nothing.
 */
export default function TestDataSettingsForm({
  settings,
  onSaved,
}: {
  settings: AdminSettings;
  onSaved: (next: AdminSettings) => void;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<Pick<TestDataSettings, 'enabled'>>();
  const [saving, setSaving] = useState(false);
  const allowed = settings.testData.deploymentAllows;

  useEffect(() => {
    form.setFieldsValue({ enabled: settings.testData.enabled });
  }, [settings, form]);

  const save = async (values: Pick<TestDataSettings, 'enabled'>) => {
    setSaving(true);
    try {
      const { data, error } = await api.PUT('/api/v1/admin/settings/test-data', { body: { enabled: values.enabled } });
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
      <Alert type="info" showIcon title={t('admin.messaging.testDataIntro')} style={{ marginBottom: 16 }} />
      {!allowed && (
        <Alert type="warning" showIcon title={t('admin.messaging.testDataNotAllowed')} style={{ marginBottom: 16 }} />
      )}
      <Form.Item
        name="enabled"
        label={t('admin.messaging.testDataEnabled')}
        valuePropName="checked"
        extra={t('admin.messaging.testDataEnabledHint')}
      >
        <Switch disabled={!allowed} data-testid="admin-test-data-enabled" />
      </Form.Item>
      <Button type="primary" htmlType="submit" loading={saving} disabled={!allowed} data-testid="admin-test-data-save">
        {t('common.save')}
      </Button>
    </Form>
  );
}
