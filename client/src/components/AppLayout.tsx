// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  CarOutlined,
  DatabaseOutlined,
  EnvironmentOutlined,
  GoldOutlined,
  HistoryOutlined,
  LogoutOutlined,
  TableOutlined,
  UserOutlined,
} from '@ant-design/icons';
import { Dropdown, Flex, Layout, Menu, Select, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useMe } from '../api/hooks.ts';
import { useAuth } from '../auth/auth.tsx';

/** Application shell: slim header + collapsible icon sidebar. */
export default function AppLayout() {
  const { t, i18n } = useTranslation();
  const { user, signOut } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  const { data: me } = useMe();
  const isAdmin = me?.roles.includes('Admin') ?? false;

  const sections = ['caves', 'features', 'geodata', 'trip-logs', 'admin/audit'] as const;
  const selectedKey = sections.find((s) => location.pathname.startsWith(`/${s}`)) ?? 'map';

  return (
    <Layout style={{ height: '100%' }}>
      <Layout.Header style={{ display: 'flex', alignItems: 'center', paddingInline: 16 }}>
        <Typography.Title level={4} style={{ color: '#fff', margin: 0, flex: 1 }}>
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
                  key: 'signout',
                  icon: <LogoutOutlined />,
                  label: t('auth.signOut'),
                  onClick: () => void signOut(),
                },
              ],
            }}
          >
            <Typography.Text style={{ color: '#fff', cursor: 'pointer' }}>
              <UserOutlined /> {user?.profile.preferred_username ?? user?.profile.email}
            </Typography.Text>
          </Dropdown>
        </Flex>
      </Layout.Header>
      <Layout>
        <Layout.Sider collapsible defaultCollapsed theme="light">
          <Menu
            mode="inline"
            selectedKeys={[selectedKey]}
            onClick={({ key }) => navigate(key === 'map' ? '/' : `/${key}`)}
            items={[
              { key: 'map', icon: <EnvironmentOutlined />, label: t('nav.map') },
              { key: 'caves', icon: <TableOutlined />, label: t('nav.caves') },
              { key: 'features', icon: <GoldOutlined />, label: t('nav.features') },
              { key: 'geodata', icon: <DatabaseOutlined />, label: t('nav.geodata') },
              { key: 'trip-logs', icon: <CarOutlined />, label: t('nav.trips') },
              ...(isAdmin
                ? [{ key: 'admin/audit', icon: <HistoryOutlined />, label: t('nav.audit') }]
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
