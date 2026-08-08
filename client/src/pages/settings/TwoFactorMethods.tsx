// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { KeyOutlined, MailOutlined, MobileOutlined } from '@ant-design/icons';
import { App, Alert, Button, Card, Flex, Input, Popconfirm, QRCode, Tag, Typography } from 'antd';
import List from '../../components/List.tsx';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client.ts';
import { queryKeys, type MfaMethod, type MfaStatus, type TwoFactorMethod } from '../../api/hooks.ts';

interface Props {
  status: MfaStatus;
}

function methodIcon(method: TwoFactorMethod) {
  if (method === 'email') return <MailOutlined />;
  if (method === 'sms') return <MobileOutlined />;
  return <KeyOutlined />;
}

/**
 * The three second factors, each as a row that can be switched on or off.
 *
 * Turning one on always ends in typing a code, whatever the method: an authenticator reads it
 * from the app, and the two delivered ones send it first. Proving the code arrives before the
 * method counts is what stops someone enabling a factor they would then be locked out by.
 */
export default function TwoFactorMethods({ status }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const [enrolling, setEnrolling] = useState<TwoFactorMethod | null>(null);
  const [enrollment, setEnrollment] = useState<{ sharedKey: string; authenticatorUri: string } | null>(null);
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null);

  const refresh = () => void queryClient.invalidateQueries({ queryKey: queryKeys.mfa });

  const reset = () => {
    setEnrolling(null);
    setEnrollment(null);
    setCode('');
  };

  const start = async (method: TwoFactorMethod) => {
    setBusy(true);
    setRecoveryCodes(null);
    try {
      if (method === 'authenticator') {
        const { data, error } = await api.POST('/api/v1/me/mfa/enroll');
        if (error !== undefined || !data) {
          message.error(t('common.saveFailed'));
          return;
        }
        setEnrollment(data);
      } else {
        const { data, error } = await api.POST('/api/v1/me/mfa/methods/{method}/challenge', {
          params: { path: { method } },
        });
        if (error !== undefined || !data) {
          message.error(t('security.challengeFailed'));
          return;
        }
        setEnrollment(null);
        message.success(t('security.codeSent', { destination: data.destination }));
      }
      setEnrolling(method);
      setCode('');
    } finally {
      setBusy(false);
    }
  };

  const enable = async () => {
    if (!enrolling) return;
    setBusy(true);
    try {
      const { data, error } = await api.POST('/api/v1/me/mfa/methods/{method}', {
        params: { path: { method: enrolling } },
        body: { code },
      });
      if (error !== undefined || !data) {
        message.error(t('security.codeInvalid'));
        return;
      }
      // Recovery codes come back only the first time two-factor is switched on; on later
      // methods the array is empty and the set the user already saved still stands.
      if (data.codes.length > 0) {
        setRecoveryCodes([...data.codes]);
      }
      reset();
      refresh();
      message.success(t('security.enabled'));
    } finally {
      setBusy(false);
    }
  };

  const disable = async (method: TwoFactorMethod) => {
    const { error } = await api.DELETE('/api/v1/me/mfa/methods/{method}', {
      params: { path: { method } },
    });
    if (error !== undefined) {
      message.error(t('common.saveFailed'));
      return;
    }
    refresh();
    message.success(t('security.methodDisabled'));
  };

  const setPreferred = async (method: TwoFactorMethod) => {
    const { error } = await api.PUT('/api/v1/me/mfa/preferred', { body: { method } });
    if (error !== undefined) {
      message.error(t('common.saveFailed'));
      return;
    }
    refresh();
  };

  /** Why a method cannot be switched on yet, or null when it can. */
  const blockedReason = (method: MfaMethod): string | null => {
    if (!method.allowed) return t('security.methodNotAllowed');
    if (method.method === 'email' && !method.ready && !method.enabled) return t('security.methodEmailNotReady');
    if (method.method === 'sms' && !method.ready && !method.enabled) return t('security.methodSmsNotReady');
    return null;
  };

  return (
    <Flex vertical gap={12}>
      <List
        dataSource={status.methods}
        renderItem={(item) => {
          const blocked = blockedReason(item);
          const isPreferred = status.preferredMethod === item.method;
          return (
            <List.Item
              actions={[
                item.enabled ? (
                  <Popconfirm
                    key="off"
                    title={t('security.disableMethodConfirm')}
                    onConfirm={() => void disable(item.method)}
                  >
                    <Button size="small" danger>
                      {t('security.turnOff')}
                    </Button>
                  </Popconfirm>
                ) : (
                  <Button
                    key="on"
                    size="small"
                    type="primary"
                    disabled={blocked !== null}
                    loading={busy && enrolling === item.method}
                    onClick={() => void start(item.method)}
                  >
                    {t('security.turnOn')}
                  </Button>
                ),
                item.enabled && !isPreferred ? (
                  <Button key="prefer" size="small" onClick={() => void setPreferred(item.method)}>
                    {t('security.makeDefault')}
                  </Button>
                ) : (
                  <span key="prefer" />
                ),
              ]}
            >
              <List.Item.Meta
                avatar={methodIcon(item.method)}
                title={
                  <Flex gap={8} align="center" wrap>
                    <span>{t(`security.method_${item.method}`)}</span>
                    {item.enabled && <Tag color="green">{t('security.statusOn')}</Tag>}
                    {isPreferred && <Tag color="blue">{t('security.default')}</Tag>}
                    {item.enabled && !item.ready && <Tag color="orange">{t('security.methodUnusable')}</Tag>}
                  </Flex>
                }
                description={
                  blocked ?? (item.destination || t(`security.methodHint_${item.method}`))
                }
              />
            </List.Item>
          );
        }}
      />

      {enrollment && (
        <Card size="small" title={t('security.scanTitle')}>
          <Flex gap={16} wrap align="flex-start">
            {/* The otpauth URI the server issued, as the square every authenticator app expects.
                Rendered client-side from that string — nothing about the shared key leaves the
                browser to become an image somewhere else. */}
            <QRCode
              value={enrollment.authenticatorUri}
              size={168}
              bordered={false}
              aria-label={t('security.scanAlt')}
            />
            <Flex vertical gap={12} style={{ flex: '1 1 240px', minWidth: 240 }}>
              <Alert type="info" showIcon title={t('security.scanHint')} />
              <Typography.Text type="secondary">{t('security.enterKey')}</Typography.Text>
              <Typography.Text code copyable={{ text: enrollment.sharedKey.replace(/ /g, '') }}>
                {enrollment.sharedKey}
              </Typography.Text>
            </Flex>
          </Flex>
        </Card>
      )}

      {enrolling && (
        <Flex gap={8} wrap>
          <Input
            style={{ width: 180 }}
            aria-label={t('security.codePlaceholder')}
            placeholder={t('security.codePlaceholder')}
            value={code}
            maxLength={10}
            onChange={(e) => setCode(e.target.value)}
            onPressEnter={() => void enable()}
          />
          <Button type="primary" loading={busy} disabled={code.length < 6} onClick={() => void enable()}>
            {t('security.confirm')}
          </Button>
          {enrolling !== 'authenticator' && (
            <Button loading={busy} onClick={() => void start(enrolling)}>
              {t('security.resendCode')}
            </Button>
          )}
          <Button type="text" onClick={reset}>
            {t('common.cancel')}
          </Button>
        </Flex>
      )}

      {recoveryCodes && (
        <Alert
          type="warning"
          showIcon
          title={t('security.recoveryTitle')}
          description={
            <Typography.Paragraph copyable={{ text: recoveryCodes.join('\n') }} style={{ margin: 0 }}>
              <pre style={{ margin: 0 }}>{recoveryCodes.join('\n')}</pre>
            </Typography.Paragraph>
          }
        />
      )}
    </Flex>
  );
}
