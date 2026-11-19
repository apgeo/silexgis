// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import '../../../i18n';
import { useWorkspaceStore } from '../../../stores/workspaceStore.ts';
import BottomTable from './BottomTable.tsx';

let summary: { data?: { items: unknown[] }; isError: boolean; error: unknown };
const refetch = vi.fn();
vi.mock('../../../api/hooks/workingSet.ts', () => ({
  useSelectionSummary: () => ({ ...summary, isFetching: false, refetch }),
}));
vi.mock('../FeatureListPanel.tsx', () => ({ default: () => null }));

beforeEach(() => {
  refetch.mockReset();
  useWorkspaceStore.getState().setSelectionSet([{ kind: 'feature', id: 'f-1' }]);
});

afterEach(() => {
  cleanup();
  useWorkspaceStore.getState().clearSelectionSet();
});

it('says the picked objects could not be read, instead of an empty table', () => {
  summary = { data: undefined, isError: true, error: new TypeError('Failed to fetch') };
  render(<BottomTable maximised={false} onToggleMaximised={() => undefined} onClose={() => undefined} />);
  fireEvent.click(screen.getByRole('tab', { name: /Picked/ }));

  expect(screen.getByTestId('picked-table-load-failed')).toBeTruthy();
  expect(screen.queryByRole('table')).toBeNull();
  fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
  expect(refetch).toHaveBeenCalledTimes(1);
});
