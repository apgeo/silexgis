// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import '../i18n';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';
import LayoutKeptHereMark from './LayoutKeptHereMark.tsx';

afterEach(() => {
  cleanup();
  act(() => useUiPrefsStore.getState().releaseAccount());
});

describe('the mark on an arrangement kept in this browser only', () => {
  it('is drawn for a layout the account could not hold, and for no other', () => {
    act(() => useUiPrefsStore.getState().setLayoutsKeptHere(['kept']));
    render(
      <>
        <LayoutKeptHereMark layoutId="kept" />
        <LayoutKeptHereMark layoutId="held" />
      </>,
    );
    expect(screen.getAllByTestId('layout-kept-here')).toHaveLength(1);
    expect(screen.getByText('This browser only')).toBeTruthy();
  });

  it('goes when another member claims the browser', () => {
    act(() => {
      useUiPrefsStore.getState().adoptAccount('account-a');
      useUiPrefsStore.getState().setLayoutsKeptHere(['kept']);
    });
    render(<LayoutKeptHereMark layoutId="kept" />);
    expect(screen.queryByTestId('layout-kept-here')).not.toBeNull();
    act(() => {
      useUiPrefsStore.getState().adoptAccount('account-b');
    });
    expect(screen.queryByTestId('layout-kept-here')).toBeNull();
  });
});
