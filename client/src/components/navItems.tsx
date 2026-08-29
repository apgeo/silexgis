// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  ApartmentOutlined,
  AppstoreOutlined,
  CalendarOutlined,
  CarOutlined,
  CheckSquareOutlined,
  CloudUploadOutlined,
  ClusterOutlined,
  CodeSandboxOutlined,
  CompassOutlined,
  ContactsOutlined,
  ControlOutlined,
  DashboardOutlined,
  DatabaseOutlined,
  EnvironmentOutlined,
  FieldTimeOutlined,
  FileTextOutlined,
  FileWordOutlined,
  FlagOutlined,
  FolderOutlined,
  GlobalOutlined,
  GoldOutlined,
  GroupOutlined,
  HistoryOutlined,
  IdcardOutlined,
  MailOutlined,
  MonitorOutlined,
  PictureOutlined,
  ProfileOutlined,
  ReadOutlined,
  SafetyCertificateOutlined,
  ScheduleOutlined,
  TagsOutlined,
  TeamOutlined,
  TableOutlined,
  ToolOutlined,
  UserOutlined,
} from '@ant-design/icons';
import type { ReactNode } from 'react';
import type { TFunction } from 'i18next';
import type { AccessDomainName } from '../api/hooks.ts';

/** A destination: its key is the route it navigates to, less the leading slash. */
export type NavLeaf = { key: string; icon: ReactNode; label: string };

/** A group of destinations. Not a destination itself — see the note on `GROUP_PREFIX`. */
export type NavGroup = NavLeaf & { children: NavLeaf[] };

export type NavEntry = NavLeaf | NavGroup;

/**
 * What marks a key as a group rather than a destination.
 *
 * antd fires no click for a submenu title, so this prefix is not what stops a group being
 * navigated to. It is what makes the distinction checkable: the rail's keys are routes, and a
 * test can only assert that by knowing which of them are not meant to be.
 */
export const GROUP_PREFIX = 'group:';

/** Everything the rail needs to know about what this caller may reach. */
export type NavGates = {
  can: (domain: AccessDomainName) => boolean;
  /** Authoring a taxonomy, which decides what every row under it may say. */
  taxonomyWrite: boolean;
  /** Having something of one's own to import, which is what the detection rules serve. */
  featureCreate: boolean;
  isFullAdmin: boolean;
};

export function isNavGroup(entry: NavEntry): entry is NavGroup {
  return 'children' in entry;
}

/**
 * The rail, grouped.
 *
 * Flat, this list had grown to thirty-one destinations — longer than the screen on a laptop,
 * and most of its length was configuration nobody opens twice a season sitting at the same
 * level as the map. Grouping trades one click for a rail that can be read at a glance, and the
 * pages that moved furthest out of reach are the ones that are also reachable from the feature
 * pages they govern.
 *
 * Built here rather than inline in the shell so the one thing that must hold of it — every
 * destination it can offer resolves to a route — can be asserted against the list itself. Read
 * off the rendered rail instead, that check silently stops covering whatever is collapsed.
 */
