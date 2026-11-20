// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  App,
  Alert,
  Card,
  Flex,
  Form,
  Input,
  InputNumber,
  Select,
  Switch,
} from 'antd';
import Button from '../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client.ts';
import { type SmsSettingsWrite } from '../../api/hooks.ts';
import { useSectionFields } from './useSectionFields.ts';
import { isSettingsConflict, sectionIfMatch, settingsSaveProblem, testSendProblem } from './settingsProblems.ts';
import { type SectionProps, SECRET_KEPT, SaveNotices, StatusBanner } from './settingsSection.tsx';

export default function SmsForm({ settings, onSaved, onStale }: SectionProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<SmsSettingsWrite & { authHeader?: string }>();
  const [saving, setSaving] = useState(false);
  // Notes the last accepted save came back with (a stored secret dropped because its server
  // moved). Kept on screen until the next save rather than flashed, because each asks the
  // administrator to do something.
  const [notices, setNotices] = useState<string[]>([]);
  const [testing, setTesting] = useState(false);
  const [testTo, setTestTo] = useState('');

  useSectionFields(form, settings.sms, 'authHeader');

  const save = async (values: SmsSettingsWrite & { authHeader?: string }) => {
    setSaving(true);
    try {
      const { data, error } = await api.PUT('/api/v1/admin/settings/sms', {
        headers: sectionIfMatch(settings.versions?.sms),
        body: {
          ...values,
          // Extra headers are not editable here yet; passing the stored set back keeps a save
          // from dropping ones an operator set through configuration.
          headers: settings.sms.headers,
          authHeader: !values.authHeader || values.authHeader === SECRET_KEPT ? null : values.authHeader,
        },
      });
      if (error !== undefined || !data) {
        message.error(settingsSaveProblem(error, t));
        if (isSettingsConflict(error)) {
          onStale?.();
        }
        return;
      }
      onSaved(data);
      form.setFieldValue('authHeader', undefined);
      setNotices(data.notices ?? []);
      message.success(t('common.saved'));
    } catch {
      // The request never reached the server (a dropped connection): said, not left unhandled.
      message.error(t('common.saveFailed'));
    } finally {
      setSaving(false);
    }
  };

  const sendTest = async () => {
    setTesting(true);
    try {
      const { data, error } = await api.POST('/api/v1/admin/settings/sms/test', {
        body: { recipient: testTo },
      });
      if (error !== undefined || !data) {
        message.error(testSendProblem(error, t));
        return;
      }
      if (data.sent) {
        message.success(t('admin.messaging.testSent'));
      } else {
        message.error(t('admin.messaging.testFailed', { error: data.error ?? '' }));
      }
    } catch {
      // The request never reached the server: said, not left as an unhandled rejection.
      message.error(t('admin.messaging.testRequestFailed'));
    } finally {
      setTesting(false);
    }
  };

  return (
    <Flex vertical gap={16}>
      <SaveNotices codes={notices} />
      <StatusBanner
        configured={settings.smsConfigured}
        okText={t('admin.messaging.smsWorking')}
        offText={t('admin.messaging.smsInactive')}
      />
      <Alert type="info" showIcon title={t('admin.messaging.smsIntro')} />

      <Form form={form} layout="vertical" onFinish={save} requiredMark={false} style={{ maxWidth: 640 }}>
        <Form.Item name="enabled" label={t('admin.messaging.enabled')} valuePropName="checked">
          <Switch />
        </Form.Item>
        <Form.Item name="url" label={t('admin.messaging.smsUrl')}>
          <Input placeholder="https://gateway.example.org/send" />
        </Form.Item>
        <Form.Item name="method" label={t('admin.messaging.smsMethod')}>
          <Select
            options={[
              { value: 'POST', label: 'POST' },
              { value: 'GET', label: 'GET' },
              { value: 'PUT', label: 'PUT' },
            ]}
          />
        </Form.Item>
        <Form.Item name="contentType" label={t('admin.messaging.smsContentType')}>
          <Select
            options={[
              { value: 'application/x-www-form-urlencoded', label: 'application/x-www-form-urlencoded' },
              { value: 'application/json', label: 'application/json' },
            ]}
          />
        </Form.Item>
        <Form.Item name="bodyTemplate" label={t('admin.messaging.smsBody')} extra={t('admin.messaging.smsBodyHint')}>
          <Input.TextArea rows={4} spellCheck={false} />
        </Form.Item>
        <Form.Item
          name="authHeader"
          label={t('admin.messaging.smsAuthHeader')}
          extra={settings.sms.hasAuthHeader ? t('admin.messaging.secretStored') : t('admin.messaging.smsAuthHint')}
        >
          <Input.Password
            autoComplete="new-password"
            placeholder={settings.sms.hasAuthHeader ? SECRET_KEPT : 'Basic …'}
          />
        </Form.Item>
        <Form.Item name="from" label={t('admin.messaging.smsFrom')}>
          <Input placeholder="+40712345678" />
        </Form.Item>
        <Form.Item name="timeoutSeconds" label={t('admin.messaging.timeout')}>
          <InputNumber min={1} max={120} />
        </Form.Item>
        <Button type="primary" htmlType="submit" loading={saving}>
          {t('common.save')}
        </Button>
      </Form>

      <Card size="small" title={t('admin.messaging.testTitle')}>
        <Flex gap={8} wrap>
          <Input
            style={{ width: 260 }}
            aria-label={t('admin.messaging.testRecipient')}
            placeholder="+40712345678"
            value={testTo}
            onChange={(e) => setTestTo(e.target.value)}
          />
          <Button loading={testing} disabled={!settings.smsConfigured || !testTo} onClick={() => void sendTest()}>
            {t('admin.messaging.sendTest')}
          </Button>
        </Flex>
      </Card>
    </Flex>
  );
}
