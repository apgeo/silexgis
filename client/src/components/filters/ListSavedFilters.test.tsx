// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { emptyDocument, withWhere } from '../../filters/document.ts';
import type { FilterNode } from '../../filters/types.ts';

const gpx: FilterNode = { node: 'condition', field: 'format', op: 'equals', values: [{ type: 'id', value: 'gpx' }] };
const createSpy = vi.fn();

vi.mock('../../api/hooks/savedFilters.ts', () => ({
  useSavedFilters: () => ({ isSuccess: true, data: [] }),
  useCreateSavedFilter: () => ({ mutateAsync: createSpy, isPending: false }),
  documentOf: (f: { document: unknown }) => f.document ?? null,
}));
vi.mock('./QuickFilterChips.tsx', () => ({ default: () => null }));

const { default: ListSavedFilters } = await import('./ListSavedFilters.tsx');

// Recorded here rather than left to the runner, so the test that causes one is the test that fails.
const unhandled: unknown[] = [];
const record = (reason: unknown) => unhandled.push(reason);

beforeEach(() => {
  createSpy.mockReset();
  unhandled.length = 0;
  process.on('unhandledRejection', record);
});

afterEach(() => {
  cleanup();
  process.off('unhandledRejection', record);
});

function mount() {
  const document = withWhere(emptyDocument(['geofile']), 'geofile', gpx);
  const onSaved = vi.fn();
  render(
    <MemoryRouter>
      <App>
        <ListSavedFilters
          reading={{ extra: [] }}
          document={document}
          newestDocument={() => document}
          read={() => ({ extra: [] })}
          same={() => true}
          testIdPrefix="list"
          notAboutThisListKey="listFilters.notAboutThisList"
          onApply={vi.fn()}
          onSaved={onSaved}
          onClearExtra={vi.fn()}
          onForget={vi.fn()}
          onRelease={vi.fn()}
        />
      </App>
    </MemoryRouter>,
  );
  return onSaved;
}

it('Save with no name marks the field, sends nothing and leaves no unhandled rejection', async () => {
  mount();
  fireEvent.click(screen.getByTestId('list-save-filter'));
  fireEvent.click(await screen.findByRole('button', { name: i18n.t('common.save') }));

  const name = screen.getByTestId('saved-filter-name');
  await waitFor(() => expect(name.getAttribute('aria-invalid')).toBe('true'));
  // Long enough for a rejection with no handler to be reported.
  await new Promise((resolve) => setTimeout(resolve, 50));
  expect(createSpy).not.toHaveBeenCalled();
  expect(unhandled).toEqual([]);
});

it('Save with a name sends the conditions as a private filter', async () => {
  createSpy.mockResolvedValue({ id: 'f-1', name: 'Tracks' });
  const onSaved = mount();
  fireEvent.click(screen.getByTestId('list-save-filter'));
  fireEvent.change(await screen.findByTestId('saved-filter-name'), { target: { value: '  Tracks ' } });
  fireEvent.click(screen.getByRole('button', { name: i18n.t('common.save') }));

  await waitFor(() => expect(onSaved).toHaveBeenCalledWith('f-1'));
  expect(createSpy).toHaveBeenCalledWith(expect.objectContaining({ name: 'Tracks', visibility: 'private' }));
});
