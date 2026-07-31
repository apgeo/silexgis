// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  CarOutlined,
  DashboardOutlined,
  DatabaseOutlined,
  EnvironmentOutlined,
  FileTextOutlined,
  GoldOutlined,
  HistoryOutlined,
  LogoutOutlined,
  MailOutlined,
  SettingOutlined,
  TeamOutlined,
  TableOutlined,
  UserOutlined,
} from '@ant-design/icons';
import { useEffect, useState } from 'react';
import { Avatar, Dropdown, Flex, Layout, Menu, Select, Typography, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useMe } from '../api/hooks.ts';
import { useAuth } from '../auth/auth.tsx';
import { useIsMobile } from '../hooks/useIsMobile.ts';

/** Application shell: slim header + collapsible icon sidebar (off-canvas on phones). */
export default function AppLayout() {
  const { t, i18n } = useTranslation();
  const { user, signOut } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const isMobile = useIsMobile();
  const [navCollapsed, setNavCollapsed] = useState(true);
  const { token } = theme.useToken();

  // Narrowing to phone width closes the rail rather than letting a 200px sider eat a
  // 390px screen. antd's own `breakpoint` prop is deliberately not used here: it drives
  // `collapsed` in both directions, so it would also force the rail *open* on every
  // desktop load, where the collapsed icon rail is the intended default.
  useEffect(() => {
    if (isMobile) {
      setNavCollapsed(true);
    }
  }, [isMobile]);

  const { data: me } = useMe();
  const isAdmin = me?.roles.includes('Admin') ?? false;

  // "settings" is listed so an unmatched path does not fall through to highlighting the map.
  // It matches no menu item, so nothing lights up — settings is not a sidebar destination.
  const sections = [
    'dashboard', 'caves', 'features', 'geodata', 'trip-logs', 'teams',
    'admin/audit', 'admin/messaging', 'admin/message-templates', 'settings',
  ] as const;
  const selectedKey = sections.find((s) => location.pathname.startsWith(`/${s}`)) ?? 'map';

  return (
    <Layout style={{ height: '100%' }}>
      <Layout.Header style={{ display: 'flex', alignItems: 'center', paddingInline: 16 }}>
        <Typography.Title level={4} style={{ color: token.colorTextLightSolid, margin: 0, flex: 1 }}>
          {t('app.name')}
        </Typography.Title>
        <Flex gap={16} align="center">
          <Select
            size="small"
            value={i18n.resolvedLanguage}
            onChange={(lng) => void i18n.changeLanguage(lng)}
            options={[
              { value: 'en', label: 'EN' },
              { value: 'ro', label: 'RO' },
            ]}
            aria-label={t('common.language')}
          />
          <Dropdown
            menu={{
              items: [
                {
                  key: 'settings',
                  icon: <SettingOutlined />,
                  label: t('nav.settings'),
                  // Straight to the first section, so the index redirect never shows.
                  onClick: () => navigate('/settings/profile'),
                },
                {
                  key: 'signout',
                  icon: <LogoutOutlined />,
                  label: t('auth.signOut'),
                  onClick: () => void signOut(),
                },
              ],
            }}
          >
            <Typography.Text style={{ color: token.colorTextLightSolid, cursor: 'pointer' }}>
              <Avatar
                size="small"
                src={me?.avatarUrl ?? undefined}
                icon={<UserOutlined />}
                style={{ marginInlineEnd: 8 }}
              />
              {user?.profile.preferred_username ?? user?.profile.email}
            </Typography.Text>
          </Dropdown>
        </Flex>
      </Layout.Header>
      <Layout>
        <Layout.Sider
          collapsible
          collapsed={navCollapsed}
          onCollapse={setNavCollapsed}
          // Below md there is no room for the icon rail: zero width takes it off-canvas
          // and antd renders its own edge trigger to bring it back.
          collapsedWidth={isMobile ? 0 : undefined}
          theme="light"
          data-testid="app-sider"
        >
          <Menu
            mode="inline"
            selectedKeys={[selectedKey]}
            // "/map" rather than "/": the root dispatches to the dashboard for users who
            // chose it as their landing page, which would make this item unable to reach the map.
            onClick={({ key }) => {
              navigate(key === 'map' ? '/map' : `/${key}`);
              // Off-canvas, the rail covers the content it just navigated to.
              if (isMobile) {
                setNavCollapsed(true);
              }
            }}
            items={[
              { key: 'map', icon: <EnvironmentOutlined />, label: t('nav.map') },
              { key: 'dashboard', icon: <DashboardOutlined />, label: t('nav.dashboard') },
              { key: 'caves', icon: <TableOutlined />, label: t('nav.caves') },
              { key: 'features', icon: <GoldOutlined />, label: t('nav.features') },
              { key: 'geodata', icon: <DatabaseOutlined />, label: t('nav.geodata') },
              { key: 'trip-logs', icon: <CarOutlined />, label: t('nav.trips') },
              { key: 'teams', icon: <TeamOutlined />, label: t('nav.teams') },
              ...(isAdmin
                ? [
                    { key: 'admin/audit', icon: <HistoryOutlined />, label: t('nav.audit') },
                    { key: 'admin/messaging', icon: <MailOutlined />, label: t('nav.messaging') },
                    { key: 'admin/message-templates', icon: <FileTextOutlined />, label: t('nav.templates') },
                  ]
                : []),
            ]}
          />
        </Layout.Sider>
        <Layout.Content style={{ overflow: 'auto' }}>
          <Outlet />
        </Layout.Content>
      </Layout>
    </Layout>
  );
}
