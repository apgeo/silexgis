// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
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
 * The delivery URL a resolved photograph arrives with. Written out in full rather than built,
 * because the assertions below are about these exact bytes surviving: the token in it signs how
 * far this reader may reach, and a chip that rebuilt the URL instead of spending the one it was
 * handed would be taking a decision the server had already taken.
 */
const pictureUrl =
  '/api/v1/files/8f6a1c2e-0000-4000-8000-000000000001/thumbnail?size=480&token=derivatives-only-token';

/**
 * A chip naming a photograph the server resolved a picture for. This is the shape every
 * picture-showing surface depends on, and the one that was never asserted: the strip and the
 * hover preview are both driven by `display.thumbnailUrl`, so a fixture that leaves it null
 * proves only that the empty case is handled.
 */
function pictureMember(): ResLinkMember {
  return {
    id: 'member-2',
    targetType: 'document',
    targetId: 'document-1',
    isMain: false,
    display: {
      title: 'Intrarea, spre lumină',
      subtitle: null,
      path: null,
      mediaType: 'image/jpeg',
      thumbnailUrl: pictureUrl,
    },
  } as unknown as ResLinkMember;
}

/** Opens a hover preview and lets antd's open delay elapse; jsdom runs no animation. */
async function hover(element: HTMLElement) {
  fireEvent.mouseEnter(element);
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 600));
  });
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

it('shows the picture the server resolved, at exactly the URL it was handed', async () => {
  render(
    <MemoryRouter>
      <MemberChip member={pictureMember()} />
    </MemoryRouter>,
  );

  await hover(screen.getByText('Intrarea, spre lumină'));

  // Queried off the document rather than by role: the preview is decorative beside the title
  // it accompanies, so it carries an empty alt and has no image role to find it by.
  const pictures = document.querySelectorAll('img');
  expect(pictures).toHaveLength(1);

  // The whole URL, not merely the file id in it. The size and the token are what make it
  // fetchable, and a chip that dropped either would draw a broken image for a reader who was
  // entitled to the picture — which is indistinguishable, on screen, from having no picture.
  expect(pictures[0].getAttribute('src')).toBe(pictureUrl);
});

it('draws no picture for a target that resolved without one', async () => {
  // The twin of the test above, and worth stating: every other fixture in this file leaves
  // the field null, so without the positive case an empty preview would pass for correct
  // whatever the server sent.
  renderChip(undefined);

  await hover(screen.getByText('Falia Demo'));

  expect(document.querySelectorAll('img')).toHaveLength(0);
});
