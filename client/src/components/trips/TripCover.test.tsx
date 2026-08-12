// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const { attachmentsSpy } = vi.hoisted(() => ({
  attachmentsSpy: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  useAttachments: (...args: unknown[]) => attachmentsSpy(...args),
}));

const { default: TripCover } = await import('./TripCover.tsx');

interface StubAttachment {
  id: string;
  isPrimary: boolean;
  caption?: string | null;
  file: { kind: string; thumbnailUrl?: string | null; contentUrl?: string | null };
}

function show(attachments: StubAttachment[] | undefined) {
  attachmentsSpy.mockReturnValue({ data: attachments });
  return render(<TripCover tripId="trip-1" tripTitle="Sunday in the cave" />);
}

const starred: StubAttachment = {
  id: 'a1',
  isPrimary: true,
  caption: 'The entrance series',
  file: { kind: 'image', thumbnailUrl: '/api/v1/files/f1/thumbnail?token=x' },
};

afterEach(cleanup);

describe('TripCover', () => {
  it('draws the starred picture as the trip’s cover', () => {
    show([
      { id: 'a0', isPrimary: false, file: { kind: 'image', thumbnailUrl: '/other.jpg' } },
      starred,
    ]);

    const image = screen.getByRole('img');
    expect(image.getAttribute('src')).toBe('/api/v1/files/f1/thumbnail?token=x');
    expect(image.getAttribute('alt')).toBe('The entrance series');
  });

  /**
   * The picture is read from the attachments the caller may see, so a cover is only ever made of a
   * picture already shown to them. This pins the consequence: give it nothing readable and it
   * draws nothing, rather than reaching for a picture by another route.
   */
  it('draws nothing when no readable picture is starred', () => {
    const { container } = show([
      { id: 'a0', isPrimary: false, file: { kind: 'image', thumbnailUrl: '/other.jpg' } },
    ]);
    expect(container.textContent).toBe('');
    expect(screen.queryByRole('img')).toBeNull();
  });

  it('draws nothing while the attachments are still being asked for', () => {
    const { container } = show(undefined);
    expect(container.textContent).toBe('');
  });

  /** A starred document is not a cover — the star is one flag over everything attached. */
  it('ignores a starred attachment that is not a picture', () => {
    show([{ id: 'a2', isPrimary: true, file: { kind: 'document', contentUrl: '/report.pdf' } }]);
    expect(screen.queryByRole('img')).toBeNull();
  });
});
