// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
import { App, Alert, Button, Card, Flex, Form, Select, Spin, Switch } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useNotificationPreferences,
  useUpdateNotificationPreferences,
  type NotificationPreferences,
} from '../../api/hooks.ts';

interface FormValues {
  emailEnabled: boolean;
  digest: NotificationPreferences['digest'];
  categories: Record<string, boolean>;
}

/**
 * What the account is notified about. The server returns every category with its current answer,
 * so this page never has to reason about a half-saved state.
 */
export default function NotificationSettingsPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<FormValues>();
  const { data: prefs, isPending } = useNotificationPreferences();
  const update = useUpdateNotificationPreferences();
  const emailEnabled = Form.useWatch('emailEnabled', form);

  useEffect(() => {
    if (prefs) {
      form.setFieldsValue({
        emailEnabled: prefs.emailEnabled,
        digest: prefs.digest,
        categories: Object.fromEntries(prefs.categories.map((c) => [c.category, c.enabled])),
      });
    }
  }, [prefs, form]);

  if (isPending || !prefs) {
    return <Spin />;
  }

  const onFinish = async (values: FormValues) => {
    try {
      await update.mutateAsync({
        emailEnabled: values.emailEnabled,
        digest: values.digest,
        categories: prefs.categories.map((c) => ({
          category: c.category,
          // A locked category keeps whatever the server says; the switch is disabled anyway.
          enabled: c.locked ? c.enabled : (values.categories[c.category] ?? c.enabled),
        })),
      });
      message.success(t('common.saved'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Form form={form} layout="vertical" onFinish={(v) => void onFinish(v)}>
      <Flex vertical gap={16}>
        {!prefs.deliveryConfigured && (
          // Saying so plainly beats letting someone tune settings that cannot reach them.
          <Alert type="warning" showIcon message={t('settings.notifications.noDelivery')} />
        )}

        <Card size="small" title={t('settings.notifications.delivery')}>
          <Flex vertical gap={0}>
            <Form.Item
              name="emailEnabled"
              label={t('settings.notifications.masterEmail')}
              valuePropName="checked"
            >
              <Switch />
            </Form.Item>
            <Form.Item name="digest" label={t('settings.notifications.digest')} style={{ maxWidth: 260 }}>
              <Select
                disabled={emailEnabled === false}
                options={(['immediate', 'daily'] as const).map((value) => ({
                  value,
                  label: t(`settings.notifications.digestValues.${value}`),
                }))}
              />
            </Form.Item>
          </Flex>
        </Card>

        <Card size="small" title={t('settings.notifications.categories')}>
          <Flex vertical gap={0}>
            {prefs.categories.map((category) => (
              <Form.Item
                key={category.category}
                name={['categories', category.category]}
                label={t(`settings.notifications.events.${category.category}`)}
                extra={
                  category.locked ? t('settings.notifications.alwaysOn') : undefined
                }
                valuePropName="checked"
              >
                <Switch disabled={category.locked || emailEnabled === false} />
              </Form.Item>
            ))}
          </Flex>
        </Card>

        <Flex justify="end">
          <Button type="primary" htmlType="submit" loading={update.isPending}>
            {t('common.save')}
          </Button>
        </Flex>
      </Flex>
    </Form>
  );
}
