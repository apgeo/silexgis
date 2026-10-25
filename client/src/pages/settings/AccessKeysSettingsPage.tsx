// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState, type HTMLAttributes } from 'react';
import { Alert, App, Card, Empty, Flex, Form, Input, Popconfirm, Select, Skeleton, Table, Tag, Typography } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import Button from '../../components/Button.tsx';
import { ApiError } from '../../api/client.ts';
import {
  accessKeyIsUsable,
  accessKeyLifetimeChoices,
  useAccessKeys,
  useCreateAccessKey,
  useRevokeAccessKey,
  type AccessKey,
  type AccessKeyCreated,
} from '../../api/hooks.ts';

/** The longest name the server accepts for a key. */
const NAME_MAX_LENGTH = 100;

const STATE_COLOURS: Record<AccessKey['state'], string | undefined> = {
  active: 'green',
  expired: undefined,
  revoked: 'red',
};

/**
 * The account's access keys: what a field device holds instead of a password to upload surveys.
 *
 * A key is a credential, so the card is built around two facts. The server keeps only a hash of
 * it and answers the whole text exactly once, in the creating response — so the text lives in this
 * component's state until the person dismisses it or leaves the page, and is never put into the
 * query cache; the list below carries a short prefix and dates, which is all the server will say
 * afterwards. And a key opens only the survey-upload routes, so a key cannot list or make keys:
 * this page works only for a normal sign-in, which is why a refusal here is worded as that.
 */
export default function AccessKeysSettingsPage() {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const keys = useAccessKeys();
  const create = useCreateAccessKey();
  const revoke = useRevokeAccessKey();
  const [name, setName] = useState('');
  const [days, setDays] = useState<number | undefined>(undefined);
  const [created, setCreated] = useState<AccessKeyCreated | null>(null);

  const choices = keys.data ? accessKeyLifetimeChoices(keys.data) : { days: [], initial: undefined };
  const chosenDays = days ?? choices.initial;
  const when = (value: string | null) => (value ? new Date(value).toLocaleDateString(i18n.language) : null);
  const trimmed = name.trim();

  const refusal = (error: unknown, fallback: string) =>
    error instanceof ApiError && error.status === 403 ? t('accessKeys.signInRequired') : fallback;

  const makeKey = async () => {
    try {
      const answer = await create.mutateAsync({ name: trimmed, expiresInDays: chosenDays ?? null });
      setCreated(answer);
      setName('');
    } catch (error) {
      message.error(refusal(error, t('accessKeys.createFailed')));
    }
  };

  const revokeKey = async (key: AccessKey) => {
    try {
      await revoke.mutateAsync(key.id);
      // The text still on screen may be the one just withdrawn; copying it now would copy a key
      // that no longer opens anything.
      setCreated((current) => (current?.key.id === key.id ? null : current));
      message.success(t('accessKeys.revokedMessage'));
    } catch (error) {
      message.error(refusal(error, t('common.saveFailed')));
    }
  };

  const columns: ColumnsType<AccessKey> = [
    { title: t('accessKeys.columns.name'), dataIndex: 'name', key: 'name' },
    {
      title: t('accessKeys.columns.prefix'),
      dataIndex: 'prefix',
      key: 'prefix',
      render: (prefix: string) => <Typography.Text code>{prefix}…</Typography.Text>,
    },
    { title: t('accessKeys.columns.purpose'), key: 'purpose', render: () => t('accessKeys.purposes.surveyUpload') },
    { title: t('accessKeys.columns.created'), key: 'created', render: (_, key) => when(key.createdAt) },
    {
      title: t('accessKeys.columns.lastUsed'),
      key: 'lastUsed',
      render: (_, key) => when(key.lastUsedAt) ?? t('accessKeys.neverUsed'),
    },
    { title: t('accessKeys.columns.expires'), key: 'expires', render: (_, key) => when(key.expiresAt) },
    {
      title: t('accessKeys.columns.state'),
      key: 'state',
      render: (_, key) => <Tag color={STATE_COLOURS[key.state]}>{t(`accessKeys.states.${key.state}`)}</Tag>,
    },
    {
      title: '',
      key: 'actions',
      render: (_, key) =>
        accessKeyIsUsable(key) ? (
          <Popconfirm
            title={t('accessKeys.revokeConfirm', { name: key.name })}
            okText={t('accessKeys.revoke')}
            onConfirm={() => void revokeKey(key)}
          >
            <Button size="small" danger loading={revoke.isPending && revoke.variables === key.id}>
              {t('accessKeys.revoke')}
            </Button>
          </Popconfirm>
        ) : null,
    },
  ];

  if (keys.isPending) {
    return <Skeleton active paragraph={{ rows: 4 }} />;
  }
  if (keys.isError) {
    return (
      <Alert
        type="error"
        showIcon
        title={refusal(keys.error, t('accessKeys.loadFailed'))}
        action={<Button onClick={() => void keys.refetch()}>{t('common.retry')}</Button>}
      />
    );
  }

  return (
    <Flex vertical gap={16}>
      <Card size="small" title={t('accessKeys.title')}>
        <Flex vertical gap={12}>
          <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
            {t('accessKeys.intro')}
          </Typography.Paragraph>

          {created && (
            <Alert
              type="warning"
              showIcon
              closable
              onClose={() => setCreated(null)}
              title={t('accessKeys.createdTitle', { name: created.key.name })}
              description={
                <Flex vertical gap={4}>
                  <Typography.Text code copyable={{ text: created.secret }} data-testid="access-key-secret">
                    {created.secret}
                  </Typography.Text>
                  <Typography.Text type="secondary">{t('accessKeys.createdWarning')}</Typography.Text>
                </Flex>
              }
            />
          )}

          <Form layout="inline" onFinish={() => void makeKey()} style={{ rowGap: 8 }}>
            <Form.Item label={t('accessKeys.form.name')}>
              <Input
                value={name}
                maxLength={NAME_MAX_LENGTH}
                placeholder={t('accessKeys.form.namePlaceholder')}
                onChange={(event) => setName(event.target.value)}
                style={{ width: 260 }}
              />
            </Form.Item>
            <Form.Item label={t('accessKeys.form.lifetime')}>
              <Select
                value={chosenDays}
                onChange={setDays}
                options={choices.days.map((d) => ({ value: d, label: t('accessKeys.form.days', { count: d }) }))}
                style={{ width: 140 }}
                aria-label={t('accessKeys.form.lifetime')}
              />
            </Form.Item>
            <Form.Item>
              <Button
                type="primary"
                htmlType="submit"
                loading={create.isPending}
                disabled={trimmed === '' || chosenDays === undefined}
              >
                {t('accessKeys.create')}
              </Button>
            </Form.Item>
          </Form>
        </Flex>
      </Card>

      <Table<AccessKey>
        rowKey="id"
        size="small"
        pagination={false}
        scroll={{ x: 'max-content' }}
        columns={columns}
        dataSource={keys.data.keys}
        locale={{ emptyText: <Empty description={t('accessKeys.empty')} image={Empty.PRESENTED_IMAGE_SIMPLE} /> }}
        onRow={(key) => ({ 'data-testid': 'access-key-row', 'data-state': key.state }) as HTMLAttributes<HTMLElement>}
      />
    </Flex>
  );
}
