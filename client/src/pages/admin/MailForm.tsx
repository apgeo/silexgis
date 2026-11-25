// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  App,
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
import { type MailSettingsWrite } from '../../api/hooks.ts';
import { useSectionFields } from './useSectionFields.ts';
import { isSettingsConflict, sectionIfMatch, settingsSaveProblem, testSendProblem } from './settingsProblems.ts';
import { type SectionProps, SECRET_KEPT, SaveNotices, StatusBanner } from './settingsSection.tsx';

export default function MailForm({
  settings,
  onSaved,
  onStale,
  defaultTestRecipient,
}: SectionProps & { defaultTestRecipient: string }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<MailSettingsWrite & { password?: string }>();
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [testTo, setTestTo] = useState(defaultTestRecipient);
  // Notes the last accepted save came back with (a stored secret dropped because its server
  // moved). Kept on screen until the next save rather than flashed, because each asks the
  // administrator to do something.
  const [notices, setNotices] = useState<string[]>([]);

  // The password is deliberately left blank: an empty field means "leave the stored one alone".
  useSectionFields(form, settings.mail, 'password');

  const save = async (values: MailSettingsWrite & { password?: string }) => {
    setSaving(true);
    try {
      const { data, error } = await api.PUT('/api/v1/admin/settings/mail', {
        headers: sectionIfMatch(settings.versions?.mail),
        body: {
          ...values,
          // Both blank and the placeholder mean "unchanged", so re-saving a form the operator
          // only glanced at cannot silently wipe the password.
          password: !values.password || values.password === SECRET_KEPT ? null : values.password,
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
      form.setFieldValue('password', undefined);
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
      const { data, error } = await api.POST('/api/v1/admin/settings/mail/test', {
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
        configured={settings.mailConfigured}
        okText={t('admin.messaging.mailWorking')}
        offText={t('admin.messaging.mailInactive')}
      />

      <Form form={form} layout="vertical" onFinish={save} requiredMark={false} style={{ maxWidth: 640 }}>
        <Form.Item name="enabled" label={t('admin.messaging.enabled')} valuePropName="checked">
          <Switch />
        </Form.Item>
        <Form.Item name="host" label={t('admin.messaging.host')}>
          <Input placeholder="smtp.example.org" />
        </Form.Item>
        <Form.Item name="port" label={t('admin.messaging.port')}>
          <InputNumber min={1} max={65535} />
        </Form.Item>
        <Form.Item name="security" label={t('admin.messaging.security')}>
          <Select
            options={[
              { value: 'auto', label: t('admin.messaging.securityAuto') },
              { value: 'startTls', label: 'STARTTLS' },
              { value: 'sslOnConnect', label: t('admin.messaging.securitySsl') },
              { value: 'none', label: t('admin.messaging.securityNone') },
            ]}
          />
        </Form.Item>
        <Form.Item name="username" label={t('admin.messaging.username')}>
          <Input autoComplete="off" />
        </Form.Item>
        <Form.Item
          name="password"
          label={t('admin.messaging.password')}
          extra={settings.mail.hasPassword ? t('admin.messaging.secretStored') : t('admin.messaging.secretNone')}
        >
          <Input.Password autoComplete="new-password" placeholder={settings.mail.hasPassword ? SECRET_KEPT : ''} />
        </Form.Item>
        <Form.Item name="fromAddress" label={t('admin.messaging.fromAddress')}>
          <Input placeholder="silexgis@example.org" />
        </Form.Item>
        <Form.Item name="fromName" label={t('admin.messaging.fromName')}>
          <Input />
        </Form.Item>
        <Form.Item name="replyTo" label={t('admin.messaging.replyTo')}>
          <Input />
        </Form.Item>
        <Form.Item name="timeoutSeconds" label={t('admin.messaging.timeout')}>
          <InputNumber min={1} max={300} />
        </Form.Item>
        <Form.Item
          name="acceptInvalidCertificate"
          label={t('admin.messaging.acceptInvalidCertificate')}
          valuePropName="checked"
          extra={t('admin.messaging.acceptInvalidCertificateHint')}
        >
          <Switch />
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
            placeholder="you@example.org"
            value={testTo}
            onChange={(e) => setTestTo(e.target.value)}
          />
          <Button loading={testing} disabled={!settings.mailConfigured || !testTo} onClick={() => void sendTest()}>
            {t('admin.messaging.sendTest')}
          </Button>
        </Flex>
      </Card>
    </Flex>
  );
}
