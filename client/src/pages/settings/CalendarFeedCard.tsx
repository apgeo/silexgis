// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { CalendarOutlined } from '@ant-design/icons';
import { App, Alert, Button, Card, Flex, Input, Popconfirm, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCalendarFeeds,
  useMintCalendarFeed,
  useRevokeCalendarFeed,
  type CalendarFeedCreated,
} from '../../api/hooks.ts';

/**
 * The account's subscription addresses for its own calendar: minted here, listed here, withdrawn
 * here. A phone or desktop calendar polls such an address without signing in, so the address is
 * the credential — which shapes everything on this card.
 *
 * The server keeps only a hash of an address and answers the whole of it exactly once, in the
 * mint response. So the one shown here lives in component state until the person dismisses it
 * or leaves the page, and the list below it never carries an address: it is a list of names and
 * dates, with a revoke on each, which is all the server will say about an address once minted.
 *
 * The whole card is absent — not disabled — while the installation offers no feeds, because a
 * button whose only answer is a refusal is a worse screen than no button.
 */
export default function CalendarFeedCard() {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const { data } = useCalendarFeeds();
  const mint = useMintCalendarFeed();
  const revoke = useRevokeCalendarFeed();
  const [label, setLabel] = useState('');
  const [minted, setMinted] = useState<CalendarFeedCreated | null>(null);

  if (!data?.enabled) {
    return null;
  }

  const when = (value: string) => new Date(value).toLocaleDateString(i18n.language);

  const mintOne = async () => {
    try {
      const trimmed = label.trim();
      const created = await mint.mutateAsync(trimmed === '' ? null : trimmed);
      setMinted(created);
      setLabel('');
    } catch {
      message.error(t('settings.account.calendarFeed.mintFailed'));
    }
  };

  const revokeOne = async (id: string) => {
    try {
      await revoke.mutateAsync(id);
      // The address just withdrawn may be the one still on screen from its mint; a person who
      // revokes and then copies would be copying something that no longer answers.
      setMinted((current) => (current?.id === id ? null : current));
      message.success(t('settings.account.calendarFeed.revokedMessage'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Card
      size="small"
      title={
        <span>
          <CalendarOutlined /> {t('settings.account.calendarFeed.heading')}
        </span>
      }
    >
      <Flex vertical gap={12}>
        <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
          {t('settings.account.calendarFeed.intro')}
        </Typography.Paragraph>

        {minted && (
          <Alert
            type="success"
            showIcon
            closable
            onClose={() => setMinted(null)}
            title={t('settings.account.calendarFeed.mintedTitle')}
            description={
              <Flex vertical gap={4}>
                <Typography.Text code copyable={{ text: minted.url }}>
                  {minted.url}
                </Typography.Text>
                <Typography.Text type="secondary">
                  {t('settings.account.calendarFeed.mintedHint')}
                </Typography.Text>
              </Flex>
            }
          />
        )}

        <Flex gap={8} align="center" wrap>
          <Input
            aria-label={t('settings.account.calendarFeed.label')}
            placeholder={t('settings.account.calendarFeed.labelPlaceholder')}
            value={label}
            maxLength={100}
            onChange={(event) => setLabel(event.target.value)}
            style={{ maxWidth: 280 }}
          />
          <Button type="primary" loading={mint.isPending} onClick={() => void mintOne()}>
            {t('settings.account.calendarFeed.mint')}
          </Button>
        </Flex>

        {data.feeds.map((feed) => (
          <Flex key={feed.id} gap={12} align="center" wrap data-testid="calendar-feed-row">
            <Flex vertical flex={1}>
              <Typography.Text strong>
                {feed.label ?? t('settings.account.calendarFeed.unlabelled')}
              </Typography.Text>
              <Typography.Text type="secondary">
                {t('settings.account.calendarFeed.createdOn', { when: when(feed.createdAt) })}
              </Typography.Text>
            </Flex>
            {feed.revokedAt ? (
              <Tag>{t('settings.account.calendarFeed.revoked')}</Tag>
            ) : (
              <Tag color="green">{t('settings.account.calendarFeed.active')}</Tag>
            )}
            {!feed.revokedAt && (
              <Popconfirm
                title={t('settings.account.calendarFeed.revokeConfirm')}
                onConfirm={() => void revokeOne(feed.id)}
              >
                <Button size="small" danger loading={revoke.isPending}>
                  {t('settings.account.calendarFeed.revoke')}
                </Button>
              </Popconfirm>
            )}
          </Flex>
        ))}
      </Flex>
    </Card>
  );
}
