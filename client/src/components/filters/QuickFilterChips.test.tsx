// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';

const ready = '0e6f2c4a-5b1d-4c39-9b7e-2f8a1d3c4e5f';
const unreadable = '1e6f2c4a-5b1d-4c39-9b7e-2f8a1d3c4e5f';
const gone = '2e6f2c4a-5b1d-4c39-9b7e-2f8a1d3c4e5f';
const winter = { id: ready, name: 'Winter leads', state: 'ready', document: { version: 1, scope: [] } };

vi.mock('../../api/hooks/savedFilters.ts', () => ({
  useSavedFilters: () => ({
    data: [winter, { id: unreadable, name: 'Old question', state: 'unreadable', document: null }],
  }),
  documentOf: (filter: { document: unknown }) => filter.document,
}));

const { default: QuickFilterChips } = await import('./QuickFilterChips.tsx');
const { quickChipStatus } = await import('./quickChipStatus.ts');

beforeEach(() => {
  useUiPrefsStore.setState({ pinnedFilterIds: [] });
});
afterEach(cleanup);

const noop = () => undefined;

describe('quickChipStatus', () => {
  it('tells a filter that is gone from one that can no longer be run', () => {
    const list = [winter, { id: unreadable, name: 'x', state: 'unreadable', document: null }] as never;
    expect(quickChipStatus(ready.toUpperCase(), list)).toBe('ready');
    expect(quickChipStatus(unreadable, list)).toBe('unreadable');
    expect(quickChipStatus(gone, list)).toBe('gone');
    expect(quickChipStatus(ready, undefined)).toBe('loading');
  });
});

describe('QuickFilterChips', () => {
  it('draws nothing while nothing is pinned', () => {
    const { container } = render(<QuickFilterChips onApply={noop} onRelease={noop} />);
    expect(container.firstChild).toBeNull();
  });

  it('applies a pinned filter on one click and lifts it on the next', () => {
    useUiPrefsStore.setState({ pinnedFilterIds: [ready] });
    const onApply = vi.fn();
    const onRelease = vi.fn();
    const { rerender } = render(<QuickFilterChips onApply={onApply} onRelease={onRelease} />);

    const chip = screen.getByRole('button', { name: 'Winter leads' });
    expect(chip.getAttribute('aria-pressed')).toBe('false');
    expect(screen.getByRole('group', { name: 'Pinned saved filters' })).toBeTruthy();
    fireEvent.click(chip);
    expect(onApply).toHaveBeenCalledWith(winter);

    rerender(<QuickFilterChips activeId={ready.toUpperCase()} onApply={onApply} onRelease={onRelease} />);
    expect(screen.getByRole('button', { name: 'Winter leads' }).getAttribute('aria-pressed')).toBe('true');
    fireEvent.click(screen.getByRole('button', { name: 'Winter leads' }));
    expect(onRelease).toHaveBeenCalledTimes(1);
  });

  it('shows a gone or unreadable pin as such and never applies it', () => {
    useUiPrefsStore.setState({ pinnedFilterIds: [unreadable, gone] });
    const onApply = vi.fn();
    render(<QuickFilterChips onApply={onApply} onRelease={noop} />);

    const old = screen.getByRole('button', { name: 'Old question (cannot be run)' }) as HTMLButtonElement;
    const missing = screen.getByRole('button', { name: 'A pinned filter that is no longer there' }) as HTMLButtonElement;
    expect(old.disabled).toBe(true);
    expect(missing.disabled).toBe(true);
    fireEvent.click(old);
    fireEvent.click(missing);
    expect(onApply).not.toHaveBeenCalled();
    // Each can still be unpinned, by a button that names what it unpins.
    expect(screen.getByRole('button', { name: 'Unpin A pinned filter that is no longer there' })).toBeTruthy();
  });

  it('unpins from the chip and hands focus to the chip that took its place', async () => {
    useUiPrefsStore.setState({ pinnedFilterIds: [unreadable, ready] });
    render(<QuickFilterChips onApply={noop} onRelease={noop} />);

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Unpin Old question' }));
    });
    expect(useUiPrefsStore.getState().pinnedFilterIds).toEqual([ready]);
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Winter leads' }));

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Unpin Winter leads' }));
    });
    expect(useUiPrefsStore.getState().pinnedFilterIds).toEqual([]);
    // The last one gone: the row stays for the moment with a line saying so, and focus lands on it.
    expect(document.activeElement?.textContent).toContain('No pinned filters');
  });
});
