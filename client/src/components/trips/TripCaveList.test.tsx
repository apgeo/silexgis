// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import TripCaveList from './TripCaveList.tsx';

const OPEN = '11111111-2222-3333-4444-555555555555';

vi.mock('../../api/hooks.ts', () => ({
  useCave: () => ({ data: { id: OPEN, name: 'Peștera Mare' } }),
}));

afterEach(cleanup);

function draw(caveIds: string[], withheld: number) {
  return render(
    <MemoryRouter>
      <TripCaveList caveIds={caveIds} withheld={withheld} />
    </MemoryRouter>,
  );
}

describe('TripCaveList', () => {
  it('says how many caves were kept back, and never which', () => {
    draw([OPEN], 2);

    // The positive half: what the reader was given is still named and still reachable.
    expect(screen.getByRole('link', { name: 'Peștera Mare' })).toHaveAttribute(
      'href',
      `/caves/${OPEN}`,
    );

    // The withheld half is a number. An identifier here would be the whole leak: it is enough
    // to go and ask for the cave by it.
    const withheld = screen.getByTestId('trip-caves-withheld');
    expect(withheld).toHaveTextContent('2 not shown to you');
    expect(withheld.textContent).not.toMatch(/[0-9a-f]{8}-/i);
  });

  it('still says the list is short when the reader was given none of it', () => {
    // Otherwise a trip whose every cave is withheld is indistinguishable from a trip that
    // never named one, and the reader is quietly told something false.
    draw([], 1);

    expect(screen.getByTestId('trip-caves-withheld')).toHaveTextContent('1 not shown to you');
  });

  it('shows nothing at all when there is neither a cave nor a shortfall', () => {
    const { container } = draw([], 0);

    expect(container).toBeEmptyDOMElement();
  });

  it('does not claim a shortfall to a reader shown everything', () => {
    draw([OPEN], 0);

    expect(screen.queryByTestId('trip-caves-withheld')).toBeNull();
    expect(screen.getByRole('link', { name: 'Peștera Mare' })).toBeInTheDocument();
  });
});
