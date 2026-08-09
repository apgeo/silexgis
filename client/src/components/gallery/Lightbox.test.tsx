// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import Lightbox, { type LightboxPhoto } from './Lightbox.tsx';

const photos: LightboxPhoto[] = [
  {
    documentId: 'a',
    title: 'first.png',
    previewUrl: '/api/v1/files/a/thumbnail?size=2400',
    caption: 'The entrance',
    mayDownloadOriginal: true,
    contentUrl: '/api/v1/files/a/content?token=x',
    originalName: 'first.png',
  },
  {
    documentId: 'b',
    title: 'second.png',
    previewUrl: '/api/v1/files/b/thumbnail?size=2400',
    // A photograph whose subject this reader may not place: delivered as renderings only.
    mayDownloadOriginal: false,
  },
];

// Renders accumulate in the document otherwise, and a lookup by label then finds an earlier
// test's picture rather than this one's.
afterEach(cleanup);

describe('Lightbox', () => {
  it('shows nothing when nothing is open', () => {
    render(<Lightbox photos={photos} index={null} onClose={vi.fn()} onIndexChange={vi.fn()} />);

    expect(screen.queryByTestId('lightbox')).toBeNull();
  });

  it('shows the large rendering rather than the upload', () => {
    render(<Lightbox photos={photos} index={0} onClose={vi.fn()} onIndexChange={vi.fn()} />);

    // What makes zooming affordable at all, and what keeps the viewer usable for a picture
    // whose stored bytes this reader may not have.
    const image = screen.getByAltText('The entrance') as HTMLImageElement;
    expect(image.getAttribute('src')).toContain('/thumbnail?');
  });

  it('moves with the arrow keys and wraps at the ends', () => {
    const onIndexChange = vi.fn();
    render(<Lightbox photos={photos} index={1} onClose={vi.fn()} onIndexChange={onIndexChange} />);

    fireEvent.keyDown(window, { key: 'ArrowRight' });
    // Wraps, because a gallery is a loop to somebody flicking through it — stopping dead at the
    // last picture reads as the viewer having broken.
    expect(onIndexChange).toHaveBeenCalledWith(0);

    fireEvent.keyDown(window, { key: 'ArrowLeft' });
    expect(onIndexChange).toHaveBeenCalledWith(0);
  });

  it('closes on escape', () => {
    const onClose = vi.fn();
    render(<Lightbox photos={photos} index={0} onClose={onClose} onIndexChange={vi.fn()} />);

    fireEvent.keyDown(window, { key: 'Escape' });

    expect(onClose).toHaveBeenCalled();
  });

  it('offers the upload where the server says it will hand the bytes over', () => {
    render(<Lightbox photos={photos} index={0} onClose={vi.fn()} onIndexChange={vi.fn()} />);

    expect(screen.getAllByLabelText('Download the original')[0].getAttribute('href'))
      .toContain('/content?');
  });

  it('offers no link for a photograph delivered as renderings only', () => {
    // Its own test rather than a re-render of the one above: the control switches between an
    // anchor and a button, and the suite's per-test cleanup is what guarantees the assertion
    // is looking at this render rather than the previous one.
    render(<Lightbox photos={photos} index={1} onClose={vi.fn()} onIndexChange={vi.fn()} />);

    // Following a link would answer as a missing file, so none is offered — the control says
    // why instead of failing when it is used.
    expect(screen.getAllByLabelText('Download the original')[0].getAttribute('href')).toBeNull();
  });

  it('stops listening to the keyboard once it is closed', () => {
    const onClose = vi.fn();
    const { rerender } = render(
      <Lightbox photos={photos} index={0} onClose={onClose} onIndexChange={vi.fn()} />,
    );

    rerender(<Lightbox photos={photos} index={null} onClose={onClose} onIndexChange={vi.fn()} />);
    fireEvent.keyDown(window, { key: 'Escape' });

    // A viewer that kept its listener would swallow Escape for whatever is on screen after it.
    expect(onClose).not.toHaveBeenCalled();
  });
});
