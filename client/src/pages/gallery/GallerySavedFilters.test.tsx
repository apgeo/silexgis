// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type { FilterNode } from '../../filters/types.ts';
import { afterRejectionsAreReported } from '../../testEventLoop.ts';

const createSpy = vi.hoisted(() => vi.fn());

vi.mock('../../api/hooks/access.ts', () => ({
  useCapabilities: () => ({ data: {}, isLoading: false }),
  useCreateDoor: () => ({ canCreate: true, unbound: false, cavingGroups: [] }),
}));
vi.mock('../../api/hooks/people.ts', () => ({ useCavingGroups: () => ({ data: [] }) }));
vi.mock('../../api/hooks/savedFilters.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks/savedFilters.ts')>(
    '../../api/hooks/savedFilters.ts',
  );
  return {
    documentOf: actual.documentOf,
    useSavedFilters: () => ({ data: [], isSuccess: true }),
    useCreateSavedFilter: () => ({ mutateAsync: createSpy, isPending: false }),
  };
});

const { default: GallerySavedFilters } = await import('./GallerySavedFilters.tsx');

const TITLED: FilterNode = {
  node: 'condition',
  field: 'title',
  op: 'contains',
  values: [{ type: 'text', value: 'entrance' }],
};

// Recorded here rather than left to the runner, so the test that causes one is the test that fails.
const unhandled: unknown[] = [];
const record = (reason: unknown) => unhandled.push(reason);

beforeEach(async () => {
  await i18n.changeLanguage('en');
  createSpy.mockReset();
  unhandled.length = 0;
  process.on('unhandledRejection', record);
});

afterEach(() => {
  cleanup();
  process.off('unhandledRejection', record);
});

it('Save with no name marks the field, sends nothing and leaves no unhandled rejection', async () => {
  render(
    <AntApp>
      <MemoryRouter initialEntries={['/gallery']}>
        <GallerySavedFilters where={TITLED} onApply={vi.fn()} onSaved={vi.fn()} onForget={vi.fn()} />
      </MemoryRouter>
    </AntApp>,
  );
  fireEvent.click(screen.getByTestId('gallery-save-filter'));
  const name = await screen.findByTestId('gallery-saved-filter-name');
  const dialog = name.closest('.ant-modal') as HTMLElement;
  fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));

  await waitFor(() => expect(name.getAttribute('aria-invalid')).toBe('true'));
  await afterRejectionsAreReported();
  expect(createSpy).not.toHaveBeenCalled();
  expect(unhandled).toEqual([]);
});
