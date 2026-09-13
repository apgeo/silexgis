// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TrackedCaver } from '../../caveview/trackedCavers.ts';
import CaveViewTrackingOverlay from './CaveViewTrackingOverlay.tsx';

const caver = (overrides: Partial<TrackedCaver> = {}): TrackedCaver => ({
  caverId: 'caver-1',
  name: 'Ana',
  teamTitle: 'Echipa 1',
  position: { kind: 'station', station: 'pestera.galerie.7' },
  lastRecordedAt: '2026-09-12T09:00:00Z',
  enteredAt: '2026-09-12T08:00:00Z',
  out: false,
  ...overrides,
});

/** The overlay with the panel's own state around it, which is where the open card lives. */
function Harness({ cavers }: { cavers: readonly TrackedCaver[] }) {
  const [open, setOpen] = useState<string | null>(null);
  const [times, setTimes] = useState(false);
  return (
    <CaveViewTrackingOverlay
      cavers={cavers}
      showTimes={times}
      onShowTimesChange={setTimes}
      openCaverId={open}
      onOpenCaver={setOpen}
    />
  );
}

afterEach(cleanup);

describe('CaveViewTrackingOverlay', () => {
  it('lists a withheld position as withheld rather than leaving the person out', () => {
    // The rule this surface exists to keep: somebody whose position was kept from this reader has
    // no marker, so the list is the only place they appear at all — and an absence there would
    // read as nobody knowing where they are.
    render(
      <Harness
        cavers={[
          caver({ caverId: 'a', name: 'Ana', position: { kind: 'withheld', certain: true } }),
          caver({ caverId: 'b', name: 'Bogdan', position: { kind: 'withheld', certain: false } }),
          caver({ caverId: 'c', name: 'Cora', position: { kind: 'unreported' } }),
        ]}
      />,
    );

    expect(screen.getByTestId('caveview-position-withheld')).toHaveTextContent('Not shown to you');
    expect(screen.getByTestId('caveview-position-maybe-withheld')).toHaveTextContent(
      'Not shown, or not reported',
    );
    expect(screen.getByTestId('caveview-caver-c')).toBeInTheDocument();
  });

  it('opens and closes a caver’s card from a tap, with no hover anywhere in it', () => {
    // The marker hover this card also opens never happens on a touchscreen, so the row is the
    // path that has to work: tap to open, a button to close, and Escape for the keyboard.
    render(<Harness cavers={[caver()]} />);

    fireEvent.click(screen.getByTestId('caveview-caver-caver-1'));
    const card = screen.getByTestId('caveview-caver-card');
    expect(card).toHaveTextContent('Echipa 1');
    expect(card).toHaveTextContent('pestera.galerie.7');

    fireEvent.click(screen.getByTestId('caveview-caver-card-close'));
    expect(screen.queryByTestId('caveview-caver-card')).not.toBeInTheDocument();

    fireEvent.click(screen.getByTestId('caveview-caver-caver-1'));
    fireEvent.keyDown(screen.getByTestId('caveview-caver-card'), { key: 'Escape' });
    expect(screen.queryByTestId('caveview-caver-card')).not.toBeInTheDocument();
  });

  it('says why a position is missing where there is room to say it', () => {
    render(<Harness cavers={[caver({ position: { kind: 'withheld', certain: true }, out: true })]} />);

    fireEvent.click(screen.getByTestId('caveview-caver-caver-1'));

    const card = screen.getByTestId('caveview-caver-card');
    expect(card).toHaveTextContent('This position exists and you may not be told it');
    // Somebody reported out is marked as out: their last position is where they were.
    expect(screen.getByTestId('caveview-caver-out')).toBeInTheDocument();
  });

  it('hands the last-update switch straight to whoever owns the markers', () => {
    const onChange = vi.fn();
    render(
      <CaveViewTrackingOverlay
        cavers={[caver()]}
        showTimes={false}
        onShowTimesChange={onChange}
        openCaverId={null}
        onOpenCaver={vi.fn()}
      />,
    );

    fireEvent.click(screen.getByTestId('caveview-tracking-times'));

    expect(onChange).toHaveBeenCalledWith(true, expect.anything());
  });
});
