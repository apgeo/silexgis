// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { afterEach, describe, expect, it } from 'vitest';
import '../../i18n';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import PanelDock from './PanelDock.tsx';

afterEach(() => {
  cleanup();
  act(() => useUiPrefsStore.getState().releaseAccount());
});

describe('the named layouts of the panel dock', () => {
  it('marks a layout kept in this browser only, and no layout the account holds', async () => {
    act(() => {
      useUiPrefsStore.getState().adoptAccount('account-a');
      useUiPrefsStore.getState().saveLayout('Kept here', false);
      useUiPrefsStore.getState().saveLayout('Held by the account', false);
    });
    const kept = useUiPrefsStore.getState().layouts.find((l) => l.name === 'Kept here')!;
    act(() => useUiPrefsStore.getState().setLayoutsKeptHere([kept.id]));

    render(
      <AntApp>
        <PanelDock scope="main" />
      </AntApp>,
    );
    fireEvent.click(screen.getByTestId('panel-layouts'));

    expect(await screen.findByText('Held by the account')).toBeTruthy();
    const marks = screen.getAllByTestId('layout-kept-here');
    expect(marks).toHaveLength(1);
    expect(marks[0]!.closest('li')).toHaveTextContent('Kept here');
  });
});
