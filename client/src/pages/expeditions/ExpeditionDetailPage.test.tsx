// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { ExpeditionInfo, ResLinkMember } from '../../api/hooks.ts';
import MemberChip from '../../components/reslinks/MemberChip.tsx';
import ExpeditionDetailPage from './ExpeditionDetailPage.tsx';

const CAMP = '77777777-8888-9999-aaaa-bbbbbbbbbbbb';

const { campSpy } = vi.hoisted(() => ({ campSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useExpedition: () => campSpy(),
  useCavingGroups: () => ({ data: [{ id: 'club-1', name: 'Clubul Speo' }] }),
  useEffectiveAccess: () => ({ data: undefined }),
  useCan: () => false,
  parseAccessActions: (actions: string) => new Set(actions.split(',')),
}));

// The sections are mounted by name here, not exercised: each has its own tests, and each asks the
// server for something of its own that this page knows nothing about.
vi.mock('../../components/tags/TagChips.tsx', () => ({
  default: ({ entityType }: { entityType: string }) => <span>tags for {entityType}</span>,
}));
vi.mock('../../components/reslinks/LinksSection.tsx', () => ({
  default: ({ entityType }: { entityType: string }) => <div>links for {entityType}</div>,
}));
vi.mock('../../components/history/HistoryPanel.tsx', () => ({
  default: ({ entityType }: { entityType: string }) => <div>history for {entityType}</div>,
}));
vi.mock('./ExpeditionTripsTab.tsx', () => ({
  default: () => <div>the trips gathered into the camp</div>,
}));
vi.mock('./ExpeditionFilesTab.tsx', () => ({
  default: () => <div>what is filed against the camp</div>,
}));
vi.mock('./ExpeditionRosterTab.tsx', () => ({
  default: () => <div>who was at the camp</div>,
}));

function camp(overrides: Partial<ExpeditionInfo> = {}): ExpeditionInfo {
  return {
    id: CAMP,
    name: 'Bihor summer camp',
    description: null,
    startDate: '2026-07-18',
    endDate: '2026-08-01',
    geom: null,
    ownerUserId: 'owner-1',
    cavingGroupId: null,
    visibility: 'public',
    state: 'published',
    publishedAt: null,
    createdAt: '2026-07-01T00:00:00Z',
    updatedAt: '2026-07-01T00:00:00Z',
    ...overrides,
  } as ExpeditionInfo;
}

function renderPage(entry = `/expeditions/${CAMP}`) {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/expeditions/:id" element={<ExpeditionDetailPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

afterEach(cleanup);
beforeEach(() => {
  campSpy.mockReturnValue({ data: camp(), isPending: false, isError: false });
});

describe('the camp page', () => {
  it('names the camp and the days it ran', () => {
    renderPage();

    expect(screen.getByTestId('expedition-name').textContent).toBe('Bihor summer camp');
    // A camp that ran on past its first day reads as a range; the dates are rebuilt as local
    // days, never handed to a date constructor that would read them as UTC midnight.
    expect(screen.getByTestId('expedition-dates').textContent).toContain('–');
  });

  it('says one day when the camp did not run on past its first', () => {
    campSpy.mockReturnValue({
      data: camp({ endDate: null }),
      isPending: false,
      isError: false,
    });
    renderPage();

    // An absent end is "it did not run on past its first day", not "the end is unknown" — so a
    // one-day camp must never read as a range of itself.
    expect(screen.getByTestId('expedition-dates').textContent).not.toContain('–');
  });

  it('tells a reader the camp is not there, whether it is missing or merely not theirs', () => {
    // Both cases arrive as the same refusal on purpose: an address that answered differently for
    // the two would be an address anybody could probe for the existence of a camp they are not
    // admitted to. The page must therefore never claim to know which of the two it is.
    campSpy.mockReturnValue({
      data: undefined,
      isPending: false,
      isError: true,
      error: new ApiError(404, 'expedition.not_found'),
    });
    renderPage();

    expect(screen.getByText('No such camp')).toBeTruthy();
    expect(screen.queryByTestId('expedition-name')).toBeNull();
  });

  it('opens on its own tab, and reads the tab out of the address', () => {
    renderPage();
    expect(screen.getByText('the trips gathered into the camp')).toBeTruthy();

    cleanup();
    renderPage(`/expeditions/${CAMP}?tab=files`);
    expect(screen.getByText('what is filed against the camp')).toBeTruthy();

    cleanup();
    renderPage(`/expeditions/${CAMP}?tab=roster`);
    expect(screen.getByText('who was at the camp')).toBeTruthy();
  });

  it('falls back to its own tab when the address names one it does not have', () => {
    // antd draws nothing at all under the tab strip for an activeKey matching no pane, so an
    // address somebody edited by hand must not be able to produce a page with no content.
    renderPage(`/expeditions/${CAMP}?tab=quantumTunnel`);

    expect(screen.getByText('the trips gathered into the camp')).toBeTruthy();
  });

  it('mounts the shared sections against the camp itself', () => {
    renderPage();

    expect(screen.getByText('tags for expedition')).toBeTruthy();
    expect(screen.getByText('links for expedition')).toBeTruthy();
  });
});

describe('a chip naming a camp', () => {
  function member(): ResLinkMember {
    return {
      id: 'member-1',
      targetType: 'expedition',
      targetId: CAMP,
      isMain: false,
      display: {
        title: 'Bihor summer camp',
        subtitle: null,
        path: null,
        thumbnailUrl: null,
        route: `/expeditions/${CAMP}`,
      },
    } as unknown as ResLinkMember;
  }

  it('takes the reader to the camp, and the camp renders', () => {
    // The defect this stands against: the resolver naming an address the application does not
    // answer, so every camp chip lands the reader on the router's error screen. Following the
    // chip to a *rendered* page is the only assertion that catches the two halves drifting apart.
    render(
      <MemoryRouter initialEntries={['/trip-logs/trip-1']}>
        <Routes>
          <Route path="/trip-logs/:id" element={<MemberChip member={member()} />} />
          <Route path="/expeditions/:id" element={<ExpeditionDetailPage />} />
        </Routes>
      </MemoryRouter>,
    );

    fireEvent.click(screen.getByText('Bihor summer camp'));

    expect(screen.getByTestId('expedition-name').textContent).toBe('Bihor summer camp');
  });
});
