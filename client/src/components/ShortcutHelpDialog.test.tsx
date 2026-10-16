// SPDX-License-Identifier: AGPL-3.0-or-later
import { render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import i18n from '../i18n';
import { useShortcutRegistry } from '../hooks/shortcutRegistry.ts';
import ShortcutHelpDialog from './ShortcutHelpDialog.tsx';

afterEach(async () => {
  useShortcutRegistry.setState({ bound: {} });
  await i18n.changeLanguage('en');
});

describe('ShortcutHelpDialog', () => {
  it('lists the shortcuts the screen has bound, from the registry', async () => {
    await i18n.changeLanguage('en');
    useShortcutRegistry.getState().register('screen', [
      { key: 'Escape', labelKey: 'shortcuts.action.clearSelection' },
      { key: 'ArrowLeft', alt: true, labelKey: 'shortcuts.action.selectionBack' },
    ]);

    render(<ShortcutHelpDialog open onClose={vi.fn()} />);

    const dialog = await screen.findByRole('dialog', { name: 'Keyboard shortcuts' });
    const table = within(dialog).getByTestId('shortcut-help-screen');
    expect(within(table).getByRole('row', { name: /Esc.*Clear the selection/ })).toBeInTheDocument();
    expect(within(table).getByRole('row', { name: /Alt.*←.*Previous selection/ })).toBeInTheDocument();
  });

  it('lists the keys of a focused map as one line per action', async () => {
    await i18n.changeLanguage('en');
    render(<ShortcutHelpDialog open onClose={vi.fn()} />);

    const table = within(await screen.findByRole('dialog')).getByTestId('shortcut-help-map');
    // The four arrows are one action and one line.
    expect(within(table).getAllByRole('row', { name: /Move the map/ })).toHaveLength(1);
    expect(within(table).getByRole('row', { name: /Zoom in/ })).toBeInTheDocument();
  });

  it('speaks Romanian when the reader does', async () => {
    await i18n.changeLanguage('ro');
    useShortcutRegistry.getState().register('screen', [{ key: '?', labelKey: 'shortcuts.action.help' }]);

    render(<ShortcutHelpDialog open onClose={vi.fn()} />);

    expect(await screen.findByRole('dialog', { name: 'Scurtături de tastatură' })).toBeInTheDocument();
    expect(screen.getByText('Afișează lista scurtăturilor')).toBeInTheDocument();
  });
});