export function buildNavItems(t: TFunction, gates: NavGates): NavEntry[] {
  /**
   * A group, or nothing at all when the caller may reach none of its pages.
   *
   * The empty case is the point: several groups hold only rights-gated pages, and a submenu
   * whose children were all filtered away still renders — an arrow that opens on nothing,
   * offered to exactly the people who may not use it.
   */
  const group = (key: string, icon: ReactNode, label: string, children: NavLeaf[]): NavGroup[] =>
    children.length > 0 ? [{ key: `${GROUP_PREFIX}${key}`, icon, label, children }] : [];

  const libraryPages: NavLeaf[] = gates.can('documents')
    ? [
        // The gallery sits beside the map data rather than under the filing tree:
        // photographs are browsed, and paperwork is filed.
        { key: 'gallery', icon: <PictureOutlined />, label: t('nav.gallery') },
        { key: 'albums', icon: <AppstoreOutlined />, label: t('nav.albums') },
        { key: 'cabinets', icon: <FolderOutlined />, label: t('nav.cabinets') },
        // The record of what arrived together, and — for whoever may — the way
        // to import a directory the server can already reach.
        { key: 'uploads', icon: <CloudUploadOutlined />, label: t('nav.uploads') },
      ]
    : [];

  // Each administration destination follows its own domain — "admin" is not a rank any more,
  // just the pages a person's rights happen to include.
  const adminPages: NavLeaf[] = [
    ...(gates.can('audit')
      ? [{ key: 'admin/audit', icon: <HistoryOutlined />, label: t('nav.audit') }]
      : []),
    ...(gates.can('permissionGroups')
      ? [{
          key: 'admin/permission-groups',
          icon: <SafetyCertificateOutlined />,
          label: t('nav.permissionGroups'),
        }]
      : []),
    ...(gates.can('settings')
      ? [{ key: 'admin/messaging', icon: <MailOutlined />, label: t('nav.messaging') }]
      : []),
    ...(gates.can('settings')
      ? [{
          key: 'admin/notification-health',
          icon: <MonitorOutlined />,
          label: t('nav.notificationHealth'),
        }]
      : []),
    // The elevation surface is one installation-wide asset, not content anybody
    // owns, so the right to see the builds is held over the domain and read is
    // what the page needs — starting one is a separate right the page asks for.
    ...(gates.can('terrain')
      ? [{ key: 'admin/terrain', icon: <GlobalOutlined />, label: t('nav.terrain') }]
      : []),
  ];

  // The vocabularies every other page reads its wording from. Gathered because they are edited
  // rarely and together, and because what each one governs is easier to say next to the others
  // than scattered down a flat rail.
  const configPages: NavLeaf[] = [
    ...(gates.can('featureSets')
      ? [{ key: 'admin/feature-sets', icon: <GroupOutlined />, label: t('nav.featureSets') }]
      : []),
    // Gated on write, not read: every account can read the taxonomies, so a read
    // check would offer this page to everyone. Authoring a kind's schema decides
    // what every document of that kind may say, which is administration.
    ...(gates.taxonomyWrite
      ? [
          { key: 'admin/document-types', icon: <ProfileOutlined />, label: t('nav.documentTypes') },
          // The same gate, for the same reason: what a trip purpose asks a report to
          // record decides what every trip under it may say.
          { key: 'admin/trip-types', icon: <CompassOutlined />, label: t('nav.tripTypes') },
          // And again for what somebody did on a trip: every roster row renders its job
          // from this list, so the wording here is what every trip reads by.
          { key: 'admin/participant-roles', icon: <IdcardOutlined />, label: t('nav.participantRoles') },
          // And once more for the layout a trip is written up in: a club's own layout
          // decides what every write-up it circulates says, and how.
          { key: 'admin/report-templates', icon: <FileWordOutlined />, label: t('nav.reportTemplates') },
        ]
      : []),
    ...(gates.isFullAdmin
      ? [{ key: 'admin/relation-types', icon: <ApartmentOutlined />, label: t('nav.relationTypes') }]
      : []),
    // Everyone with something to import has rules of their own to keep, so this is
    // not an administrator's page — only promoting a set to what a group or the
    // installation inherits is, and that is refused on the server.
    ...(gates.featureCreate
      ? [{ key: 'admin/term-rules', icon: <TagsOutlined />, label: t('nav.termRules') }]
      : []),
    ...(gates.can('messageTemplates')
      ? [{ key: 'admin/message-templates', icon: <FileTextOutlined />, label: t('nav.templates') }]
      : []),
  ];

  const navItems = [
    // The three ways of looking at everything at once stay at the top level, unwrapped: they are
    // what most sessions open with, and a click to reach the map is a click too many.
    { key: 'map', icon: <EnvironmentOutlined />, label: t('nav.map') },
    { key: 'map3d', icon: <CodeSandboxOutlined />, label: t('nav.map3d') },
    { key: 'dashboard', icon: <DashboardOutlined />, label: t('nav.dashboard') },
    ...group('cadastre', <ClusterOutlined />, t('nav.groups.cadastre'), [
      { key: 'caves', icon: <TableOutlined />, label: t('nav.caves') },
      { key: 'features', icon: <GoldOutlined />, label: t('nav.features') },
      { key: 'geodata', icon: <DatabaseOutlined />, label: t('nav.geodata') },
    ]),
    // The filing tree is readable by anyone who may read documents at all; what
    // is on a shelf is decided per document, not by hiding the shelf.
    ...group('library', <ReadOutlined />, t('nav.groups.library'), libraryPages),
    ...group('activity', <FieldTimeOutlined />, t('nav.groups.activity'), [
      // Everything dated, read as one list. Not gated on a right: it spans two
      // families of row and the answer is narrowed to what each reader may open,
      // row by row, so there is no single domain that could decide the offer.
      { key: 'calendar', icon: <CalendarOutlined />, label: t('nav.calendar') },
      // The dated things a club runs that are not trips or camps. Not gated on a
      // right either: everybody may keep their own, and what a caller may read and
      // write is settled per row.
      { key: 'events', icon: <ScheduleOutlined />, label: t('nav.events') },
      { key: 'trip-logs', icon: <CarOutlined />, label: t('nav.trips') },
      // The lists trips work through. Everybody may keep their own, so this is not
      // gated on a right: what a caller may read and write is settled per row.
      { key: 'checklists', icon: <CheckSquareOutlined />, label: t('nav.checklists') },
      { key: 'expeditions', icon: <FlagOutlined />, label: t('nav.expeditions') },
    ]),
    ...group('people', <ContactsOutlined />, t('nav.groups.people'), [
      { key: 'caving-groups', icon: <TeamOutlined />, label: t('nav.cavingGroups') },
      { key: 'cavers', icon: <UserOutlined />, label: t('nav.cavers') },
    ]),
    ...group('admin', <ControlOutlined />, t('nav.groups.admin'), adminPages),
    ...group('config', <ToolOutlined />, t('nav.groups.config'), configPages),
  ];

  return navItems;
}
