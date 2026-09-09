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
import {
  hasAccessAction,
  useCapabilities,
  useMe,
  usePhotoLibraries,
  type AccessDomainName,
} from '../api/hooks.ts';
import { useAuth } from '../auth/auth.tsx';
import NotificationBell from './NotificationBell.tsx';
import { useIsFullAdmin } from './reslinks/permissions.ts';
import { useIsMobile } from '../hooks/useIsMobile.ts';
import { buildNavItems, isNavGroup } from './navItems.tsx';
import { sectionFor } from './navSections.ts';

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
  // Asked once, and not watched. The rail wants one thing from this answer — whether there is a
  // neighbouring library this account may look through — and that cannot change without the server
  // being restarted. Whether a library is up right now changes on its own and is worth watching,
  // but only on a surface showing it: this component is mounted on every page for the whole of a
  // session, so a timer here would be a request twice a minute for every signed-in account, for
  // ever, to decide whether to draw one rail entry.
  const { data: photoLibraries } = usePhotoLibraries({ watchingHealth: false });

  const section = sectionFor(location.pathname);
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
    // Reading a spreadsheet into trips ends in creating them, and the server refuses the whole
    // screen — the preview included — to anyone who may not. A read check here would offer an
    // afternoon's review to somebody whose first request is turned down.
    tripLogCreate: hasAccessAction(capabilities?.domains.tripLogs, 'create'),
    // Two facts, both from the server: whether this account may reach the neighbouring photo
    // libraries at all, and whether this installation has been given one. Neither is a right of
    // this application's own, and an installation that runs none of these products has nothing
    // behind the page — so the rail offers it only when there is something there.
    photoLibrary: (photoLibraries?.mayRead ?? false) && (photoLibraries?.providers.length ?? 0) > 0,
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
  // Only ever added to by the arrival below. A group the reader collapsed by hand stays
  // collapsed until they open a page inside it, and opening one group does not close the
  // others — accordion behaviour here would keep shutting the group somebody had just opened
  // to compare two of its pages.
  //
  // Starts empty even when the landing page sits in a group, because the rail starts collapsed;
  // see the two effects below for why an open group and a collapsed rail must not coincide.
  const [openKeys, setOpenKeys] = useState<string[]>([]);

  // Arriving on a page opens the group holding it — but only once there is room to draw it
  // inline. Collapsed, antd renders an open group as a floating flyout beside the rail, and one
  // the reader never asked for sits over the page and swallows clicks on whatever is beneath
  // it. So the arrival waits for the rail rather than being suppressed at the point of use:
  // expand it and the group of the page being shown is open, which is what it is for.
  useEffect(() => {
    if (openGroup && !navCollapsed) {
      setOpenKeys((keys) => (keys.includes(openGroup) ? keys : [...keys, openGroup]));
    }
  }, [openGroup, navCollapsed]);

  // Narrowing the rail closes what was open, for that same reason from the other direction: a
  // group left open inline becomes a flyout over the page the moment the rail collapses under
  // it. Only on the transition — a group the reader opens *while* collapsed is their own
  // deliberate act and is theirs to keep, which is the whole difference between this and
  // refusing to hand antd any open key at all while the rail is narrow.
  useEffect(() => {
    if (navCollapsed) {
      setOpenKeys([]);
    }
  }, [navCollapsed]);

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
            // Handed over as it stands, collapsed or not. Which groups may be open while the
            // rail is narrow is decided where the state is kept, not here: filtering it out at
            // this point cannot tell a group that opened itself on arrival from one the reader
            // clicked the icon for, so it suppressed both and the collapsed rail — which is how
            // the rail opens — stopped opening any group at all.
            openKeys={openKeys}
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
