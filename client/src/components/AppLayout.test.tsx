// SPDX-License-Identifier: AGPL-3.0-or-later
import { matchRoutes, MemoryRouter, Route, Routes } from 'react-router-dom';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import AppLayout from './AppLayout.tsx';
import { routes } from '../App.tsx';
import type { UnreadNotificationCount } from '../api/hooks.ts';
import i18n from '../i18n';
import { buildNavItems, isNavGroup, GROUP_PREFIX } from './navItems.tsx';

let mobile = false;
let capabilities: Record<string, string> | undefined;
// The rank the relation vocabulary is gated on, which no capability answer expresses:
// membership of the protected group is what the server resolves it from.
let permissionGroups: { slug: string }[] = [];

vi.mock('../hooks/useIsMobile.ts', () => ({ useIsMobile: () => mobile }));
const saveLocale = vi.fn();
let me: { avatarUrl: null; displayName?: string | null; email?: string } = { avatarUrl: null };
// Typed from the wire so the stub cannot drift from the shape the bell actually reads.
let unreadNotifications: UnreadNotificationCount | undefined;
vi.mock('../api/hooks.ts', () => ({
  useMe: () => ({ data: me }),
  useUpdateLocale: () => ({ mutate: saveLocale }),
  useMyPermissionGroups: () => ({ data: permissionGroups }),
  useCapabilities: () => ({ data: capabilities ? { domains: capabilities } : undefined }),
  // The header's bell reads this; the mock replaces the module wholesale, so a hook left out
  // here is undefined at the call site and every test in this file dies on the render.
  useUnreadNotificationCount: () => ({ data: unreadNotifications }),
  // The rail asks whether this installation has a neighbouring photo library the reader may look
  // through. Answered as an installation that runs none of these products, which is the shipped
  // shape: what this file is about is the destinations the capabilities carry, and the photo
  // libraries are gated on a separate answer of their own — pinned in the nav cases below.
  usePhotoLibraries: () => ({ data: undefined }),
  // The real helper, inlined: the mock replaces the module wholesale.
  hasAccessAction: (actions: string | undefined, flag: string) =>
    (actions ?? '').split(',').map((x) => x.trim()).includes(flag),
}));
// The user-name claim carries the protected label the server publishes to third parties: for an
// account that never set a display name it is a generated pseudonym, not a name or an address.
vi.mock('../auth/auth.tsx', () => ({
  useAuth: () => ({
    user: { profile: { preferred_username: 'user-4f2b8c1d', email: 'caver@example.org' } },
    signOut: vi.fn(),
  }),
}));

