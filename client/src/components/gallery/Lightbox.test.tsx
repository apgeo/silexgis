// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { Drawer, Input } from 'antd';
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

  it('says in view, not only in a tooltip, that the location was removed from this copy', () => {
    const removed: LightboxPhoto[] = [{ ...photos[0], locationRemoved: true }, photos[1]];
    const { rerender } = render(<Lightbox photos={removed} index={0} onClose={vi.fn()} onIndexChange={vi.fn()} />);
    expect(screen.getByTestId('location-removed')).toBeTruthy();

    rerender(<Lightbox photos={removed} index={1} onClose={vi.fn()} onIndexChange={vi.fn()} />);
    expect(screen.queryByTestId('location-removed')).toBeNull();
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

  it('leaves typing in a field opened over it alone, and still answers keys on the picture', () => {
    // The credit drawer is opened from the viewer's edit button and drawn in a portal, so its
    // fields' keystrokes reach the viewer's window listener: a place called "Sala 10" or a
    // photographer "Ion-Vasile" lost its 0 and its hyphen, and an arrow in the caption moved
    // the picture behind the drawer.
    const onIndexChange = vi.fn();
    const onClose = vi.fn();
    render(
      <>
        <Lightbox photos={photos} index={0} onClose={onClose} onIndexChange={onIndexChange} />
        <Drawer open title="Credit">
          <Input aria-label="Place" />
          <Input.TextArea aria-label="Caption" />
        </Drawer>
      </>,
    );
    const place = screen.getByLabelText('Place');
    const caption = screen.getByLabelText('Caption');

    for (const key of ['-', '=', '+', '0']) {
      // fireEvent answers false when a listener cancelled the key: the character never lands.
      expect(fireEvent.keyDown(place, { key })).toBe(true);
    }
    expect(fireEvent.keyDown(caption, { key: 'ArrowLeft' })).toBe(true);
    expect(onIndexChange).not.toHaveBeenCalled();

    // The positive case in the same render: the viewer itself still answers.
    expect(fireEvent.keyDown(window, { key: 'ArrowRight' })).toBe(false);
    expect(onIndexChange).toHaveBeenCalledWith(1);
  });

  it('lets Escape close only the drawer opened over it', () => {
    const onClose = vi.fn();
    render(
      <>
        <Lightbox photos={photos} index={0} onClose={onClose} onIndexChange={vi.fn()} />
        <Drawer open title="Credit">
          <button type="button">Save</button>
        </Drawer>
      </>,
    );

    // Focus inside the drawer, on something that is not a text field.
    fireEvent.keyDown(screen.getByText('Save'), { key: 'Escape' });
    expect(onClose).not.toHaveBeenCalled();

    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).toHaveBeenCalled();
  });

  it('still answers keys when it is itself opened from inside a drawer', () => {
    // A trip's or an expedition's photographs are shown in a drawer and the viewer is opened
    // from a thumbnail there: focus stays on that thumbnail, inside the drawer that holds the
    // viewer, and the keys are the viewer's.
    const onIndexChange = vi.fn();
    render(
      <Drawer open title="Trip">
        <button type="button">thumbnail</button>
        <Lightbox photos={photos} index={0} onClose={vi.fn()} onIndexChange={onIndexChange} />
      </Drawer>,
    );

    fireEvent.keyDown(screen.getByText('thumbnail'), { key: 'ArrowRight' });
    expect(onIndexChange).toHaveBeenCalledWith(1);
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
