// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import {
  BellOutlined,
  CameraOutlined,
  EnvironmentOutlined,
  ImportOutlined,
  MailOutlined,
  MobileOutlined,
  SafetyOutlined,
} from '@ant-design/icons';
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
  hasAccessAction,
  queryKeys,
  useAdminSettings,
  useCapabilities,
  useMe,
  usePhotoLibraries,
  type AdminSettings,
  type AnnouncementSettings,
  type ImportSettings,
  type MailSettingsWrite,
  type NotificationSettings,
  type PhotoLibrarySuspension,
  type ProtectionSettings,
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
  const { data: capabilities } = useCapabilities();
  const canRead = hasAccessAction(capabilities?.domains.settings, 'read');
  const { data: settings, isLoading } = useAdminSettings(canRead);

  const onSaved = (next: AdminSettings) => queryClient.setQueryData(queryKeys.adminSettings, next);

  // While capabilities load, render nothing rather than flashing a refusal at people
  // who do hold the right; the server enforces regardless.
  if (!capabilities) {
    return null;
  }
  if (!canRead) {
    return <Alert type="error" showIcon title={t('admin.forbidden')} style={{ margin: 16 }} />;
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
            {
              key: 'protection',
              label: (
                <span>
                  <EnvironmentOutlined /> {t('admin.messaging.protectionTab')}
                </span>
              ),
              children: <ProtectionForm settings={settings} onSaved={onSaved} />,
            },
            {
              key: 'import',
              label: (
                <span>
                  <ImportOutlined /> {t('admin.messaging.importTab')}
                </span>
              ),
              children: <ImportForm settings={settings} onSaved={onSaved} />,
            },
            {
              key: 'notifications',
              label: (
                <span>
                  <BellOutlined /> {t('admin.messaging.notificationsTab')}
                </span>
              ),
              children: (
                <Flex vertical gap={32}>
                  <NotificationsForm settings={settings} onSaved={onSaved} />
                  <AnnouncementsForm settings={settings} onSaved={onSaved} />
                </Flex>
              ),
            },
            {
              key: 'photo-libraries',
              label: (
                <span>
                  <CameraOutlined /> {t('admin.messaging.photoLibrariesTab')}
                </span>
              ),
              children: <PhotoLibrariesForm settings={settings} onSaved={onSaved} />,
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

/**
 * What the installation gives away about a protected cave's surroundings. The documents
 * themselves are never affected by this — only whether a caller who may not see where the cave
 * is gets told which documents point at it.
 */
function ProtectionForm({ settings, onSaved }: SectionProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<ProtectionSettings>();
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    form.setFieldsValue(settings.protection);
  }, [settings, form]);

  const save = async (values: ProtectionSettings) => {
    setSaving(true);
    try {
      const { data, error } = await api.PUT('/api/v1/admin/settings/protection', { body: values });
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
      <Button type="primary" htmlType="submit" loading={saving}>
        {t('common.save')}
      </Button>
    </Form>
  );
}

/**
 * How much this installation trusts a vector file to become registry objects on its own.
 *
 * Off is the shipped answer and the one to keep unless the club's rules have been tuned and
 * proved: rules are a guess about somebody's naming habits, and the difference between a bad
 * review and a bad auto-import is four hundred objects in the registry.
 */
function ImportForm({ settings, onSaved }: SectionProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<ImportSettings>();
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    form.setFieldsValue(settings.import);
  }, [settings, form]);

  const save = async (values: ImportSettings) => {
    setSaving(true);
    try {
      const { data, error } = await api.PUT('/api/v1/admin/settings/import', { body: values });
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
      <Alert type="info" showIcon title={t('admin.messaging.importIntro')} style={{ marginBottom: 16 }} />
      <Form.Item
        name="allowCreateWithoutReview"
        label={t('admin.messaging.allowWithoutReview')}
        valuePropName="checked"
        extra={t('admin.messaging.allowWithoutReviewHint')}
      >
        <Switch />
      </Form.Item>
      <Form.Item
        name="duplicateRadiusMeters"
        label={t('admin.messaging.duplicateRadius')}
        extra={t('admin.messaging.duplicateRadiusHint')}
      >
        <InputNumber min={0} max={5000} step={5} addonAfter="m" />
      </Form.Item>
      <Form.Item
        name="duplicateNameSimilarity"
        label={t('admin.messaging.duplicateSimilarity')}
        extra={t('admin.messaging.duplicateSimilarityHint')}
      >
        <InputNumber min={0} max={1} step={0.05} />
      </Form.Item>
      <Button type="primary" htmlType="submit" loading={saving}>
        {t('common.save')}
      </Button>
    </Form>
  );
}

