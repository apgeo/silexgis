// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { MailOutlined, MobileOutlined, SafetyOutlined } from '@ant-design/icons';
import {
  App,
  Alert,
  Button,
  Card,
  Flex,
  Form,
  Input,
  InputNumber,
  Select,
  Switch,
  Tabs,
  Tag,
  Typography,
} from 'antd';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client.ts';
import {
  queryKeys,
  useAdminSettings,
  useMe,
  type AdminSettings,
  type MailSettingsWrite,
  type SecuritySettings,
  type SmsSettingsWrite,
} from '../../api/hooks.ts';

/** Stands in for a secret that is already stored; the real one is never sent to the browser. */
const SECRET_KEPT = '••••••••';

interface SectionProps {
  settings: AdminSettings;
  onSaved: (next: AdminSettings) => void;
}

/**
 * Mail, SMS and sign-in policy for the whole installation.
 *
 * The same values can come from the deployment's environment; anything saved here replaces them.
 * Secrets are write-only — the page is told only whether one is stored, so a saved password can
 * never be read back out of it.
 */
export default function MessagingSettingsPage() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const { data: me } = useMe();
  const isAdmin = me?.roles.includes('Admin') ?? false;
  const { data: settings, isLoading } = useAdminSettings(isAdmin);

  const onSaved = (next: AdminSettings) => queryClient.setQueryData(queryKeys.adminSettings, next);

  if (!isAdmin) {
    return <Alert type="error" showIcon message={t('admin.forbidden')} style={{ margin: 16 }} />;
  }

  return (
    <Flex vertical gap={16} style={{ padding: 16 }}>
      <Typography.Title level={4} style={{ margin: 0 }}>
        <MailOutlined /> {t('admin.messaging.title')}
      </Typography.Title>
      <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
        {t('admin.messaging.intro')}
      </Typography.Paragraph>

      {isLoading || !settings ? null : (
        <Tabs
          items={[
            {
              key: 'mail',
              label: (
                <span>
                  <MailOutlined /> {t('admin.messaging.mailTab')}
                </span>
              ),
              children: <MailForm settings={settings} onSaved={onSaved} defaultTestRecipient={me?.email ?? ''} />,
            },
            {
              key: 'sms',
              label: (
                <span>
                  <MobileOutlined /> {t('admin.messaging.smsTab')}
                </span>
              ),
              children: <SmsForm settings={settings} onSaved={onSaved} />,
            },
            {
              key: 'policy',
              label: (
                <span>
                  <SafetyOutlined /> {t('admin.messaging.policyTab')}
                </span>
              ),
              children: <PolicyForm settings={settings} onSaved={onSaved} />,
            },
          ]}
        />
      )}
    </Flex>
  );
}

function MailForm({
  settings,
  onSaved,
  defaultTestRecipient,
}: SectionProps & { defaultTestRecipient: string }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<MailSettingsWrite & { password?: string }>();
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [testTo, setTestTo] = useState(defaultTestRecipient);

  useEffect(() => {
    // The password is deliberately left blank: an empty field means "leave the stored one alone".
    form.setFieldsValue({ ...settings.mail, password: undefined });
  }, [settings, form]);

  const save = async (values: MailSettingsWrite & { password?: string }) => {
    setSaving(true);
    try {
      const { data, error } = await api.PUT('/api/v1/admin/settings/mail', {
        body: {
          ...values,
          // Both blank and the placeholder mean "unchanged", so re-saving a form the operator
          // only glanced at cannot silently wipe the password.
          password: !values.password || values.password === SECRET_KEPT ? undefined : values.password,
        },
      });
      if (error !== undefined || !data) {
        message.error(t('common.saveFailed'));
        return;
      }
      onSaved(data);
      form.setFieldValue('password', undefined);
      message.success(t('common.saved'));
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
        message.error(t('admin.messaging.testNotConfigured'));
        return;
      }
      if (data.sent) {
        message.success(t('admin.messaging.testSent'));
      } else {
        message.error(t('admin.messaging.testFailed', { error: data.error ?? '' }));
      }
    } finally {
      setTesting(false);
    }
  };

  return (
    <Flex vertical gap={16}>
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

function SmsForm({ settings, onSaved }: SectionProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<SmsSettingsWrite & { authHeader?: string }>();
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [testTo, setTestTo] = useState('');

  useEffect(() => {
    form.setFieldsValue({ ...settings.sms, authHeader: undefined });
  }, [settings, form]);

  const save = async (values: SmsSettingsWrite & { authHeader?: string }) => {
    setSaving(true);
    try {
      const { data, error } = await api.PUT('/api/v1/admin/settings/sms', {
        body: {
          ...values,
          // Extra headers are not editable here yet; passing the stored set back keeps a save
          // from dropping ones an operator set through configuration.
          headers: settings.sms.headers,
          authHeader: !values.authHeader || values.authHeader === SECRET_KEPT ? undefined : values.authHeader,
        },
      });
      if (error !== undefined || !data) {
        message.error(t('common.saveFailed'));
        return;
      }
      onSaved(data);
      form.setFieldValue('authHeader', undefined);
      message.success(t('common.saved'));
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
        message.error(t('admin.messaging.testNotConfigured'));
        return;
      }
      if (data.sent) {
        message.success(t('admin.messaging.testSent'));
      } else {
        message.error(t('admin.messaging.testFailed', { error: data.error ?? '' }));
      }
    } finally {
      setTesting(false);
    }
  };

  return (
    <Flex vertical gap={16}>
      <StatusBanner
        configured={settings.smsConfigured}
        okText={t('admin.messaging.smsWorking')}
        offText={t('admin.messaging.smsInactive')}
      />
      <Alert type="info" showIcon message={t('admin.messaging.smsIntro')} />

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

function PolicyForm({ settings, onSaved }: SectionProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<SecuritySettings>();
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    form.setFieldsValue(settings.security);
  }, [settings, form]);

  const save = async (values: SecuritySettings) => {
    setSaving(true);
    try {
      const { data, error } = await api.PUT('/api/v1/admin/settings/security', { body: values });
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

function StatusBanner({ configured, okText, offText }: { configured: boolean; okText: string; offText: string }) {
  return configured ? <Tag color="green">{okText}</Tag> : <Alert type="warning" showIcon message={offText} />;
}
