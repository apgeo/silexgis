// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import '../../i18n';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';
import MultiSelectionCard from './MultiSelectionCard.tsx';

let summary: { data?: { items: unknown[] }; isError: boolean; error: unknown };
const refetch = vi.fn();
vi.mock('../../api/hooks/workingSet.ts', () => ({
  useSelectionSummary: () => ({ ...summary, refetch }),
}));
vi.mock('../../api/hooks/savedFilters.ts', () => ({
  useCreateSavedFilter: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

beforeEach(() => {
  refetch.mockReset();
  useWorkspaceStore.getState().setSelectionSet([
    { kind: 'feature', id: 'f-1' },
    { kind: 'feature', id: 'f-2' },
  ]);
});

afterEach(() => {
  cleanup();
  useWorkspaceStore.getState().clearSelectionSet();
});

function renderCard() {
  return render(
    <App>
      <MultiSelectionCard />
    </App>,
  );
}

it('does not call a failed read "0 readable, nothing readable"', () => {
  summary = { data: undefined, isError: true, error: new TypeError('Failed to fetch') };
  renderCard();

  expect(screen.queryByText('None of the selected objects can be read here.')).toBeNull();
  expect(screen.getByText('What the picked objects have in common could not be read.')).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
  expect(refetch).toHaveBeenCalledTimes(1);
});

it('still says nothing is readable when the read answered with nothing', () => {
  summary = { data: { items: [] }, isError: false, error: null };
  renderCard();

  expect(screen.getByText('None of the selected objects can be read here.')).toBeTruthy();
  expect(screen.queryByTestId('multi-selection-load-failed')).toBeNull();
});
