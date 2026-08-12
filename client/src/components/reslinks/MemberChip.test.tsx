// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ResLinkMember } from '../../api/hooks.ts';
import MemberChip from './MemberChip.tsx';

afterEach(cleanup);

/** A chip naming a feature, which is the case that carries a route to somewhere else. */
function featureMember(): ResLinkMember {
  return {
    id: 'member-1',
    targetType: 'feature',
    targetId: 'feature-1',
    isMain: false,
    display: { title: 'Falia Demo', subtitle: null, path: null, thumbnailUrl: null },
  } as unknown as ResLinkMember;
}

/**
 * Renders the chip inside a router that shows where it ended up, so a navigation the chip
 * did not intend is visible rather than merely absent from an assertion.
 */
function renderChip(onRemove?: () => void) {
  return render(
    <MemoryRouter initialEntries={['/trip-logs/trip-1']}>
      <Routes>
        <Route path="/trip-logs/:id" element={<MemberChip member={featureMember()} onRemove={onRemove} />} />
        <Route path="/features/:id" element={<div>the feature's own page</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

it('striking a membership out asks, and does not take the reader to the thing being unnamed', async () => {
  const onRemove = vi.fn();
  renderChip(onRemove);

  fireEvent.click(screen.getByLabelText('Remove from link'));

  // The confirmation is the whole point of the control: nothing is removed by the first click.
  expect(await screen.findByText('Remove this item from the link?')).toBeTruthy();
  expect(onRemove).not.toHaveBeenCalled();

  // And the reader is still where they were. The chip is a link to its target and the close
  // icon sits inside it, so a click that is allowed to bubble navigates — which both loses the
  // page the reader was working on and leaves them on the target's own page, holding a delete
  // button that means something else entirely.
  expect(screen.queryByText("the feature's own page")).toBeNull();
});

it('confirming removes the membership and still does not navigate', async () => {
  const onRemove = vi.fn();
  renderChip(onRemove);

  fireEvent.click(screen.getByLabelText('Remove from link'));
  fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

  expect(onRemove).toHaveBeenCalledTimes(1);
  expect(screen.queryByText("the feature's own page")).toBeNull();
});

it('a chip nobody may amend offers no close icon at all', () => {
  renderChip(undefined);

  expect(screen.queryByLabelText('Remove from link')).toBeNull();
  expect(screen.getByText('Falia Demo')).toBeTruthy();
});
