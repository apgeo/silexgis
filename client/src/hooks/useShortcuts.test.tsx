// SPDX-License-Identifier: AGPL-3.0-or-later
import { render, fireEvent } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { useShortcutRegistry } from './shortcutRegistry.ts';
import { useShortcuts, type Shortcut } from './useShortcuts.ts';

function Screen({ shortcuts, enabled = true }: { shortcuts: Shortcut[]; enabled?: boolean }) {
  useShortcuts(shortcuts, enabled);
  return <input aria-label="name" />;
}

describe('useShortcuts', () => {
  it('runs a bound shortcut and registers what it does while mounted', () => {
    const run = vi.fn();
    const shortcuts: Shortcut[] = [{ key: '?', labelKey: 'shortcuts.action.help', run }];

    const { unmount } = render(<Screen shortcuts={shortcuts} />);
    fireEvent.keyDown(document.body, { key: '?', shiftKey: true });

    expect(run).toHaveBeenCalledTimes(1);
    const bound = Object.values(useShortcutRegistry.getState().bound).flat();
    expect(bound).toEqual([{ key: '?', labelKey: 'shortcuts.action.help' }]);

    unmount();
    // Gone with the screen: the help must never list a key that no longer does anything.
    expect(Object.values(useShortcutRegistry.getState().bound).flat()).toEqual([]);
  });

  it('lists nothing while disabled', () => {
    render(<Screen shortcuts={[{ key: '[', labelKey: 'x', run: vi.fn() }]} enabled={false} />);
    expect(Object.values(useShortcutRegistry.getState().bound).flat()).toEqual([]);
  });

  it('leaves the key to a field being typed in', () => {
    const run = vi.fn();
    const { getByLabelText } = render(<Screen shortcuts={[{ key: '?', labelKey: 'x', run }]} />);
    fireEvent.keyDown(getByLabelText('name'), { key: '?' });
    expect(run).not.toHaveBeenCalled();
  });
});
