// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useState } from 'react';
import {
  DisconnectOutlined,
  GithubOutlined,
  GoogleOutlined,
  LinkOutlined,
  LoginOutlined,
  SafetyOutlined,
} from '@ant-design/icons';
import { App, Alert, Button, Card, Flex, Input, List, Popconfirm, Tag, Typography } from 'antd';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { api } from '../../api/client.ts';
import { useMfaStatus } from '../../api/hooks.ts';

interface LinkedLogin {
  provider: string;
  displayName: string;
}

function providerIcon(name: string) {
  if (name === 'google') return <GoogleOutlined />;
  if (name === 'github') return <GithubOutlined />;
  return <LoginOutlined />;
}

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
  const [linked, setLinked] = useState<LinkedLogin[]>([]);
  const [hasPassword, setHasPassword] = useState(true);
  const [available, setAvailable] = useState<LinkedLogin[]>([]);
  const [params, setParams] = useSearchParams();

  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['mfa'] });

  const loadLogins = useCallback(async () => {
    const [logins, config] = await Promise.all([
      api.GET('/api/v1/me/external-logins'),
      api.GET('/api/v1/auth/config'),
    ]);
    if (logins.data) {
      setLinked(logins.data.logins);
      setHasPassword(logins.data.hasPassword);
    }
    if (config.data) {
      setAvailable(config.data.providers.map((p) => ({ provider: p.name, displayName: p.displayName })));
    }
  }, []);

  useEffect(() => {
    void loadLogins();
  }, [loadLogins]);

  // A link round-trip returns with ?linked=<name> or ?linkError=<code>; surface it once.
  useEffect(() => {
    if (params.get('linked')) {
      message.success(t('security.linkAdded'));
    } else if (params.get('linkError')) {
      message.error(t('security.linkFailed'));
    }
    if (params.get('linked') || params.get('linkError')) {
      const next = new URLSearchParams(params);
      next.delete('linked');
      next.delete('linkError');
      setParams(next, { replace: true });
    }
  }, [params, setParams, message, t]);

  const linkHref = (name: string) =>
    `/api/v1/auth/external/${encodeURIComponent(name)}?mode=link&returnUrl=${encodeURIComponent('/account/security')}`;

  const unlink = async (provider: string) => {
    const { error } = await api.DELETE('/api/v1/me/external-logins/{provider}', {
      params: { path: { provider } },
    });
    if (error !== undefined) {
      message.error(t('security.unlinkFailed'));
      return;
    }
    message.success(t('security.unlinked'));
    void loadLogins();
  };

  const linkable = available.filter((a) => !linked.some((l) => l.provider === a.provider));

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

      {(linked.length > 0 || linkable.length > 0) && (
        <Card size="small" title={t('security.linkedAccounts')} style={{ marginTop: 16 }}>
          <Typography.Paragraph type="secondary" style={{ marginTop: 0 }}>
            {t('security.linkedIntro')}
          </Typography.Paragraph>
          {linked.length > 0 && (
            <List
              size="small"
              dataSource={linked}
              renderItem={(item) => {
                // Never let a passwordless account drop its only sign-in method.
                const isLastLogin = !hasPassword && linked.length <= 1;
                return (
                  <List.Item
                    actions={[
                      <Popconfirm
                        key="unlink"
                        title={t('security.unlinkConfirm')}
                        onConfirm={() => void unlink(item.provider)}
                        disabled={isLastLogin}
                      >
                        <Button size="small" danger icon={<DisconnectOutlined />} disabled={isLastLogin}>
                          {t('security.unlink')}
                        </Button>
                      </Popconfirm>,
                    ]}
                  >
                    <List.Item.Meta avatar={providerIcon(item.provider)} title={item.displayName} />
                  </List.Item>
                );
              }}
            />
          )}
          {linkable.length > 0 && (
            <Flex vertical gap={8} style={{ marginTop: linked.length > 0 ? 12 : 0 }}>
              {linkable.map((p) => (
                <Button key={p.provider} icon={<LinkOutlined />} block href={linkHref(p.provider)}>
                  {t('security.linkWith', { provider: p.displayName })}
                </Button>
              ))}
            </Flex>
          )}
        </Card>
      )}
    </div>
  );
}
