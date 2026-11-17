// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CaveWrite } from '../../api/hooks.ts';
import { ApiError } from '../../api/client.ts';
import { noteVersionOfReading } from '../../api/readingVersion.ts';

const updateCave = vi.fn();
const createCave = vi.fn();

/** The cave as the page's read of it answers at this moment; reassigned to stand for a re-read. */
let caveOnScreen: Record<string, unknown>;

// The newest version the transport has filed for the cave at this moment. It can be ahead of the
// copy the page holds: a save files its own version before the page's re-read of the cave lands.
const versionOnFile = vi.fn<(path: string) => string | undefined>(() => undefined);

vi.mock('../../api/client.ts', async (original) => ({
  ...(await original<typeof import('../../api/client.ts')>()),
  lastReadETag: (path: string) => versionOnFile(path),
}));

vi.mock('../../api/hooks.ts', () => ({
  useCave: () => ({ data: caveOnScreen }),
  useCaveTypes: () => ({ data: [{ id: 1, name: 'Cave' }] }),
  useRockTypes: () => ({ data: [] }),
  useFeatureTypes: () => ({ data: [] }),
  useCreateCave: () => ({ mutateAsync: createCave, isPending: false }),
  useUpdateCave: () => ({ mutateAsync: updateCave, isPending: false }),
}));

const { default: CaveFormPage } = await import('./CaveFormPage.tsx');

const path = '/api/v1/caves/cave-1';
const onFile = (version: string) =>
  versionOnFile.mockImplementation((asked) => (asked === path ? version : undefined));

function aCave(overrides: Record<string, unknown> = {}) {
  return {
    id: 'cave-1',
    name: 'Peștera de Test',
    caveTypeId: 1,
    description: null,
    properties: {},
    parents: [],
    cavingGroupId: null,
    visibility: 'public',
    locationProtected: false,
    isShowCave: false,
    explorationStatus: 'unknown',
    ...overrides,
  };
}

/** A cave as a read answered at `version` carried it. */
function aCaveAt(version: string, overrides: Record<string, unknown> = {}) {
  const cave = aCave(overrides);
  noteVersionOfReading(cave, new Response(null, { headers: { ETag: version } }));
  return cave;
}

function page() {
  return (
    <MemoryRouter initialEntries={['/caves/cave-1/edit']}>
      <App>
        <Routes>
          <Route path="/caves/:id/edit" element={<CaveFormPage />} />
          <Route path="/caves/:id" element={<div data-testid="cave-sheet" />} />
        </Routes>
      </App>
    </MemoryRouter>
  );
}

const nameInput = (container: HTMLElement) => container.querySelector('#name') as HTMLInputElement;

beforeEach(() => {
  updateCave.mockReset().mockResolvedValue({ id: 'cave-1' });
  createCave.mockReset();
  versionOnFile.mockReset().mockReturnValue(undefined);
  caveOnScreen = aCave();
});
afterEach(cleanup);

/**
 * The page reads the cave again under the form — on a return to the browser tab, after an entrance
 * is edited elsewhere — and each read is a new object with a newer version filed beside it.
 */
