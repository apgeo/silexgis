// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ExpeditionInfo } from '../../api/hooks.ts';

const CAMP = '77777777-8888-9999-aaaa-bbbbbbbbbbbb';

const { campSpy, accessSpy, canSpy, keepMutate, downloadSpy } = vi.hoisted(() => ({
  campSpy: vi.fn(),
  accessSpy: vi.fn(),
  canSpy: vi.fn(),
  keepMutate: vi.fn(),
  downloadSpy: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  useExpedition: () => campSpy(),
  useCavingGroups: () => ({ data: [{ id: 'club-1', name: 'Clubul Speo' }] }),
  useReportTemplatesOfKind: () => ({ data: [] }),
  useEffectiveAccess: () => accessSpy(),
  useCan: (domain: string, action: string) => canSpy(domain, action),
  useKeepExpeditionReport: () => ({ mutate: keepMutate, isPending: false }),
  usePhotos: () => ({ data: { items: [] }, isPending: false }),
  parseAccessActions: (actions: string) => new Set(actions.split(',')),
}));

vi.mock('../../api/download.ts', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/download.ts')>();
  return { ...actual, downloadFile: (url: string) => downloadSpy(url) };
});

// The sections are the camp page's own, exercised by their own tests; here each has only to be
// the thing this document mounts in its place.
vi.mock('./ExpeditionTripsTab.tsx', () => ({
  default: () => <div>the trips gathered into the camp</div>,
}));
vi.mock('./ExpeditionRosterTab.tsx', () => ({
  default: () => <div>who was at the camp</div>,
}));
vi.mock('./ExpeditionTripAccounts.tsx', () => ({
  default: () => <div>what each trip wrote about itself</div>,
}));
vi.mock('../../components/trips/TripGeometryField.tsx', () => ({
  default: () => <div>the working area, drawn</div>,
}));

const { default: ExpeditionReportPage } = await import('./ExpeditionReportPage.tsx');

function camp(overrides: Partial<ExpeditionInfo> = {}): ExpeditionInfo {
  return {
    id: CAMP,
    name: 'Bihor summer camp',
    description: 'A fortnight in the Bihor.',
    startDate: '2026-07-18',
    endDate: '2026-08-01',
    geom: null,
    ownerUserId: 'owner-1',
    cavingGroupId: 'club-1',
    visibility: 'cavingGroup',
    state: 'published',
    publishedAt: null,
    createdAt: '2026-07-01T00:00:00Z',
    updatedAt: '2026-07-01T00:00:00Z',
    ...overrides,
  } as ExpeditionInfo;
}

function renderPage() {
  return render(
    <App>
      <MemoryRouter initialEntries={[`/expeditions/${CAMP}/report`]}>
        <Routes>
          <Route path="/expeditions/:id/report" element={<ExpeditionReportPage />} />
        </Routes>
      </MemoryRouter>
    </App>,
  );
}

afterEach(cleanup);
beforeEach(() => {
  campSpy.mockReturnValue({ data: camp(), isPending: false });
  accessSpy.mockReturnValue({ data: { actions: 'read' } });
  canSpy.mockReturnValue(false);
  keepMutate.mockReset();
  downloadSpy.mockReset().mockResolvedValue(undefined);
});

describe('the camp write-up', () => {
  it('lays the camp out as a document: its facts, its account, its trips and its roster', () => {
    renderPage();

    expect(screen.getByTestId('expedition-report-name').textContent).toBe('Bihor summer camp');
    expect(screen.getByTestId('expedition-report-account').textContent).toBe('A fortnight in the Bihor.');
    // Named twice on purpose, under the title and in the facts: the document reads top down.
    expect(screen.getAllByText('Clubul Speo', { exact: false }).length).toBeGreaterThan(0);
    expect(screen.getByText('the trips gathered into the camp')).toBeTruthy();
    expect(screen.getByText('who was at the camp')).toBeTruthy();
    // A draft is said on the document; an announced camp carries no badge.
    expect(screen.queryByTestId('trip-state')).toBeNull();
  });

  it('downloads the document for this camp, in the layout chosen', () => {
    renderPage();
    fireEvent.click(screen.getByTestId('expedition-report-download'));

    expect(downloadSpy).toHaveBeenCalledTimes(1);
    expect(downloadSpy.mock.calls[0][0]).toBe(`/api/v1/expeditions/${CAMP}/report?`);
  });

  it('offers to file the write-up only to somebody who may change the camp and put a document in the archive', () => {
    // Write on the camp without the right to file a document: the server would refuse the
    // filing, so the button is not drawn.
    accessSpy.mockReturnValue({ data: { actions: 'read,write' } });
    canSpy.mockImplementation((domain: string) => domain !== 'documents');
    renderPage();
    expect(screen.queryByTestId('expedition-report-keep')).toBeNull();

    cleanup();
    canSpy.mockReturnValue(true);
    renderPage();
    fireEvent.click(screen.getByTestId('expedition-report-keep'));
    expect(keepMutate).toHaveBeenCalledTimes(1);
    expect(keepMutate.mock.calls[0][0]).toEqual({ id: CAMP, templateId: undefined });
  });

  it('says on the document that it is a draft, and writes the working area out for the printer', () => {
    campSpy.mockReturnValue({
      data: camp({
        state: 'draft',
        geom: { type: 'Point', coordinates: [22.7, 46.5] } as ExpeditionInfo['geom'],
      }),
      isPending: false,
    });
    renderPage();

    expect(screen.getByTestId('trip-state').textContent).toContain('Draft');
    expect(screen.getByText('the working area, drawn')).toBeTruthy();
    expect(screen.getByTestId('expedition-report-area').textContent).toMatch(/Working area: a point at/);
  });
});
