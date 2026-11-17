// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { MobileOutlined } from '@ant-design/icons';
import { App, Alert, Card, Flex, Input, Popconfirm, Tag, Typography } from 'antd';
import Button from '../../components/Button.tsx';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client.ts';
import { queryKeys, useMfaStatus, usePhoneStatus } from '../../api/hooks.ts';

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
  const { data: mfa } = useMfaStatus();
  const [number, setNumber] = useState('');
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  // Whether the person asked to send the code to another number while one is waiting for its code.
  const [replacing, setReplacing] = useState(false);

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.phone });
    void queryClient.invalidateQueries({ queryKey: queryKeys.mfa });
  };

  if (!status) return null;

  // The number is the account's last second factor when texted codes are the only method on.
  // Removing it then switches two-factor sign-in off altogether, which the confirmation has to say
  // in so many words rather than leave to be discovered at the next sign-in.
  const enabledMethods = mfa?.methods.filter((m) => m.enabled).map((m) => m.method) ?? [];
  const lastFactor = enabledMethods.length > 0 && enabledMethods.every((m) => m === 'sms');

  /**
   * The stable code the server refused with, when it wrote one.
   *
   * Worth reading rather than collapsing every failure into one sentence: two of the refusals on
   * these routes are terminal, and answering both with "the code is not valid" invites exactly
   * the wrong reaction — retyping a correct code, or asking for another text — from someone who
   * has no way to learn what actually happened.
   */
  const codeOf = (error: unknown) => (error as { code?: string } | undefined)?.code;

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
      setReplacing(false);
      refresh();
      message.success(t('security.codeSent', { destination: data.destination }));
    } catch {
      message.error(t('security.phoneSendFailed'));
    } finally {
      setBusy(false);
    }
  };

  // Texts the waiting code again to the number already pending; a mistyped number is corrected
  // by sending to another one instead, which replaces the pending number.
  const resend = async () => {
    setBusy(true);
    try {
      const { data, error } = await api.POST('/api/v1/me/phone/resend');
      if (error !== undefined || !data) {
        const problem = codeOf(error);
        message.error(
          problem === 'me.phone_resend_too_soon'
            ? t('security.phoneResendTooSoon')
            : problem === 'me.phone_change_missing'
              ? t('security.phoneNothingPending')
              : t('security.phoneSendFailed'),
        );
        if (problem === 'me.phone_change_missing') {
          refresh();
        }
        return;
      }
      setCode('');
      message.success(t('security.codeSent', { destination: data.destination }));
    } catch {
      message.error(t('security.phoneSendFailed'));
    } finally {
      setBusy(false);
    }
  };

  const confirm = async () => {
    setBusy(true);
    try {
      const { error } = await api.POST('/api/v1/me/phone/confirm', { body: { code: code.trim() } });
      if (error !== undefined) {
        const problem = codeOf(error);
        message.error(
          problem === 'auth.locked_out'
            ? t('security.lockedOut')
            : problem === 'me.phone_taken'
              ? t('security.phoneTaken')
              : t('security.codeInvalid'),
        );
        return;
      }
      setNumber('');
      setCode('');
      refresh();
      message.success(t('security.phoneConfirmed'));
    } catch {
      message.error(t('common.saveFailed'));
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    try {
      const { error } = await api.DELETE('/api/v1/me/phone');
      if (error !== undefined) {
        message.error(t('common.saveFailed'));
        return;
      }
      refresh();
      message.success(t('security.phoneRemoved'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const numberEntry = (
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
        {status.confirmed || status.pendingPhoneNumber ? t('security.phoneChange') : t('security.phoneAdd')}
      </Button>
    </Flex>
  );

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
        {!status.smsConfigured && <Alert type="info" showIcon title={t('security.smsNotConfigured')} />}

        {status.confirmed && status.phoneNumber && (
          <Flex gap={8} align="center" wrap>
            <Typography.Text strong>{status.phoneNumber}</Typography.Text>
            <Popconfirm
              title={lastFactor ? t('security.phoneRemoveLastFactorTitle') : t('security.phoneRemoveConfirm')}
              description={
                lastFactor ? (
                  <Typography.Paragraph style={{ maxWidth: 320, marginBottom: 0 }}>
                    {t('security.phoneRemoveLastFactor')}
                  </Typography.Paragraph>
                ) : undefined
              }
              okText={lastFactor ? t('security.phoneRemoveLastFactorOk') : undefined}
              okButtonProps={{ danger: true }}
              onConfirm={() => void remove()}
            >
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
            {/* A code that never arrived can be asked for again; a mistyped number is replaced by
                sending to the right one. Nothing here clears the pending number on its own: the
                only route that does also removes a confirmed number and its texted sign-in codes. */}
            {status.smsConfigured && (
              <Flex gap={8} wrap>
                <Button size="small" disabled={busy} onClick={() => void resend()}>
                  {t('security.phoneResend')}
                </Button>
                {!replacing && (
                  <Button size="small" type="link" onClick={() => setReplacing(true)}>
                    {t('security.phoneUseOther')}
                  </Button>
                )}
              </Flex>
            )}
            {status.smsConfigured && replacing && numberEntry}
          </Flex>
        ) : (
          status.smsConfigured && numberEntry
        )}

        <Typography.Text type="secondary">{t('security.phoneHint')}</Typography.Text>
      </Flex>
    </Card>
  );
}
