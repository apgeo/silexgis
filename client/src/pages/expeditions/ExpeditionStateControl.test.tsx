// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ActivityState } from '../../api/hooks.ts';

const move = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useMoveExpedition: () => ({ mutateAsync: move, isPending: false, variables: undefined }),
}));

const { default: ExpeditionStateControl } = await import('./ExpeditionStateControl.tsx');

function show(state: ActivityState, canEdit = true) {
  return render(
    <App>
      <ExpeditionStateControl expeditionId="camp-1" state={state} visibility="cavingGroup" canEdit={canEdit} />
    </App>,
  );
}

// Anchored at the end: an antd icon labels itself, so each button's accessible name carries
// the icon's own word ahead of the button's.
const publishButton = () => screen.queryByRole('button', { name: /Publish$/ });
const floatButton = () => screen.queryByRole('button', { name: /Float it$/ });
const callOffButton = () => screen.queryByRole('button', { name: /Call it off$/ });
const draftButton = () => screen.queryByRole('button', { name: /Back to draft$/ });

beforeEach(() => {
  move.mockReset().mockResolvedValue({});
});
afterEach(cleanup);

describe('ExpeditionStateControl', () => {
  it('offers nothing to somebody who may not write the camp', () => {
    show('draft', false);
    expect(publishButton()).toBeNull();
    expect(floatButton()).toBeNull();
  });

  it('moves the camp through the one route that names the target state', async () => {
    show('draft');
    fireEvent.click(floatButton()!);
    await vi.waitFor(() => expect(move).toHaveBeenCalledWith({ id: 'camp-1', state: 'proposed' }));
  });

  it('asks before announcing, in words that promise no notification and name the audience', async () => {
    // Nothing notifies on a camp when it is announced. The trip's question says the people on
    // it will be told; a camp's must not, because a confirmation that promises what will not
    // happen is the worst kind of wrong.
    show('done');
    fireEvent.click(publishButton()!);
    const question = await screen.findByText(/Announce this camp\?/);
    expect(question.textContent).toContain('Nobody is notified');
    expect(question.textContent).toContain('your caving group');
    expect(move).not.toHaveBeenCalled();
  });

  it('offers a called-off camp only the way back to the workshop', () => {
    show('cancelled');
    expect(draftButton()).not.toBeNull();
    expect(callOffButton()).toBeNull();
    expect(publishButton()).toBeNull();
  });
});