describe('editing a cave the page reads again meanwhile', () => {
  it('keeps what somebody typed when the cave is read again with a change in it', async () => {
    onFile('"1"');
    const view = render(page());
    await waitFor(() => expect(nameInput(view.container)).toHaveValue('Peștera de Test'));

    fireEvent.change(screen.getByLabelText('Description'), {
      target: { value: 'A long description somebody is part way through' },
    });

    onFile('"2"');
    caveOnScreen = aCave({ name: 'Renamed by somebody else' });
    view.rerender(page());

    expect(screen.getByLabelText('Description')).toHaveValue(
      'A long description somebody is part way through',
    );
    expect(nameInput(view.container)).toHaveValue('Peștera de Test');
  });

  it('saves against the version the form was filled from, so a change made meanwhile is refused', async () => {
    onFile('"1"');
    caveOnScreen = aCaveAt('"1"');
    const view = render(page());
    await waitFor(() => expect(nameInput(view.container)).toHaveValue('Peștera de Test'));

    onFile('"2"');
    caveOnScreen = aCaveAt('"2"', { name: 'Renamed by somebody else' });
    view.rerender(page());

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(updateCave).toHaveBeenCalledTimes(1));
    const sent = updateCave.mock.calls[0][0] as { body: CaveWrite; ifMatch?: string };
    expect(sent.ifMatch).toBe('"1"');
    expect(sent.body.name).toBe('Peștera de Test');
  });

  // A save files the version it produced as soon as it answers, while the page still holds the cave
  // from before it until its re-read lands. A form opened in that gap is filled from the older copy;
  // saved under the newer version, its older values would be taken over the save that came first.
  it('saves under the version of the copy it was filled from, though a newer one is on file', async () => {
    caveOnScreen = aCaveAt('"1"');
    onFile('"2"');
    const view = render(page());
    await waitFor(() => expect(nameInput(view.container)).toHaveValue('Peștera de Test'));

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(updateCave).toHaveBeenCalledTimes(1));
    const sent = updateCave.mock.calls[0][0] as { body: CaveWrite; ifMatch?: string };
    expect(sent.ifMatch).toBe('"1"');
  });

  it('leaves a stale-version refusal to the one conflict warning and stays on the form', async () => {
    updateCave.mockRejectedValue(new ApiError(412, 'concurrency.version_mismatch'));
    const view = render(page());
    await waitFor(() => expect(nameInput(view.container)).toHaveValue('Peștera de Test'));

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(updateCave).toHaveBeenCalledTimes(1));

    // A second press refused for another reason draws its own sentence. Messages are drawn in the
    // order they were asked for, so once that one is on screen anything the stale refusal had
    // asked for would be too — no wait on the clock is needed to see that it asked for nothing.
    updateCave.mockRejectedValue(new ApiError(403, 'forbidden'));
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText(FORBIDDEN)).toBeInTheDocument();
    expect(screen.queryByText(SAVE_FAILED)).toBeNull();
    expect(screen.queryByText(CONFLICT)).toBeNull();
    expect(screen.queryByTestId('cave-sheet')).toBeNull();
  });
});

const SAVE_FAILED = 'The operation failed. Please try again.';
const FORBIDDEN = 'You are not allowed to make that change.';
const CONFLICT = 'Someone else changed this since you loaded it. Reload and reapply your changes.';

describe('what a cave save sends', () => {
  it('sends the form with the cave’s own fields, parent and group carried over unchanged', async () => {
    caveOnScreen = aCave({
      properties: { clubCode: 'K-12', notOnThisForm: { kept: true } },
      parents: [
        { id: 'karst-area', isPrimary: false },
        { id: 'massif', isPrimary: true },
      ],
      cavingGroupId: 'group-1',
      visibility: 'internal',
      locationProtected: true,
    });
    const view = render(page());
    await waitFor(() => expect(nameInput(view.container)).toHaveValue('Peștera de Test'));
    fireEvent.change(nameInput(view.container), { target: { value: 'Peștera Nouă' } });

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(updateCave).toHaveBeenCalledTimes(1));
    const { body } = updateCave.mock.calls[0][0] as { body: CaveWrite };
    expect(body).toMatchObject({
      name: 'Peștera Nouă',
      caveTypeId: 1,
      visibility: 'internal',
      locationProtected: true,
      isShowCave: false,
      explorationStatus: 'unknown',
      cavingGroupId: 'group-1',
      parentId: 'massif',
      properties: { clubCode: 'K-12', notOnThisForm: { kept: true } },
    });
    expect(await screen.findByTestId('cave-sheet')).toBeInTheDocument();
  });

  it.each([
    ['refused for want of the right', new ApiError(403, 'forbidden'), FORBIDDEN],
    ['cut off by a lost connection', new TypeError('Failed to fetch'), SAVE_FAILED],
  ])('names a save %s and stays on the form with what was typed', async (_label, failure, words) => {
    updateCave.mockRejectedValue(failure);
    const view = render(page());
    await waitFor(() => expect(nameInput(view.container)).toHaveValue('Peștera de Test'));
    fireEvent.change(nameInput(view.container), { target: { value: 'Peștera Nouă' } });

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText(words)).toBeInTheDocument();
    expect(screen.queryByTestId('cave-sheet')).toBeNull();
    expect(nameInput(view.container)).toHaveValue('Peștera Nouă');
  });
});
