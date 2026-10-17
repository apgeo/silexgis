// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

/**
 * The map's "new entrance here" tool: a cave is picked first, then the entrance editor opens for
 * that cave at the clicked spot. Every name and identifier below is invented.
 */

const CAVE = '55555555-5555-4555-8555-555555555555';

const { queryCalls } = vi.hoisted(() => ({
  queryCalls: [] as { body: unknown; enabled: boolean }[],
}));

vi.mock('../../api/hooks.ts', () => ({
  createEntranceFor: vi.fn(),
  useCaveTypes: () => ({ data: [] }),
  useEntranceTypes: () => ({ data: [] }),
  useCreateCave: () => ({ mutateAsync: vi.fn() }),
  useFilterVocabulary: () => ({
    data: { worlds: [{ world: 'feature', labelKey: 'x', fields: [], sorts: [] }] },
  }),
  useFilterQuery: (body: unknown, enabled: boolean) => {
    queryCalls.push({ body, enabled });
    return enabled
      ? {
          data: {
            worlds: [{
              world: 'feature',
              hits: [{ world: 'feature', id: CAVE, title: 'Avenul Nou', subtitle: 'Cave', symbol: null, placeable: true }],
              total: null,
            }],
            page: 1, pageSize: 8, counted: false,
          },
          isFetching: false,
        }
      : { data: undefined, isFetching: false };
  },
  useFilterResolve: () => ({ data: [] }),
}));

vi.mock('../../map/entranceLayer.ts', () => ({ reloadEntrances: vi.fn() }));
vi.mock('../../stores/workspaceStore.ts', () => ({
  useWorkspaceStore: (select: (state: { setSelection: unknown }) => unknown) =>
    select({ setSelection: vi.fn() }),
}));
// The editor is a dialog of its own with its own tests; here it only has to say which cave and
// which spot it was opened for.
vi.mock('../caves/EntranceEditorModal.tsx', () => ({
  default: ({ caveId, defaultCenter }: { caveId: string; defaultCenter: [number, number] }) => (
    <div data-testid="entrance-editor">{`${caveId} @ ${defaultCenter.join(',')}`}</div>
  ),
}));

const { default: CaveAddModal } = await import('./CaveAddModal.tsx');

function show(mode: 'add-entrance' | null = 'add-entrance') {
  return render(
    <App>
      <CaveAddModal mode={mode} lonLat={[23.5, 46.25]} onClose={() => {}} />
    </App>,
  );
}

async function search() {
  fireEvent.change(screen.getByTestId('new-entrance-cave-picker-input'), { target: { value: 'av' } });
  // The search is debounced; a loaded machine can take longer than the default second to answer.
  return screen.findByRole('button', { name: /Avenul Nou/ }, { timeout: 3000 });
}

afterEach(cleanup);
beforeEach(() => {
  queryCalls.length = 0;
});

describe('the new-entrance cave picker', () => {
  it('asks for caves alone, since only a cave can receive an entrance', async () => {
    show();
    await search();

    const document = JSON.stringify(queryCalls.filter((c) => c.enabled).at(-1)!.body);
    expect(document).toContain('"feature"');
    expect(document).toContain('"kind"');
    expect(document).toContain('"cave"');
    expect(screen.queryByTestId('entrance-editor')).toBeNull();
  });

  it('opens the entrance editor for the picked cave at the clicked spot', async () => {
    show();
    fireEvent.click(await search());

    expect(screen.getByTestId('entrance-editor')).toHaveTextContent(`${CAVE} @ 23.5,46.25`);
  });

  it('picks the cave from the keyboard alone', async () => {
    show();
    const row = await search();

    fireEvent.keyDown(screen.getByTestId('new-entrance-cave-picker-input'), { key: 'ArrowDown' });
    expect(row).toHaveFocus();
    fireEvent.keyDown(row, { key: ' ' });

    expect(screen.getByTestId('entrance-editor')).toHaveTextContent(CAVE);
  });

  it('starts over with nothing picked once the tool is put down', async () => {
    const { rerender } = show();
    fireEvent.click(await search());
    expect(screen.getByTestId('entrance-editor')).toBeTruthy();

    rerender(
      <App>
        <CaveAddModal mode={null} lonLat={null} onClose={() => {}} />
      </App>,
    );
    rerender(
      <App>
        <CaveAddModal mode="add-entrance" lonLat={[23.5, 46.25]} onClose={() => {}} />
      </App>,
    );

    expect(screen.queryByTestId('entrance-editor')).toBeNull();
    expect(screen.getByTestId('new-entrance-cave-picker-input')).toHaveValue('');
  });
});
