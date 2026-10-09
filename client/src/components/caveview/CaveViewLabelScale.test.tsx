// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import '../../i18n';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import CaveViewLabelScale from './CaveViewLabelScale.tsx';

beforeEach(() => {
  useUiPrefsStore.setState({ modelLabelScale: undefined });
});
afterEach(cleanup);

describe('the control for the size of station names', () => {
  it('offers the sizes as percentages, the viewer\'s own marked until another is chosen', async () => {
    render(<CaveViewLabelScale />);
    fireEvent.click(screen.getByTestId('caveview-label-scale'));

    const offered = await screen.findAllByRole('menuitem');
    expect(offered.map((item) => item.textContent)).toEqual(['40%', '60%', '80%', '100%', '125%', '150%']);
    expect(offered.find((item) => item.classList.contains('ant-dropdown-menu-item-selected'))?.textContent).toBe('100%');
  });

  it('keeps what was chosen as this person\'s size for every model', async () => {
    render(<CaveViewLabelScale />);
    fireEvent.click(screen.getByTestId('caveview-label-scale'));
    fireEvent.click(await screen.findByText('60%'));

    expect(useUiPrefsStore.getState().modelLabelScale).toBe(0.6);
  });

  it('reads a stored size nobody was offered as the viewer\'s own', async () => {
    useUiPrefsStore.setState({ modelLabelScale: 7 });
    render(<CaveViewLabelScale />);
    fireEvent.click(screen.getByTestId('caveview-label-scale'));

    const offered = await screen.findAllByRole('menuitem');
    expect(offered.find((item) => item.classList.contains('ant-dropdown-menu-item-selected'))?.textContent).toBe('100%');
  });
});
