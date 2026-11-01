// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';

import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import ShowAsGraphButton from './ShowAsGraphButton.tsx';
import { graphRoute } from './graphRoute.ts';

function Where() {
  const loc = useLocation();
  return <div data-testid="where">{loc.pathname + loc.search}</div>;
}

describe('ShowAsGraphButton', () => {
  it('opens the graph seeded on the item', () => {
    render(
      <MemoryRouter initialEntries={['/caves/abc']}>
        <Routes>
          <Route path="*" element={<><ShowAsGraphButton type="feature" id="abc" /><Where /></>} />
        </Routes>
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByTestId('show-as-graph'));
    expect(screen.getByTestId('where')).toHaveTextContent('/graph?type=feature&id=abc');
  });

  it('escapes what it puts in the address', () => {
    expect(graphRoute('feature', 'a b&c')).toBe('/graph?type=feature&id=a+b%26c');
  });
});
