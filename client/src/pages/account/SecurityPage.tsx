// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { SafetyOutlined } from '@ant-design/icons';
import { App, Alert, Button, Card, Flex, Input, Popconfirm, Tag, Typography } from 'antd';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client.ts';
import { useMfaStatus } from '../../api/hooks.ts';

/**
 * Account security: TOTP two-factor management. Enrollment shows the shared key and the
 * otpauth URI for authenticator apps (manual entry; QR rendering is a later nicety).
 * Recovery codes appear exactly once — the user must store them.
 */
export default function SecurityPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const { data: status } = useMfaStatus();
  const [enrollment, setEnrollment] = useState<{ sharedKey: string; authenticatorUri: string } | null>(null);
  const [code, setCode] = useState('');
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null);
  const [busy, setBusy] = useState(false);

  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['mfa'] });

  const enroll = async () => {
    setBusy(true);
    try {
      const { data, error } = await api.POST('/api/v1/me/mfa/enroll');
      if (error !== undefined || !data) {
        throw new Error('enroll failed');
      }
      setEnrollment(data);
      setRecoveryCodes(null);
    } catch {
      message.error(t('common.saveFailed'));
    } finally {
      setBusy(false);
    }
  };

  const confirm = async () => {
    setBusy(true);
    try {
      const { data, error } = await api.POST('/api/v1/me/mfa/confirm', { body: { code } });
      if (error !== undefined || !data) {
        message.error(t('security.codeInvalid'));
        return;
      }
      setRecoveryCodes([...data.codes]);
      setEnrollment(null);
      setCode('');
      refresh();
      message.success(t('security.enabled'));
    } finally {
      setBusy(false);
    }
  };

  const disable = async () => {
    const { error } = await api.POST('/api/v1/me/mfa/disable');
    if (error !== undefined) {
      message.error(t('common.saveFailed'));
      return;
    }
    setRecoveryCodes(null);
    refresh();
    message.success(t('security.disabled'));
  };

  return (
    <div style={{ padding: 24, maxWidth: 640 }}>
      <Typography.Title level={3} style={{ marginTop: 0 }}>
        <SafetyOutlined /> {t('security.title')}
      </Typography.Title>

      <Card
        size="small"
        title={t('security.mfa')}
        extra={
          status?.enabled ? (
            <Tag color="green">{t('security.statusOn')}</Tag>
          ) : (
            <Tag>{t('security.statusOff')}</Tag>
          )
        }
      >
        {!status?.enabled && !enrollment && (
          <Flex vertical gap={12}>
            <Typography.Paragraph style={{ margin: 0 }}>{t('security.intro')}</Typography.Paragraph>
            <Button type="primary" loading={busy} onClick={() => void enroll()}>
              {t('security.enable')}
            </Button>
          </Flex>
        )}

        {enrollment && (
          <Flex vertical gap={12}>
            <Alert type="info" showIcon message={t('security.enterKey')} />
            <Typography.Text code copyable={{ text: enrollment.sharedKey.replace(/ /g, '') }}>
              {enrollment.sharedKey}
            </Typography.Text>
            <Typography.Text type="secondary" copyable={{ text: enrollment.authenticatorUri }} ellipsis>
              {enrollment.authenticatorUri}
            </Typography.Text>
            <Flex gap={8}>
              <Input
                style={{ width: 180 }}
                placeholder={t('security.codePlaceholder')}
                value={code}
                maxLength={6}
                onChange={(e) => setCode(e.target.value)}
                onPressEnter={() => void confirm()}
              />
              <Button type="primary" loading={busy} disabled={code.length < 6} onClick={() => void confirm()}>
                {t('security.confirm')}
              </Button>
            </Flex>
          </Flex>
        )}

        {status?.enabled && (
          <Flex vertical gap={12}>
            <Typography.Text>
              {t('security.recoveryLeft', { count: status.recoveryCodesLeft })}
            </Typography.Text>
            <Flex gap={8}>
              <Popconfirm title={t('security.disableConfirm')} onConfirm={() => void disable()}>
                <Button danger>{t('security.disable')}</Button>
              </Popconfirm>
            </Flex>
          </Flex>
        )}

        {recoveryCodes && (
          <Alert
            style={{ marginTop: 12 }}
            type="warning"
            showIcon
            message={t('security.recoveryTitle')}
            description={
              <Typography.Paragraph copyable={{ text: recoveryCodes.join('\n') }} style={{ margin: 0 }}>
                <pre style={{ margin: 0 }}>{recoveryCodes.join('\n')}</pre>
              </Typography.Paragraph>
            }
          />
        )}
      </Card>
    </div>
  );
}
