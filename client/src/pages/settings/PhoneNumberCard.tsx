// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { MobileOutlined } from '@ant-design/icons';
import { App, Alert, Button, Card, Flex, Input, Popconfirm, Tag, Typography } from 'antd';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client.ts';
import { queryKeys, usePhoneStatus } from '../../api/hooks.ts';

/**
 * The account's phone number, which exists here for one reason: to carry sign-in codes.
 *
 * The number is not live until a texted code comes back, mirroring how the email change works —
 * for a number that is about to become a second factor, a typo that silently took effect would
 * send every future code to a stranger.
 */
export default function PhoneNumberCard() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const { data: status } = usePhoneStatus();
  const [number, setNumber] = useState('');
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.phone });
    void queryClient.invalidateQueries({ queryKey: queryKeys.mfa });
  };

  if (!status) return null;

  const start = async () => {
    setBusy(true);
    try {
      const { data, error } = await api.POST('/api/v1/me/phone/change', {
        body: { phoneNumber: number.replace(/\s/g, '') },
      });
      if (error !== undefined || !data) {
        message.error(t('security.phoneSendFailed'));
        return;
      }
      setCode('');
      refresh();
      message.success(t('security.codeSent', { destination: data.destination }));
    } finally {
      setBusy(false);
    }
  };

  const confirm = async () => {
    setBusy(true);
    try {
      const { error } = await api.POST('/api/v1/me/phone/confirm', { body: { code: code.trim() } });
      if (error !== undefined) {
        message.error(t('security.codeInvalid'));
        return;
      }
      setNumber('');
      setCode('');
      refresh();
      message.success(t('security.phoneConfirmed'));
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    const { error } = await api.DELETE('/api/v1/me/phone');
    if (error !== undefined) {
      message.error(t('common.saveFailed'));
      return;
    }
    refresh();
    message.success(t('security.phoneRemoved'));
  };

  return (
    <Card
      size="small"
      title={
        <span>
          <MobileOutlined /> {t('security.phoneTitle')}
        </span>
      }
      extra={
        status.confirmed ? <Tag color="green">{t('security.phoneVerified')}</Tag> : <Tag>{t('security.statusOff')}</Tag>
      }
    >
      <Flex vertical gap={12}>
        {!status.smsConfigured && <Alert type="info" showIcon message={t('security.smsNotConfigured')} />}

        {status.confirmed && status.phoneNumber && (
          <Flex gap={8} align="center" wrap>
            <Typography.Text strong>{status.phoneNumber}</Typography.Text>
            <Popconfirm title={t('security.phoneRemoveConfirm')} onConfirm={() => void remove()}>
              <Button size="small" danger>
                {t('security.phoneRemove')}
              </Button>
            </Popconfirm>
          </Flex>
        )}

        {status.pendingPhoneNumber ? (
          <Flex vertical gap={8}>
            <Typography.Text type="secondary">
              {t('security.phonePending', { number: status.pendingPhoneNumber })}
            </Typography.Text>
            <Flex gap={8} wrap>
              <Input
                style={{ width: 180 }}
                aria-label={t('security.codePlaceholder')}
                placeholder={t('security.codePlaceholder')}
                value={code}
                maxLength={10}
                onChange={(e) => setCode(e.target.value)}
                onPressEnter={() => void confirm()}
              />
              <Button type="primary" loading={busy} disabled={code.length < 6} onClick={() => void confirm()}>
                {t('security.confirm')}
              </Button>
            </Flex>
          </Flex>
        ) : (
          status.smsConfigured && (
            <Flex gap={8} wrap>
              <Input
                style={{ width: 220 }}
                aria-label={t('security.phoneNumber')}
                placeholder="+40712345678"
                value={number}
                maxLength={32}
                onChange={(e) => setNumber(e.target.value)}
                onPressEnter={() => void start()}
              />
              <Button loading={busy} disabled={number.trim().length < 8} onClick={() => void start()}>
                {status.confirmed ? t('security.phoneChange') : t('security.phoneAdd')}
              </Button>
            </Flex>
          )
        )}

        <Typography.Text type="secondary">{t('security.phoneHint')}</Typography.Text>
      </Flex>
    </Card>
  );
}