/**
 * How long the installation keeps what it has told people. This was a deployment key alone and is
 * still readable as one: an installation that never opens this tab keeps whatever its environment
 * says, and what is saved here replaces it from the next prune onwards.
 */
function NotificationsForm({ settings, onSaved }: SectionProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<NotificationSettings>();
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    form.setFieldsValue(settings.notifications);
  }, [settings, form]);

  const save = async (values: NotificationSettings) => {
    setSaving(true);
    try {
      const { data, error } = await api.PUT('/api/v1/admin/settings/notifications', { body: values });
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

/**
 * What an announcement to a whole caving group may cost this installation.
 *
 * Its own form rather than two more fields on the retention one, because saving a section
 * replaces the whole stored document: a form posting only the retention window would reset a
 * switch about spending money back to its default, and a switch that turns itself off when
 * somebody edits an unrelated field is worse than no switch.
 */
function AnnouncementsForm({ settings, onSaved }: SectionProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<AnnouncementSettings>();
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    form.setFieldsValue(settings.announcements);
  }, [settings, form]);

  const save = async (values: AnnouncementSettings) => {
    setSaving(true);
    try {
      const { data, error } = await api.PUT('/api/v1/admin/settings/announcements', { body: values });
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

/**
 * Which stored switch belongs to which library.
 *
 * Deliberately not claimed to be exhaustive. The contract names a library with a free string
 * rather than a closed set, so a product added on the server arrives here as a name this build has
 * no switch for — and that is answered by showing the library without a control rather than by
 * posting a body the server would ignore. A switch that silently saves nothing is worse than a
 * library listed without one, because only the second is visible.
 */
const SUSPENSION_FIELD: Partial<Record<string, keyof PhotoLibrarySuspension>> = {
  immich: 'immichSuspended',
  photoprism: 'photoPrismSuspended',
};

/**
 * What it takes to stop the library itself, which is the deployment rather than this page.
 *
 * The commands are the ones the shipped overlays define, and they are printed rather than run:
 * this application is deliberately given no way to start or stop the containers beside it, and a
 * web application that could would be a different kind of program than this one.
 */
const STOP_COMMAND: Partial<Record<string, string>> = {
  immich: 'docker compose stop immich-server',
  photoprism: 'docker compose stop photoprism',
};

/**
 * The one-way brake on each neighbouring photo library.
 *
 * <p>
 * It stops this installation using a library it already has, and starts it again. It cannot
 * connect one — which libraries exist is settled by the deployment that gave them addresses and
 * credentials — so a product nobody connected gets a sentence here and no switch. That asymmetry
 * is the design rather than an unfinished screen: a control that could switch a library "on" would
 * be claiming a container nobody started, and the state it produced is one no operator could act
 * on.
 * </p>
 * <p>
 * Which libraries this installation has is read from the photo-library status route rather than
 * held as a second list here. That route is already the one answer to "what has this installation
 * been given", and a screen with its own idea of it would go on offering a switch for a library
 * somebody removed from the deployment.
 * </p>
 */
export function PhotoLibrariesForm({ settings, onSaved }: SectionProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  // Health polling off: this page is about a stored decision, not about whether a container is up,
  // and a settings tab left open should not put a request a minute at a neighbour.
  const { data: status } = usePhotoLibraries({ watchingHealth: false });
  const [saving, setSaving] = useState<string | null>(null);

  const save = async (
    source: string,
    field: keyof PhotoLibrarySuspension,
    suspended: boolean,
  ) => {
    setSaving(source);
    try {
      // Read the stored decisions again, immediately before changing one of them. Both switches
      // have to travel, because saving replaces the whole stored section and a request carrying one
      // library's value alone would release the other library's brake as a side effect of touching
      // this one — but the copy this tab loaded may be minutes old, and posting it back would undo
      // a brake somebody else pulled meanwhile. Re-reading does not make the write atomic; it
      // shortens the window from "however long this tab has been open" to one round trip.
      const { data: current } = await api.GET('/api/v1/admin/settings');
      const body: PhotoLibrarySuspension = {
        ...(current?.photoLibraries ?? settings.photoLibraries),
        [field]: suspended,
      };
      const { data, error } = await api.PUT('/api/v1/admin/settings/photo-libraries', { body });
      if (error !== undefined || !data) {
        message.error(t('common.saveFailed'));
        return;
      }
      onSaved(data);
      // Every surface that offers a library reads the status route, so it has to be re-asked here:
      // a map still drawing an overlay for a library this page has just stopped is the one thing
      // an operator watching the screen would read as the switch not working.
      await queryClient.invalidateQueries({ queryKey: queryKeys.photoLibraryStatus });
      message.success(t('common.saved'));
    } finally {
      setSaving(null);
    }
  };

  const connected = status?.providers ?? [];
  const absent = status?.unconfigured ?? [];

  return (
    <Flex vertical gap={16} style={{ maxWidth: 640 }}>
      <Alert type="info" showIcon title={t('admin.messaging.photoLibrariesIntro')} />

      {connected.length === 0 && absent.length === 0 && (
        <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
          {t('libraryPhotos.admin.none')}
        </Typography.Paragraph>
      )}

      {connected.map((library) => {
        const field = SUSPENSION_FIELD[library.source];
        const command = STOP_COMMAND[library.source];
        return (
          <Card key={library.source} size="small" title={library.name}>
            {field !== undefined && (
              <Form layout="vertical" requiredMark={false}>
                <Form.Item
                  label={t('libraryPhotos.admin.suspend')}
                  extra={t('libraryPhotos.admin.suspendHint')}
                  style={{ marginBottom: 8 }}
                >
                  <Switch
                    // Shown from the same answer the save is built from, so the switch can never
                    // render one position and post the other. The status route reports the same
                    // decision, but through a separately cached query with a slower refresh, and a
                    // control that displays one source and writes another eventually shows a state
                    // it would not post.
                    checked={settings.photoLibraries[field]}
                    loading={saving === library.source}
                    onChange={(checked) => void save(library.source, field, checked)}
                    data-testid={`photo-library-suspend-${library.source}`}
                  />
                </Form.Item>
              </Form>
            )}
            {/* The sentence that otherwise gets left out, and the one that matters most: the switch
                will be read as "the photographs are private again", which is false in every
                particular. A stopped-but-running library still indexes, still scans, still holds
                its own accounts and still serves anyone who signs into it directly. */}
            <Typography.Paragraph type="secondary" style={{ marginBottom: 4 }}>
              {t('libraryPhotos.admin.stillHoldsEverything')}
            </Typography.Paragraph>
            {command !== undefined && (
              <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
                {t('libraryPhotos.admin.stopCommand', { command })}
              </Typography.Paragraph>
            )}
          </Card>
        );
      })}

      {/* Named, and given no switch. This is where the one-way shape is visible on the screen: a
          library the deployment never supplied is something this page can describe and not
          something it can create. */}
      {absent.map((library) => (
        <Typography.Paragraph key={library.source} type="secondary" style={{ margin: 0 }}>
          {t('libraryPhotos.admin.cannotConnect', { library: library.name })}
        </Typography.Paragraph>
      ))}
    </Flex>
  );
}

function StatusBanner({ configured, okText, offText }: { configured: boolean; okText: string; offText: string }) {
  return configured ? <Tag color="green">{okText}</Tag> : <Alert type="warning" showIcon title={offText} />;
}
