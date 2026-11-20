// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';

/**
 * The search the link picker runs for people, clubs and camps — the kinds the object selector does
 * not serve. Its failure is the one thing pinned here: a target search that did not land must not
 * read as "nothing by that name", or somebody concludes the caver does not exist.
 */

let answer: { data: unknown; isFetching: boolean; isError: boolean; error: unknown } = {
  data: undefined,
  isFetching: false,
  isError: false,
  error: null,
};
const refetch = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useResLinkTargets: () => ({ ...answer, refetch }),
}));
vi.mock('../../hooks/useDebouncedValue.ts', () => ({ useDebouncedValue: (value: string) => value }));
vi.mock('../selector/ObjectSelector.tsx', () => ({ default: () => null }));

const { default: ResLinkTargetPicker } = await import('./ResLinkTargetPicker.tsx');

afterEach(() => {
  cleanup();
  answer = { data: undefined, isFetching: false, isError: false, error: null };
  refetch.mockReset();
});

function show() {
  const onChange = vi.fn();
  render(<ResLinkTargetPicker targetType="caver" value={null} onChange={onChange} />);
  fireEvent.change(screen.getByRole('combobox'), { target: { value: 'Ion' } });
  return onChange;
}

describe('the link target search for people, clubs and camps', () => {
  it('lists what the server found', () => {
    answer = { ...answer, data: [{ id: 'caver-1', title: 'Ion A.', subtitle: null }] };
    show();
    expect(screen.getByText('Ion A.')).toBeTruthy();
    expect(screen.queryByTestId('reslink-target-search-failed')).toBeNull();
  });

  it('names a failed search with a way to ask again, instead of finding nothing', () => {
    answer = { ...answer, isError: true, error: new TypeError('Failed to fetch') };
    const onChange = show();

    const failed = screen.getByTestId('reslink-target-search-failed');
    expect(failed.textContent).toContain('could not be run just now');
    fireEvent.click(within(failed).getByRole('button', { name: 'Retry' }));
    expect(refetch).toHaveBeenCalledTimes(1);
    // Nothing was chosen, so nothing can be submitted.
    expect(onChange).toHaveBeenLastCalledWith(null, null);
  });

  it('words a refused search as a refusal, with nothing to try again', () => {
    answer = { ...answer, isError: true, error: new ApiError(403, 'auth.forbidden') };
    show();

    const failed = screen.getByTestId('reslink-target-search-failed');
    expect(failed).toHaveAttribute('data-refused', 'true');
    expect(within(failed).queryByRole('button', { name: 'Retry' })).toBeNull();
  });
});
