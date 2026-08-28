// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
import { App, Alert, Button, Card, Flex, Form, Segmented, Spin, Switch, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  NOTIFICATION_CHANNELS,
  useNotificationPreferences,
  useUpdateNotificationPreferences,
  type NotificationCategory,
  type NotificationChannelCell,
  type NotificationChannelName,
  type NotificationChoice,
} from '../../api/hooks.ts';

/** One cell per category and channel, keyed the way the server names them. */
interface FormValues {
  cells: Record<string, Record<string, NotificationChoice>>;
}

/**
 * What the account is notified about, and where: categories down, channels across.
 *
 * The server returns the complete matrix — every category, every channel that category may ever
 * use, with the defaults already merged in — so this page never reasons about a half-saved state
 * and never has to know which combinations are allowed. Three facts travel with each cell and are
 * obeyed rather than re-derived here: whether it may be switched off, whether a daily summary is
 * offerable on it, and whether this installation has the channel configured at all.
 */
export default function NotificationSettingsPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<FormValues>();
  const { data: prefs, isPending } = useNotificationPreferences();
  const update = useUpdateNotificationPreferences();
  const live = Form.useWatch('cells', form);

  useEffect(() => {
    if (prefs) {
      form.setFieldsValue({
        cells: Object.fromEntries(
          prefs.categories.map((category) => [
            String(category.category),
            Object.fromEntries(
              category.channels.map((cell) => [String(cell.channel), cell.choice]),
            ),
          ]),
        ),
      });
    }
  }, [prefs, form]);

  if (isPending || !prefs) {
    return <Spin />;
  }

  // The columns are whatever the categories actually offer, in the vocabulary's own order, so a
  // channel no category may use never becomes an empty column somebody has to interpret.
  const offered = NOTIFICATION_CHANNELS.filter((channel) =>
    prefs.categories.some((category) => category.channels.some((cell) => cell.channel === channel)),
  );
  const configured = new Set(prefs.configuredChannels.map(String));
  const missing = offered.filter((channel) => !configured.has(channel));

  const cellOf = (category: NotificationCategory, channel: NotificationChannelName) =>
    category.channels.find((c) => c.channel === channel);

  // Recomputed from what is on screen rather than from what was last saved: this is the sentence
  // that has to appear at the moment somebody switches the last channel off, not after they save.
  const reachesNobody = (category: NotificationCategory) => {
    const chosen = live?.[String(category.category)];
    return chosen === undefined
      ? category.reachesNobody
      : category.channels.every((cell) => (chosen[String(cell.channel)] ?? cell.choice) === 'off');
  };

  const onFinish = async (values: FormValues) => {
    try {
      await update.mutateAsync({
        categories: prefs.categories.map((category) => ({
          category: category.category,
          channels: category.channels.map((cell) => ({
            channel: cell.channel as NotificationChannelName,
            // A locked cell keeps whatever the server says; its control is disabled anyway, and
            // sending anything else back would be refused.
            choice: cell.locked
              ? cell.choice
              : (values.cells?.[String(category.category)]?.[String(cell.channel)] ?? cell.choice),
          })),
        })),
      });
      message.success(t('common.saved'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const control = (category: NotificationCategory, cell: NotificationChannelCell) => {
    const name = ['cells', String(category.category), String(cell.channel)];
    const label = `${t(`settings.notifications.events.${String(category.category)}`)} — ${t(
      `settings.notifications.channels.${String(cell.channel)}`,
    )}`;

    return (
      <div data-testid={`pref-${String(category.category)}-${String(cell.channel)}`}>
        {cell.canDefer ? (
          <Form.Item name={name} noStyle>
            <Segmented
              size="small"
              disabled={cell.locked}
              aria-label={label}
              options={(['off', 'immediate', 'daily'] as const).map((choice) => ({
                value: choice,
                label: t(`settings.notifications.choices.${choice}`),
              }))}
            />
          </Form.Item>
        ) : (
          <Form.Item
            name={name}
            noStyle
            valuePropName="checked"
            // The cell holds a choice, not a boolean, so the switch is mapped both ways here
            // rather than the matrix being flattened into two shapes the server would disagree on.
            getValueProps={(value: NotificationChoice | undefined) => ({ checked: value !== 'off' })}
            normalize={(checked: boolean): NotificationChoice => (checked ? 'immediate' : 'off')}
          >
            <Switch disabled={cell.locked} aria-label={label} />
          </Form.Item>
        )}
      </div>
    );
  };

  return (
    <Form form={form} layout="vertical" onFinish={(v) => void onFinish(v)}>
      <Flex vertical gap={16}>
        {missing.length > 0 && (
          // Saying so plainly beats letting someone tune a channel that cannot reach them. The
          // choices are still saved, and start working the day the transport is configured.
          <Alert
            type="warning"
            showIcon
            title={t('settings.notifications.notConfiguredHere', {
              channels: missing
                .map((channel) => t(`settings.notifications.channels.${channel}`))
                .join(', '),
            })}
          />
        )}

        <Card size="small" title={t('settings.notifications.categories')}>
          <Table<NotificationCategory>
            dataSource={prefs.categories}
            rowKey={(category) => String(category.category)}
            pagination={false}
            size="small"
            scroll={{ x: 'max-content' }}
            columns={[
              {
                title: t('settings.notifications.category'),
                key: 'category',
                render: (_, category) => (
                  <Flex vertical>
                    <span>{t(`settings.notifications.events.${String(category.category)}`)}</span>
                    {/*
                      Asked of any cell rather than of all of them, because a row can hold both
                      kinds at once: a category with a safety argument behind it is held on
                      wherever it costs nothing, while on a channel billed per message the same
                      category is the account's own choice. Asking whether every cell is locked
                      would drop this sentence from exactly the row whose greyed-out switches it
                      explains, the moment that row gained an optional paid cell.
                    */}
                    {category.channels.some((cell) => cell.locked) && (
                      <Typography.Text type="secondary">
                        {t('settings.notifications.alwaysOn')}
                      </Typography.Text>
                    )}
                    {reachesNobody(category) && (
                      // A legitimate choice, and never one to arrive at without being told.
                      <Typography.Text type="warning">
                        {t('settings.notifications.reachesNobody')}
                      </Typography.Text>
                    )}
                  </Flex>
                ),
              },
              ...offered.map((channel) => ({
                title: (
                  <Flex gap={6} align="center">
                    {t(`settings.notifications.channels.${channel}`)}
                    {!configured.has(channel) && (
                      <Tag color="warning">{t('settings.notifications.notConfigured')}</Tag>
                    )}
                  </Flex>
                ),
                key: channel,
                render: (_: unknown, category: NotificationCategory) => {
                  const cell = cellOf(category, channel);
                  return cell ? (
                    control(category, cell)
                  ) : (
                    <Typography.Text type="secondary">
                      {t('settings.notifications.notOffered')}
                    </Typography.Text>
                  );
                },
              })),
            ]}
          />
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