function renderShell(at = '/map') {
  return render(
    <MemoryRouter initialEntries={[at]}>
      <Routes>
        <Route path="/" element={<AppLayout />}>
          <Route path="map" element={<div>map page</div>} />
          <Route path="notifications" element={<div>inbox page</div>} />
          <Route path="checklists" element={<div>checklists page</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

/** The label of whatever the sider is showing as the page you are on. */
const selectedItem = () =>
  document.querySelector('.ant-menu-item-selected')?.textContent?.trim();

const sider = () => document.querySelector('.ant-layout-sider');
const zeroWidthTrigger = () => document.querySelector('.ant-layout-sider-zero-width-trigger');

/**
 * Open the rail, and then a group inside it.
 *
 * Both are needed before a grouped destination is in the document at all. The rail opens
 * collapsed, where antd renders a group's children into a hover popup rather than inline; and a
 * closed inline group renders no children either. So a page that is offered but not looked for
 * this way is indistinguishable from one that is gated away — which is what these assertions
 * are about, and why they drive the rail instead of reading the list behind it.
 */
const expandRail = () => fireEvent.click(document.querySelector('.ant-layout-sider-trigger')!);
const openGroup = (label: string) => fireEvent.click(screen.getByText(label));

beforeEach(() => {
  me = { avatarUrl: null, displayName: null, email: 'caver@example.org' };
  mobile = false;
  capabilities = undefined;
  permissionGroups = [];
  unreadNotifications = undefined;
  saveLocale.mockClear();
});

afterEach(cleanup);

describe('AppLayout sider', () => {
  it('keeps the collapsed icon rail on desktop', () => {
    renderShell();

    // 80px is antd's collapsed rail. Guards the reason this sider is controlled rather
    // than using antd's `breakpoint` prop: that prop drives `collapsed` in both
    // directions and would force the rail open here, to its full 200px.
    expect(sider()).toHaveStyle({ width: '80px' });
    expect(zeroWidthTrigger()).toBeNull();
    expect(screen.getByText('map page')).toBeInTheDocument();
  });

  it('takes the rail off-canvas with a trigger on mobile', () => {
    mobile = true;
    renderShell();

    expect(sider()).toHaveStyle({ width: '0px' });
    // antd's own edge trigger is what brings it back; without it the nav is unreachable.
    expect(zeroWidthTrigger()).not.toBeNull();
  });
});

describe('AppLayout account name', () => {
  it('names an account with no display name by its address, never by the protected label', () => {
    renderShell();

    // Showing somebody their own address is not the leak the label exists to close; showing them
    // "user-4f2b8c1d" instead leaves no way to tell which account is signed in on a shared machine.
    expect(screen.getByText('caver@example.org')).toBeInTheDocument();
    expect(screen.queryByText('user-4f2b8c1d')).toBeNull();
  });

  it('prefers the display name once there is one', () => {
    me = { avatarUrl: null, displayName: 'Ana', email: 'caver@example.org' };
    renderShell();

    expect(screen.getByText('Ana')).toBeInTheDocument();
  });
});

describe('AppLayout header', () => {
  it('carries the way to the inbox, which no sidebar entry offers', () => {
    unreadNotifications = { unread: 4 };
    renderShell();

    // The inbox is reached from here and nowhere else: it is not a sidebar destination, so a
    // rewrite of this cluster that drops the bell leaves the page registered and unreachable.
    expect(screen.getByRole('button', { name: 'Notifications' })).toBeInTheDocument();
    expect(screen.getByTitle('4')).toBeInTheDocument();
  });

  it('lights nothing in the rail while the inbox is open', () => {
    // The other half of registering this address: the shell resolves a section from the path and
    // falls back to the map when it recognises none, so an inbox missing from that list lights
    // the Map item and tells the reader they are somewhere they are not. Nothing lighting up is
    // the intended answer here — the inbox is reached from the header, not from the rail.
    renderShell('/notifications');

    expect(screen.getByText('inbox page')).toBeInTheDocument();
    expect(document.querySelectorAll('.ant-menu-item-selected')).toHaveLength(0);
  });
});

describe('AppLayout selected destination', () => {
  /**
   * Every destination in the rail has to be recognised from the path, or opening it lights up
   * the map instead — which reads as "you are on the map" while you are plainly not. The failure
   * is silent: navigation still works, so only the highlight is wrong.
   */
  it('lights up the destination the path is under', () => {
    capabilities = { checklists: 'read' };
    renderShell('/checklists');

    expect(screen.getByText('checklists page')).toBeInTheDocument();
    // The group holding the page opens itself on arrival, so the destination is lit as soon as
    // the rail is opened — no click on the group needed.
    expandRail();
    expect(selectedItem()).toBe('Checklists');
  });
});

describe('AppLayout collapsed rail', () => {
  it('opens no group while the rail is collapsed, so nothing floats over the page', async () => {
    // The rail opens collapsed, and collapsed antd draws an open group as a floating flyout
    // beside it. The group holding the current page opens itself, so without this the reader
    // lands on a page with a menu panel they never asked for sitting on top of it — and it
    // swallows clicks on whatever is underneath. It cost four browser flows before it was seen,
    // every one of them failing as "something intercepts pointer events" rather than as a menu.
    capabilities = { checklists: 'read' };
    renderShell('/checklists');

    // The page is shown, and the group that holds it is the one that would have opened.
    expect(screen.getByText('checklists page')).toBeInTheDocument();
    // On the marked-open class, because that is the one thing that actually differs here.
    // jsdom lays nothing out, so the flyout that does the damage in a real browser is not drawn
    // and cannot be asserted on — a check for the popup element, or for its children being
    // absent, passes just as well with the defect present. This class is what antd sets from
    // the open state, and it is what flips.
    expect(document.querySelectorAll('.ant-menu-submenu-open')).toHaveLength(0);

    // ...and expanding the rail still opens it, which is the behaviour this must not cost.
    expandRail();
    expect(selectedItem()).toBe('Checklists');
  });

  it('opens a group the reader clicks, collapsed rail and all', async () => {
    // The other half of the rule above, and the one it cost. Suppressing every open key while
    // the rail was narrow could not tell a group that opened itself on arrival from one the
    // reader had just clicked, so it refused both: the rail opens collapsed, which made every
    // group in it unopenable, and the destinations under them unreachable without first
    // finding the expand trigger. Nothing said so — the click was recorded, simply never drawn.
    capabilities = { checklists: 'read' };
    renderShell();

    // Collapsed, as the rail opens, and with nothing open in it.
    expect(sider()).toHaveStyle({ width: '80px' });
    expect(document.querySelectorAll('.ant-menu-submenu-open')).toHaveLength(0);

    // By pointer, not by `openGroup`: collapsed, antd draws the group as a popup and opens it
    // on hover, so a bare click event carries nothing it listens to. A real click opens one
    // because the pointer arrives first — which is exactly what this reproduces, and why the
    // reader reports it as a click that does nothing.
    fireEvent.mouseEnter(screen.getByText('Activity'));
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 300));
    });

    // On the marked-open class for the same reason the sibling test gives: jsdom draws no
    // flyout, so the popup itself cannot be asserted on either way. This class is what antd
    // sets from the open state, and with the defect present it stays off.
    expect(document.querySelectorAll('.ant-menu-submenu-open')).toHaveLength(1);
  });

  it('closes an open group when the rail narrows under it', () => {
    // The flyout arrives by this route too: a group opened inline is over the page the moment
    // the rail collapses beneath it, with no arrival involved to blame for it.
    capabilities = { checklists: 'read' };
    renderShell('/checklists');
    expandRail();
    expect(document.querySelectorAll('.ant-menu-submenu-open')).toHaveLength(1);

    // The same trigger, back the other way.
    expandRail();

    expect(sider()).toHaveStyle({ width: '80px' });
    expect(document.querySelectorAll('.ant-menu-submenu-open')).toHaveLength(0);
  });
});

