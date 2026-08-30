// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  LogoutOutlined,
  SettingOutlined,
  UserOutlined,
} from '@ant-design/icons';
import { useEffect, useState } from 'react';
import { Avatar, Dropdown, Flex, Layout, Menu, Select, Typography, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import { useLanguageChoice } from '../i18n/languageChoice.ts';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { hasAccessAction, useCapabilities, useMe, type AccessDomainName } from '../api/hooks.ts';
import { useAuth } from '../auth/auth.tsx';
import NotificationBell from './NotificationBell.tsx';
import { useIsFullAdmin } from './reslinks/permissions.ts';
import { useIsMobile } from '../hooks/useIsMobile.ts';
import { buildNavItems, isNavGroup } from './navItems.tsx';

/** Application shell: slim header + collapsible icon sidebar (off-canvas on phones). */
export default function AppLayout() {
  const { t } = useTranslation();
  const { language, choose } = useLanguageChoice();
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

  // Named from this account's own record rather than from the token. The user-name claim carries
  // the protected label — a generated pseudonym for anyone who never set a display name — which
  // is the right thing to hand a third party and the wrong thing to show somebody about
  // themselves: on a shared machine it leaves no way to tell which account is signed in.
  const { data: me } = useMe();
  // Nav visibility follows the caller's domain-level capabilities. There is no
  // route-level guard on purpose: the server refuses, the nav simply doesn't offer.
  const { data: capabilities } = useCapabilities();
  const can = (domain: AccessDomainName) => hasAccessAction(capabilities?.domains[domain], 'read');
  // The relation vocabulary is not a resource domain — it is installation-wide wording
  // every domain's links read from — so the rank, not a domain right, decides who is
  // offered the page that authors it.
  const isFullAdmin = useIsFullAdmin();

  // "settings" and "notifications" are listed so an unmatched path does not fall through to
  // highlighting the map; neither matches a menu item, so nothing lights up while one is open,
  // which is deliberate — neither is a sidebar destination. Every other entry here is one,
  // including a camp: the list is a destination and a camp's own page stays under it, so opening
  // one keeps the camps item lit.
  const sections = [
    'map3d', 'dashboard', 'work-areas', 'caves', 'features', 'geodata', 'gallery', 'albums', 'cabinets',
    'uploads', 'documents', 'calendar', 'events', 'trip-logs', 'expeditions', 'checklists',
    'caving-groups', 'cavers',
    'admin/audit', 'admin/notification-health', 'admin/messaging', 'admin/message-templates',
    'admin/permission-groups',
    'admin/feature-sets', 'admin/document-types', 'admin/relation-types', 'admin/term-rules',
    'admin/terrain',
    // The three the rail offers under configuration. Missing here, they matched nothing and
    // fell through to the map, so opening trip purposes lit the map item instead.
    'admin/trip-types', 'admin/participant-roles', 'admin/report-templates',
    'settings', 'notifications',
  ] as const;
  const section = sections.find((s) => location.pathname.startsWith(`/${s}`)) ?? 'map';
  // A document's own page is not a sidebar destination of its own — documents are reached
  // through the cabinets they are filed in, so that is what stays lit while one is open.
  const selectedKey = section === 'documents' ? 'cabinets' : section;

  const navItems = buildNavItems(t, {
    can,
    // Gated on write, not read: every account can read the taxonomies, so a read check would
    // offer these pages to everyone. Authoring one decides what every row under it may say.
    taxonomyWrite: hasAccessAction(capabilities?.domains.taxonomies, 'write'),
    // Everyone with something to import has detection rules of their own to keep, so that page
    // is not an administrator's — only promoting a set to what a group or the installation
    // inherits is, and that is refused on the server.
    featureCreate: hasAccessAction(capabilities?.domains.features, 'create'),
    isFullAdmin,
  });


  // The group holding the page being shown, so arriving from a link — a dashboard tile, a
  // notification — opens the rail on the page it landed on rather than on nothing.
  const openGroup = navItems.find(
    (item) => isNavGroup(item) && item.children.some((child) => child.key === selectedKey),
  )?.key;

  // Which groups are expanded. Controlled rather than left to antd's own state, because the
  // group of the page being shown has to open on arrival however the reader got there; antd's
  // `defaultOpenKeys` is read once at mount and would not reopen for a later navigation.
  //
  // Only ever added to. A group the reader collapsed by hand stays collapsed until they open a
  // page inside it, and opening one group does not close the others — accordion behaviour here
  // would keep shutting the group somebody had just opened to compare two of its pages.
  const [openKeys, setOpenKeys] = useState<string[]>(openGroup ? [openGroup] : []);
  useEffect(() => {
    if (openGroup) {
      setOpenKeys((keys) => (keys.includes(openGroup) ? keys : [...keys, openGroup]));
    }
  }, [openGroup]);

  return (
    <Layout style={{ height: '100%' }}>
      <Layout.Header style={{ display: 'flex', alignItems: 'center' }}>
        <Flex align="center" gap={10} style={{ flex: 1 }}>
          {/* On a light chip because the mark is mostly black line work: the passage drawn in it
              is all but invisible against the dark header, and inverting the image would take the
              red and blue of the survey marker with it. Width is left to follow the height so the
              chip cannot squash the drawing if the mark is ever replaced by one a different shape
              — only its own aspect ratio decides how wide it sits. */}
          <img
            src="/silexgis_1_op.png"
            alt=""
            height={26}
            style={{
              display: 'block',
              width: 'auto',
              background: token.colorBgContainer,
              borderRadius: token.borderRadius,
              padding: '2px 5px',
            }}
          />
          <Typography.Title level={5} style={{ color: token.colorTextLightSolid, margin: 0 }}>
            {t('app.name')}
          </Typography.Title>
        </Flex>
        <Flex gap={16} align="center">
          <NotificationBell />
          <Select
            size="small"
            value={language}
            onChange={choose}
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
              {me?.displayName ?? me?.email ?? user?.profile.email}
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
            // Nothing is open while the rail is collapsed, and that is not cosmetic. Collapsed,
            // antd draws an open group as a floating flyout beside the rail — and a flyout the
            // reader never asked for sits over the page, silently swallowing clicks on whatever
            // is beneath it. Auto-opening the current page's group therefore has to stop at the
            // edge of the collapsed rail: the state is kept, so it reappears on expand, but it
            // is not handed to antd while there is nowhere for it to go but on top of the page.
            openKeys={navCollapsed ? [] : openKeys}
            onOpenChange={setOpenKeys}
            // "/map" rather than "/": the root dispatches to the dashboard for users who
            // chose it as their landing page, which would make this item unable to reach the map.
            onClick={({ key }) => {
              navigate(key === 'map' ? '/map' : `/${key}`);
              // Off-canvas, the rail covers the content it just navigated to.
              if (isMobile) {
                setNavCollapsed(true);
              }
            }}
            items={navItems}
          />
        </Layout.Sider>
        <Layout.Content style={{ overflow: 'auto' }}>
          <Outlet />
        </Layout.Content>
      </Layout>
    </Layout>
  );
}
