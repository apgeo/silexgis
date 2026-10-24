// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { afterEach, describe, expect, it } from 'vitest';
import PaneSlot from './PaneSlot.tsx';
import { usePaneElement } from './paneElement.ts';

afterEach(cleanup);

let mounts = 0;

/** Stands in for a pane that is expensive to build: it counts every time it is built. */
function Costly() {
  useEffect(() => {
    mounts++;
  }, []);
  return <canvas data-testid="drawn" />;
}

function Workspace({ place }: { place: 'left' | 'right' | 'bottom' | 'none' }) {
  const element = usePaneElement('test');
  const [shown] = useState(true);
  return (
    <>
      <div data-testid="left">{place === 'left' && <PaneSlot key="left" element={element} />}</div>
      <div data-testid="right">{place === 'right' && <PaneSlot key="right" element={element} />}</div>
      <section data-testid="bottom">
        <div>{place === 'bottom' && <PaneSlot key="bottom" element={element} />}</div>
      </section>
      {shown && createPortal(<Costly />, element)}
    </>
  );
}

describe('a pane moved between slots', () => {
  it('is shown in whichever slot is mounted, and its content is built once', () => {
    mounts = 0;
    const { rerender } = render(<Workspace place="right" />);
    const drawn = screen.getByTestId('drawn');
    expect(screen.getByTestId('right').contains(drawn)).toBe(true);

    for (const place of ['left', 'bottom', 'right', 'bottom', 'left'] as const) {
      rerender(<Workspace place={place} />);
      expect(screen.getByTestId(place).contains(drawn), place).toBe(true);
    }
    // The same element throughout — moved, never made again — and built exactly once.
    expect(screen.getByTestId('drawn')).toBe(drawn);
    expect(mounts).toBe(1);
  });

  it('leaves the document while no slot shows it, and comes back the same element', () => {
    const { rerender } = render(<Workspace place="left" />);
    const drawn = screen.getByTestId('drawn');
    rerender(<Workspace place="none" />);
    expect(drawn.isConnected).toBe(false);
    rerender(<Workspace place="bottom" />);
    expect(drawn.isConnected).toBe(true);
    expect(screen.getByTestId('bottom').contains(drawn)).toBe(true);
  });
});