describe('AppLayout nav gating', () => {
  it('offers no admin destinations before capabilities arrive', () => {
    renderShell();
    expandRail();

    // No administration group at all, rather than an empty one: with nothing granted there is
    // nothing under it, and an arrow that opens on nothing is worse than no arrow.
    expect(screen.queryByText('Administration')).toBeNull();
    expect(screen.queryByText('Permission groups')).toBeNull();
    expect(screen.queryByText('Audit')).toBeNull();
  });

  it('shows exactly the destinations the capabilities carry', () => {
    capabilities = {
      permissionGroups: 'read, write',
      audit: 'read',
      // Held but without read — must not surface the page.
      messageTemplates: 'write',
    };
    renderShell();
    expandRail();
    openGroup('Administration');

    expect(screen.getByText('Permission groups')).toBeInTheDocument();
    expect(screen.getByText('Audit')).toBeInTheDocument();
    expect(screen.queryByText('Messaging')).toBeNull();
    // Configuration holds the other three, and none of them was earned, so the group itself
    // never appears — there is nothing to open.
    expect(screen.queryByText('Configuration')).toBeNull();
    expect(screen.queryByText('Message texts')).toBeNull();
    expect(screen.queryByText('Feature sets')).toBeNull();
  });

  it('offers the terrain builder only to a holder of the terrain right', () => {
    // Every other domain right there is, and still not the terrain page: the elevation
    // surface is its own installation-level domain, so no other right earns it. The holder
    // is asserted in the same test, because a gate that had quietly stopped offering the
    // page to anybody would otherwise read as a passing security assertion.
    capabilities = { permissionGroups: 'read, write', taxonomies: 'read, write', audit: 'read' };
    renderShell();
    expandRail();
    openGroup('Administration');
    expect(screen.queryByText('Terrain')).toBeNull();

    cleanup();
    capabilities = { terrain: 'read, execute, delete' };
    renderShell();
    expandRail();
    openGroup('Administration');
    expect(screen.getByText('Terrain')).toBeInTheDocument();
  });

  it('offers the relation vocabulary to the rank that can write it, not to a domain right', () => {
    // Every domain right there is, and still not the rank: the vocabulary is not a
    // resource domain, so no amount of domain access earns the page.
    capabilities = { permissionGroups: 'read, write', taxonomies: 'read, write', audit: 'read' };
    renderShell();
    expandRail();
    openGroup('Configuration');
    expect(screen.queryByText('Link relations')).toBeNull();

    cleanup();
    permissionGroups = [{ slug: 'full-administrators' }];
    renderShell();
    expandRail();
    openGroup('Configuration');
    expect(screen.getByText('Link relations')).toBeInTheDocument();
  });
});

