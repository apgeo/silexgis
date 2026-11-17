// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import { useDiscardGuard } from './useDiscardGuard.ts';

function Closer({ dirty, discard }: { dirty: () => boolean; discard: () => void }) {
  const requestDiscard = useDiscardGuard();
  return (
    <button type="button" onClick={() => requestDiscard(dirty, discard)}>
      close
    </button>
  );
}

function show(dirty: () => boolean, discard: () => void) {
  render(
    <App>
      <Closer dirty={dirty} discard={discard} />
    </App>,
  );
}

afterEach(cleanup);

describe('useDiscardGuard', () => {
  it('reads whether there is unsaved work at the moment of closing', () => {
    let changed = false;
    const discard = vi.fn();
    show(() => changed, discard);
    fireEvent.click(screen.getByRole('button', { name: 'close' }));
    expect(discard).toHaveBeenCalledTimes(1);

    changed = true;
    fireEvent.click(screen.getByRole('button', { name: 'close' }));
    expect(discard).toHaveBeenCalledTimes(1);
  });

  it('asks one question however often closing is asked for, and discards once on yes', async () => {
    const discard = vi.fn();
    show(() => true, discard);
    fireEvent.click(screen.getByRole('button', { name: 'close' }));
    fireEvent.click(screen.getByRole('button', { name: 'close' }));
    await vi.waitFor(() => expect(screen.getAllByTestId('discard-confirm')).toHaveLength(1));
    fireEvent.click(screen.getByTestId('discard-confirm'));
    await vi.waitFor(() => expect(discard).toHaveBeenCalledTimes(1));
  });
});
