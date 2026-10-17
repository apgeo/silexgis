// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const FEATURE = '22222222-2222-2222-2222-222222222222';
const OTHER = '44444444-4444-4444-4444-444444444444';

const { setLinksSpy, queryCalls } = vi.hoisted(() => ({
  setLinksSpy: vi.fn(),
  queryCalls: [] as { body: unknown; enabled: boolean }[],
}));

const hit = (id: string, title: string, subtitle: string) =>
  ({ world: 'feature', id, title, subtitle, symbol: null, placeable: false });

vi.mock('../../api/hooks.ts', () => ({
  useFeature: () => ({ data: undefined, isError: false }),
  useFeatureLinks: () => ({ data: [] }),
  useLinkKinds: () => ({ data: [{ code: 'connectsTo', name: 'Connects to' }] }),
  useSetFeatureLinks: () => ({ mutateAsync: setLinksSpy, isPending: false }),
  useFilterVocabulary: () => ({ data: { worlds: [{ world: 'feature', labelKey: 'x', fields: [], sorts: [] }] } }),
  // The search answers with the feature itself and one other.
  useFilterQuery: (body: unknown, enabled: boolean) => {
    queryCalls.push({ body, enabled });
    return enabled
      ? {
          data: {
            worlds: [{
              world: 'feature',
              hits: [hit(FEATURE, 'Peștera Urșilor', 'Cave'), hit(OTHER, 'Izbucul Tăuz', 'Spring')],
              total: null,
            }],
            page: 1, pageSize: 6, counted: false,
          },
          isFetching: false,
        }
      : { data: undefined, isFetching: false };
  },
  useFilterResolve: () => ({ data: [] }),
}));

const { default: LinksCard } = await import('./LinksCard.tsx');

function show() {
  return render(
    <AntApp>
      <MemoryRouter>
        <LinksCard featureId={FEATURE} canEdit />
      </MemoryRouter>
    </AntApp>,
  );
}

async function openEditorAndSearch() {
  fireEvent.click(screen.getByRole('button', { name: /Edit links/ }));
  fireEvent.change(await screen.findByTestId('feature-link-target-picker-input'), { target: { value: 'iz' } });
  // The search is debounced; a loaded machine can take longer than the default second to answer.
  await screen.findByRole('button', { name: /Izbucul Tăuz/ }, { timeout: 3000 });
}

function chooseKind() {
  const dialog = screen.getByRole('dialog');
  fireEvent.mouseDown(within(dialog).getByRole('combobox'));
  fireEvent.click(screen.getByTitle('Connects to'));
}

afterEach(cleanup);
beforeEach(() => {
  setLinksSpy.mockReset().mockResolvedValue([]);
  queryCalls.length = 0;
});

describe('the links card target picker', () => {
  it('searches the whole feature world, unnarrowed by kind', async () => {
    show();
    await openEditorAndSearch();

    const document = JSON.stringify(queryCalls.filter((c) => c.enabled).at(-1)!.body);
    expect(document).toContain('"feature"');
    expect(document).not.toContain('"kind"');
  });

  it('does not offer the feature as a target of its own link', async () => {
    show();
    await openEditorAndSearch();

    const offered = screen.getAllByTestId('feature-link-target-picker-row').map((r) => r.textContent);
    expect(offered).toEqual([expect.stringContaining('Izbucul Tăuz')]);
  });

  it('adds the picked target under the name its row gave it, and saves it', async () => {
    show();
    await openEditorAndSearch();

    fireEvent.click(screen.getByRole('button', { name: /Izbucul Tăuz/ }));
    expect(screen.getByRole('button', { name: /Izbucul Tăuz/ })).toHaveAttribute('aria-pressed', 'true');
    chooseKind();
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));

    expect(within(screen.getByRole('dialog')).getByRole('cell', { name: 'Izbucul Tăuz' })).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    await waitFor(() => expect(setLinksSpy).toHaveBeenCalled());
    expect(setLinksSpy.mock.calls[0][0]).toEqual({
      id: FEATURE,
      links: [{ toId: OTHER, linkKindCode: 'connectsTo', note: null }],
    });
  });

  it('lets a picked target be withdrawn before it is added', async () => {
    show();
    await openEditorAndSearch();

    fireEvent.click(screen.getByRole('button', { name: /Izbucul Tăuz/ }));
    chooseKind();
    expect(screen.getByRole('button', { name: 'Add' })).toBeEnabled();

    fireEvent.click(screen.getByTestId('feature-link-target-picker-clear'));
    expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled();
  });

  it('picks a target from the keyboard alone', async () => {
    show();
    await openEditorAndSearch();

    fireEvent.keyDown(screen.getByTestId('feature-link-target-picker-input'), { key: 'ArrowDown' });
    const row = screen.getByRole('button', { name: /Izbucul Tăuz/ });
    expect(row).toHaveFocus();
    fireEvent.keyDown(row, { key: 'Enter' });

    expect(row).toHaveAttribute('aria-pressed', 'true');
  });
});
