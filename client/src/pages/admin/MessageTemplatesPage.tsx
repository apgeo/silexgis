// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { FileTextOutlined } from '@ant-design/icons';
import { App, Alert, Button, Collapse, Flex, Form, Input, Popconfirm, Tabs, Tag, Typography } from 'antd';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client.ts';
import { queryKeys, useMe, useMessageTemplates, type MessageTemplate } from '../../api/hooks.ts';

interface EditorProps {
  template: MessageTemplate;
  locale: string;
}

/**
 * Rewriting the wording of the messages the application sends.
 *
 * The list of messages is fixed — one exists only because some code path sends it — but each can
 * be rewritten per language. A message with no rewrite follows the product, so leaving one alone
 * is a real choice rather than a gap, and resetting is a delete rather than a copy of whatever the
 * default happened to be.
 */
export default function MessageTemplatesPage() {
  const { t } = useTranslation();
  const { data: me } = useMe();
  const isAdmin = me?.roles.includes('Admin') ?? false;
  const { data: templates, isLoading } = useMessageTemplates(isAdmin);

  if (!isAdmin) {
    return <Alert type="error" showIcon message={t('admin.forbidden')} style={{ margin: 16 }} />;
  }

  return (
    <Flex vertical gap={16} style={{ padding: 16 }}>
      <Typography.Title level={4} style={{ margin: 0 }}>
        <FileTextOutlined /> {t('admin.templates.title')}
      </Typography.Title>
      <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
        {t('admin.templates.intro')}
      </Typography.Paragraph>

      {isLoading || !templates ? null : (
        <Collapse
          accordion
          items={templates.map((template) => ({
            key: template.key,
            label: (
              <Flex gap={8} align="center" wrap>
                <span>{t(`admin.templates.name_${template.key.replace(/[.-]/g, '_')}`, template.key)}</span>
                <Tag>{template.channel === 'sms' ? t('admin.templates.sms') : t('admin.templates.email')}</Tag>
                {template.locales.some((l) => l.customised) && (
                  <Tag color="blue">{t('admin.templates.customised')}</Tag>
                )}
              </Flex>
            ),
            children: (
              <Flex vertical gap={12}>
                <Typography.Text type="secondary">{template.description}</Typography.Text>
                <Typography.Text type="secondary">
                  {t('admin.templates.placeholders')}:{' '}
                  {template.placeholders.map((name) => (
                    <Typography.Text code key={name}>
                      {`{${name}}`}
                    </Typography.Text>
                  ))}
                </Typography.Text>
                <Tabs
                  items={template.locales.map((locale) => ({
                    key: locale.locale,
                    label: locale.locale.toUpperCase(),
                    children: <TemplateEditor template={template} locale={locale.locale} />,
                  }))}
                />
              </Flex>
            ),
          }))}
        />
      )}
    </Flex>
  );
}

function TemplateEditor({ template, locale }: EditorProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const [form] = Form.useForm<{ subject?: string; body: string }>();
  const [saving, setSaving] = useState(false);

  const current = template.locales.find((l) => l.locale === locale)!;
  const isEmail = template.channel === 'email';

  useEffect(() => {
    form.setFieldsValue({ subject: current.subject ?? undefined, body: current.body });
  }, [current, form]);

  const refresh = () => void queryClient.invalidateQueries({ queryKey: queryKeys.messageTemplates });

  const save = async (values: { subject?: string; body: string }) => {
    setSaving(true);
    try {
      const { error, response } = await api.PUT('/api/v1/admin/message-templates/{key}/{locale}', {
        params: { path: { key: template.key, locale } },
        body: { subject: isEmail ? values.subject : undefined, body: values.body },
      });
      if (error !== undefined) {
        // The server refuses a placeholder the message will never be given a value for; its
        // detail names them, which is far more use than a generic failure.
        const detail = (error as { detail?: string; code?: string } | undefined)?.detail;
        message.error(response.status === 400 && detail ? detail : t('common.saveFailed'));
        return;
      }
      refresh();
      message.success(t('common.saved'));
    } finally {
      setSaving(false);
    }
  };

  const reset = async () => {
    const { error } = await api.DELETE('/api/v1/admin/message-templates/{key}/{locale}', {
      params: { path: { key: template.key, locale } },
    });
    if (error !== undefined) {
      message.error(t('common.saveFailed'));
      return;
    }
    refresh();
    message.success(t('admin.templates.resetDone'));
  };

  return (
    <Form form={form} layout="vertical" onFinish={save} requiredMark={false}>
      {!current.customised && <Alert type="info" showIcon message={t('admin.templates.usingDefault')} />}

      {isEmail && (
        <Form.Item
          name="subject"
          label={t('admin.templates.subject')}
          rules={[{ required: true }, { max: 300 }]}
          style={{ marginTop: 12 }}
        >
          <Input />
        </Form.Item>
      )}

      <Form.Item name="body" label={t('admin.templates.body')} rules={[{ required: true }, { max: 8000 }]}>
        <Input.TextArea rows={isEmail ? 10 : 4} spellCheck={false} />
      </Form.Item>

      <Flex gap={8} wrap>
        <Button type="primary" htmlType="submit" loading={saving}>
          {t('common.save')}
        </Button>
        {current.customised && (
          <Popconfirm title={t('admin.templates.resetConfirm')} onConfirm={() => void reset()}>
            <Button danger>{t('admin.templates.reset')}</Button>
          </Popconfirm>
        )}
      </Flex>
    </Form>
  );
}