describe('AppLayout nav destinations', () => {
  // Everything granted, so nothing is filtered out of view. Every domain answers with every
  // action, whatever domains exist — a fixed list here would quietly stop covering a domain
  // added later, which is exactly the case this guards.
  const everything = {
    can: () => true,
    taxonomyWrite: true,
    featureCreate: true,
    tripLogCreate: true,
    photoLibrary: true,
    isFullAdmin: true,
  };

  /** Every key the rail can offer, groups flattened into the destinations they hold. */
  const allEntries = () => {
    const items = buildNavItems(i18n.t, everything);
    return items.flatMap((item) => (isNavGroup(item) ? [item, ...item.children] : [item]));
  };

  it('offers no destination the router cannot match', () => {
    // Selecting an item navigates to `/<key>`, and this application's router has no fallback
    // route and no error element: an unmatched path does not land on an empty page, it replaces
    // header, sider and content with the router's own error screen, leaving no way back inside
    // the application. So every key the rail can offer has to resolve.
    const destinations = allEntries()
      .map((entry) => entry.key)
      .filter((key) => !key.startsWith(GROUP_PREFIX));

    // The rail is long; a mistake that had emptied it would otherwise pass here by finding
    // nothing at all to check.
    expect(destinations).toContain('admin/terrain');
    expect(destinations).toContain('admin/trip-types');
    expect(destinations.length).toBeGreaterThan(15);

    const unmatched = destinations.filter((key) => matchRoutes(routes, `/${key}`) === null);
    expect(unmatched).toEqual([]);
  });

  it('gives every entry a label and hides no group behind an empty one', () => {
    const items = buildNavItems(i18n.t, everything);

    // A group that renders with no children is an arrow that opens on nothing. The builder
    // drops those, so an empty one here means a group was assembled without its gate.
    for (const item of items) {
      if (isNavGroup(item)) {
        expect(item.children.length).toBeGreaterThan(0);
      }
    }

    // A missing key renders as the key itself, which reads as a destination named
    // "nav.groups.cadastre" sitting in the rail.
    for (const entry of allEntries()) {
      expect(entry.label).toBeTruthy();
      expect(entry.label).not.toMatch(/^nav\./);
    }
  });

  it('offers nothing but the always-public pages to an account granted nothing', () => {
    const items = buildNavItems(i18n.t, {
      can: () => false,
      taxonomyWrite: false,
      featureCreate: false,
      tripLogCreate: false,
      photoLibrary: false,
      isFullAdmin: false,
    });
    const keys = items.map((item) => item.key);

    // The groups that hold only rights-gated pages disappear entirely rather than becoming
    // empty arrows; the ones every account may use stay.
    expect(keys).not.toContain(`${GROUP_PREFIX}library`);
    expect(keys).not.toContain(`${GROUP_PREFIX}admin`);
    expect(keys).not.toContain(`${GROUP_PREFIX}config`);
    expect(keys).toContain('map');
    expect(keys).toContain(`${GROUP_PREFIX}activity`);
  });

  it('offers the spreadsheet review only to an account that may record trips', () => {
    // The distinction this pins is between reading trips and writing them. Reading a club's
    // spreadsheet ends in creating every trip in it, and the server refuses the screen — its
    // preview included — to an account that may not, so an entry offered on the read right
    // invites somebody to spend an afternoon on a review they will be turned down for.
    const activityOf = (gates: Parameters<typeof buildNavItems>[1]) => {
      const group = buildNavItems(i18n.t, gates).find(
        (item) => item.key === `${GROUP_PREFIX}activity`,
      );
      return isNavGroup(group!) ? group.children.map((child) => child.key) : [];
    };

    const reader = { ...everything, tripLogCreate: false };
    expect(activityOf(reader)).not.toContain('trip-logs/import');
    // The control: the reader still reaches the trip list itself, so what disappeared is the
    // import and not the whole group.
    expect(activityOf(reader)).toContain('trip-logs');

    expect(activityOf(everything)).toContain('trip-logs/import');
  });

  it('offers the neighbouring photo library only when there is one to look through', () => {
    // Not gated on a right of this application's own. Who may reach those libraries is one
    // installation-wide setting the server answers, and an installation that runs none of these
    // products has nothing behind the page — so an entry offered on the documents right would
    // send most readers of most installations to a sentence saying there is nothing there.
    const libraryOf = (gates: Parameters<typeof buildNavItems>[1]) => {
      const group = buildNavItems(i18n.t, gates).find(
        (item) => item.key === `${GROUP_PREFIX}library`,
      );
      return isNavGroup(group!) ? group.children.map((child) => child.key) : [];
    };

    expect(libraryOf(everything)).toContain('photo-library');

    const without = { ...everything, photoLibrary: false };
    expect(libraryOf(without)).not.toContain('photo-library');
    // The control: this installation's own gallery is still offered, so what disappeared is the
    // neighbouring library and not the group around it.
    expect(libraryOf(without)).toContain('gallery');
  });
});

describe('AppLayout language switch', () => {
  it('tells the server which language was chosen, and the zone it was chosen in', async () => {
    renderShell();

    // By accessible name, not position: several comboboxes exist once a page is mounted below.
    const selector = screen.getByRole('combobox', { name: 'Language' });
    await act(async () => {
      fireEvent.mouseDown(selector);
    });
    await act(async () => {
      fireEvent.click(await screen.findByTitle('RO'));
    });

    // The language a person reads in is also the language every message to them is written in,
    // so it has to reach the account and not only this browser.
    expect(saveLocale).toHaveBeenCalledTimes(1);
    expect(saveLocale.mock.calls[0][0]).toMatchObject({ language: 'ro' });
    const { timeZone } = saveLocale.mock.calls[0][0] as { timeZone: string | null };
    expect(timeZone === null || typeof timeZone === 'string').toBe(true);
  });
});
