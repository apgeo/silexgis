// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  BellOutlined,
  EyeOutlined,
  IdcardOutlined,
  MailOutlined,
  MobileOutlined,
  SafetyOutlined,
  UserOutlined,
} from '@ant-design/icons';
import type { ReactNode } from 'react';
import { Flex, Menu, Select, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useIsMobile } from '../../hooks/useIsMobile.ts';
import { SETTINGS_SECTIONS, sectionFromPath, type SettingsSection } from './sections.ts';

const SECTION_ICONS: Record<SettingsSection, ReactNode> = {
  profile: <UserOutlined />,
  account: <IdcardOutlined />,
  emails: <MailOutlined />,
  notifications: <BellOutlined />,
  security: <SafetyOutlined />,
  accessibility: <EyeOutlined />,
  sync: <MobileOutlined />,
};

/**
 * The settings shell: a vertical section nav beside the section itself.
 *
 * Each section is a real route rather than a tab, so a section can be linked to, bookmarked and
 * reached with the back button — which is also what lets the address-confirmation link land
 * directly on the emails section.
 */
export default function SettingsLayout() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const isMobile = useIsMobile();
  const section = sectionFromPath(location.pathname);

  const items = SETTINGS_SECTIONS.map((key) => ({
    key,
    icon: SECTION_ICONS[key],
    label: t(`settings.nav.${key}`),
  }));

  return (
    <div style={{ padding: 24 }}>
      <Typography.Title level={3} style={{ marginTop: 0 }}>
        {t('settings.title')}
      </Typography.Title>

      {isMobile ? (
        // A picker rather than a second off-canvas drawer: the shell already puts one on this
        // screen edge, and six items overflow a segmented control.
        <Flex vertical gap={16}>
          <Select
            value={section}
            onChange={(key) => navigate(`/settings/${key}`)}
            options={items.map(({ key, label }) => ({ value: key, label }))}
            style={{ width: '100%' }}
            aria-label={t('settings.title')}
          />
          <Outlet />
        </Flex>
      ) : (
        <Flex gap={24} align="flex-start">
          <Menu
            mode="inline"
            selectedKeys={[section]}
            onClick={({ key }) => navigate(`/settings/${key}`)}
            items={items}
            // No rail border: this menu is inline content, not a sider.
            style={{ width: 220, borderInlineEnd: 0, position: 'sticky', top: 24, alignSelf: 'flex-start' }}
          />
          {/* minWidth 0 lets a wide child scroll inside itself instead of widening the page. */}
          <div style={{ flex: 1, maxWidth: 760, minWidth: 0 }}>
            <Outlet />
          </div>
        </Flex>
      )}
    </div>
  );
}
