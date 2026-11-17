// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';

/**
 * The map's "new entrance here" tool: a cave is picked first, then the entrance editor opens for
 * that cave at the clicked spot. Every name and identifier below is invented.
 */

const CAVE = '55555555-5555-4555-8555-555555555555';

const NEW_CAVE = '66666666-6666-4666-8666-666666666666';

const { queryCalls, createCave, createEntrance, setSelection } = vi.hoisted(() => ({
  queryCalls: [] as { body: unknown; enabled: boolean }[],
  createCave: vi.fn(),
  createEntrance: vi.fn(),
  setSelection: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  createEntranceFor: createEntrance,
  useCaveTypes: () => ({ data: [{ id: 3, name: 'Peșteră test' }] }),
  useEntranceTypes: () => ({ data: [{ id: 4, name: 'Gură test' }] }),
  useCreateCave: () => ({ mutateAsync: createCave }),
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
    select({ setSelection }),
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
  createCave.mockReset().mockResolvedValue({ id: NEW_CAVE });
  createEntrance.mockReset().mockResolvedValue({});
  setSelection.mockReset();
});

async function choose(label: string, option: string) {
  fireEvent.mouseDown(screen.getByLabelText(label));
  fireEvent.click(await screen.findByTitle(option));
}

async function fillNewCave() {
  fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Avenul de test' } });
  await choose('Type', 'Peșteră test');
  await choose('Entrance type', 'Gură test');
}

describe('a new cave placed on the map', () => {
  it('creates the cave, then its main entrance at the clicked spot', async () => {
    const onClose = vi.fn();
    render(
      <App>
        <CaveAddModal mode="add-cave" lonLat={[23.5, 46.25]} onClose={onClose} />
      </App>,
    );
    await fillNewCave();
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(createCave).toHaveBeenCalledTimes(1);
    expect(createCave.mock.calls[0][0]).toMatchObject({
      name: 'Avenul de test',
      caveTypeId: 3,
      visibility: 'private',
      locationProtected: false,
    });
    expect(createEntrance).toHaveBeenCalledWith(NEW_CAVE, expect.objectContaining({
      entranceTypeId: 4,
      isMain: true,
      geom: { type: 'Point', coordinates: [23.5, 46.25] },
    }));
    expect(setSelection).toHaveBeenCalledWith({ kind: 'cave', caveId: NEW_CAVE });
  });

  it('says the cave was made but its entrance was not, and a retry adds only the entrance', async () => {
    createEntrance.mockRejectedValueOnce(new ApiError(503, undefined));
    const onClose = vi.fn();
    render(
      <App>
        <CaveAddModal mode="add-cave" lonLat={[23.5, 46.25]} onClose={onClose} />
      </App>,
    );
    await fillNewCave();
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    expect(
      (await screen.findAllByText(/The cave was created but its entrance was not/)).length,
    ).toBeGreaterThan(0);
    expect(onClose).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    await waitFor(() => expect(onClose).toHaveBeenCalled());

    // One cave, never a second: the retry went to the cave that already exists.
    expect(createCave).toHaveBeenCalledTimes(1);
    expect(createEntrance).toHaveBeenCalledTimes(2);
    expect(createEntrance.mock.calls[1][0]).toBe(NEW_CAVE);
  });

  // The dialog can be docked beside a map that stays usable, so a new placement can arrive while
  // the retry for an earlier one is still on screen. It must open as a new cave of its own, never
  // put its point on the earlier cave as that cave's entrance.
  it.each([
    ['another spot', (rerender: (ui: React.ReactElement) => void) =>
      rerender(<App><CaveAddModal mode="add-cave" lonLat={[24, 47]} onClose={vi.fn()} /></App>)],
    ['the entrance tool and back', (rerender: (ui: React.ReactElement) => void) => {
      rerender(<App><CaveAddModal mode="add-entrance" lonLat={[24, 47]} onClose={vi.fn()} /></App>);
      rerender(<App><CaveAddModal mode="add-cave" lonLat={[24.5, 47.5]} onClose={vi.fn()} /></App>);
    }],
  ])('starts a new placement after %s as a new cave, leaving the earlier one on its sheet', async (_, move) => {
    createEntrance.mockRejectedValueOnce(new ApiError(503, undefined));
    const { rerender } = render(
      <App>
        <CaveAddModal mode="add-cave" lonLat={[23.5, 46.25]} onClose={vi.fn()} />
      </App>,
    );
    await fillNewCave();
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    expect(
      (await screen.findAllByText(/The cave was created but its entrance was not/)).length,
    ).toBeGreaterThan(0);

    move(rerender);

    await waitFor(() => expect(setSelection).toHaveBeenCalledWith({ kind: 'cave', caveId: NEW_CAVE }));
    expect(screen.queryByText(/The cave was created but its entrance was not/, { selector: '.ant-alert *' })).toBeNull();
    expect(screen.getByLabelText('Name')).not.toBeDisabled();

    createCave.mockResolvedValueOnce({ id: CAVE });
    await fillNewCave();
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(createCave).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(createEntrance).toHaveBeenCalledTimes(2));
    expect(createEntrance.mock.calls[1][0]).toBe(CAVE);
    expect(createEntrance.mock.calls[1][1].geom.coordinates).not.toEqual([23.5, 46.25]);
  });

  it('names why the cave itself was refused rather than a bare failed save', async () => {
    createCave.mockRejectedValueOnce(new ApiError(400, 'cave.type_unknown'));
    render(
      <App>
        <CaveAddModal mode="add-cave" lonLat={[23.5, 46.25]} onClose={vi.fn()} />
      </App>,
    );
    await fillNewCave();
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(createCave).toHaveBeenCalledTimes(1));
    expect(createEntrance).not.toHaveBeenCalled();
    expect(await screen.findByText(i18n.t('caves.problems.typeUnknown'))).toBeTruthy();
    expect(screen.queryByText(i18n.t('common.saveFailed'))).toBeNull();
  });
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
