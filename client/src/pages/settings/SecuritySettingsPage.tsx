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
import { Alert, App, Card, Flex, Popconfirm, Spin, Tag, Typography } from 'antd';
import Button from '../../components/Button.tsx';
import List from '../../components/List.tsx';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { api } from '../../api/client.ts';
import { queryKeys, useMfaStatus } from '../../api/hooks.ts';
import PasswordChangeCard from './PasswordChangeCard.tsx';
import PhoneNumberCard from './PhoneNumberCard.tsx';
import TwoFactorMethods from './TwoFactorMethods.tsx';

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
export default function SecuritySettingsPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const { data: status } = useMfaStatus();
  const [linked, setLinked] = useState<LinkedLogin[]>([]);
  // Unknown until the account's sign-in methods have been read: a password card shown on a guess
  // would offer a passwordless account a change it cannot make.
  const [hasPassword, setHasPassword] = useState<boolean | null>(null);
  const [loginsState, setLoginsState] = useState<'loading' | 'failed' | 'loaded'>('loading');
  const [available, setAvailable] = useState<LinkedLogin[]>([]);
  const [params, setParams] = useSearchParams();

  const disableAll = async () => {
    try {
      const { error } = await api.POST('/api/v1/me/mfa/disable');
      if (error !== undefined) {
        message.error(t('common.saveFailed'));
        return;
      }
    } catch {
      message.error(t('common.saveFailed'));
      return;
    }
    void queryClient.invalidateQueries({ queryKey: queryKeys.mfa });
    message.success(t('security.disabled'));
  };

  const loadLogins = useCallback(async () => {
    setLoginsState('loading');
    try {
      const [logins, config] = await Promise.all([
        api.GET('/api/v1/me/external-logins'),
        api.GET('/api/v1/auth/config'),
      ]);
      if (!logins.data || !config.data) {
        setLoginsState('failed');
        return;
      }
      setLinked(logins.data.logins);
      setHasPassword(logins.data.hasPassword);
      setAvailable(config.data.providers.map((p) => ({ provider: p.name, displayName: p.displayName })));
      setLoginsState('loaded');
    } catch {
      setLoginsState('failed');
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
    `/api/v1/auth/external/${encodeURIComponent(name)}?mode=link&returnUrl=${encodeURIComponent('/settings/security')}`;

  const unlink = async (provider: string) => {
    let failed: boolean;
    let code: string | undefined;
    try {
      const { error } = await api.DELETE('/api/v1/me/external-logins/{provider}', {
        params: { path: { provider } },
      });
      failed = error !== undefined;
      code = (error as { code?: string } | undefined)?.code;
    } catch {
      failed = true;
    }
    if (failed) {
      // The server keeps the last guard too (the list here can be stale): its refusal says what to
      // do instead, which "could not be unlinked" does not.
      message.error(
        t(code === 'auth.external_last_login' ? 'security.unlinkLastLogin' : 'security.unlinkFailed'),
      );
      return;
    }
    message.success(t('security.unlinked'));
    void loadLogins();
  };

  const linkable = available.filter((a) => !linked.some((l) => l.provider === a.provider));

  return (
    <Flex vertical gap={16}>
      <Typography.Title level={4} style={{ margin: 0 }}>
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
        <Flex vertical gap={12}>
          <Typography.Paragraph style={{ margin: 0 }}>{t('security.intro')}</Typography.Paragraph>
          {status && <TwoFactorMethods status={status} />}
          {status?.enabled && (
            <Flex gap={8} align="center" wrap>
              <Typography.Text>{t('security.recoveryLeft', { count: status.recoveryCodesLeft })}</Typography.Text>
              <Popconfirm title={t('security.disableConfirm')} onConfirm={() => void disableAll()}>
                <Button danger size="small">{t('security.disable')}</Button>
              </Popconfirm>
            </Flex>
          )}
        </Flex>
      </Card>

      {loginsState === 'loading' && hasPassword === null && <Spin data-testid="security-logins-loading" />}

      {loginsState === 'failed' && (
        <Alert
          type="error"
          showIcon
          title={t('security.loginsLoadFailed')}
          action={
            <Button size="small" onClick={() => void loadLogins()}>
              {t('common.retry')}
            </Button>
          }
        />
      )}

      {hasPassword === true && <PasswordChangeCard />}

      <PhoneNumberCard />

      {(linked.length > 0 || linkable.length > 0) && (
        <Card size="small" title={t('security.linkedAccounts')}>
          <Typography.Paragraph type="secondary" style={{ marginTop: 0 }}>
            {t('security.linkedIntro')}
          </Typography.Paragraph>
          {linked.length > 0 && (
            <List
              size="small"
              dataSource={linked}
              renderItem={(item) => {
                // Never let a passwordless account drop its only sign-in method.
                const isLastLogin = hasPassword !== true && linked.length <= 1;
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
    </Flex>
  );
}
