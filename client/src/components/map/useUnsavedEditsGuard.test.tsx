// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { App } from 'antd';
import { createMemoryRouter, RouterProvider, useNavigate } from 'react-router-dom';
import { afterEach, describe, expect, it } from 'vitest';
import '../../i18n';
import { useUnsavedEditsGuard } from './useUnsavedEditsGuard.ts';

afterEach(cleanup);

function MapStandIn({ unsaved }: { unsaved: number }) {
  useUnsavedEditsGuard(unsaved);
  const navigate = useNavigate();
  return (
    <>
      <p>map page</p>
      <button type="button" onClick={() => navigate('/caves/1')}>
        open cave
      </button>
      <button type="button" onClick={() => navigate('/map?layer=x')}>
        same page
      </button>
    </>
  );
}

function renderAt(unsaved: number) {
  const router = createMemoryRouter(
    [
      { path: '/map', element: <MapStandIn unsaved={unsaved} /> },
      { path: '/caves/:id', element: <p>cave page</p> },
    ],
    { initialEntries: ['/map'] },
  );
  render(
    <App>
      <RouterProvider router={router} />
    </App>,
  );
  return router;
}

function unloadIsQuestioned(): boolean {
  const event = new Event('beforeunload', { cancelable: true });
  window.dispatchEvent(event);
  return event.defaultPrevented;
}

describe('leaving the map with unsaved edits', () => {
  it('asks first, and stays when the editor chooses to stay', async () => {
    renderAt(3);

    fireEvent.click(screen.getByText('open cave'));
    expect(await screen.findByText('3 edits on the map are not saved. Leaving the map throws them away.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Stay on the map' }));

    expect(screen.getByText('map page')).toBeInTheDocument();
    expect(screen.queryByText('cave page')).not.toBeInTheDocument();
  });

  it('leaves when the editor confirms', async () => {
    renderAt(1);

    fireEvent.click(screen.getByText('open cave'));
    fireEvent.click(await screen.findByRole('button', { name: 'Leave and discard' }));

    expect(await screen.findByText('cave page')).toBeInTheDocument();
  });

  it('lets a move within the map page and any move without edits through unasked', async () => {
    const router = renderAt(2);
    fireEvent.click(screen.getByText('same page'));
    expect(router.state.location.search).toBe('?layer=x');
    cleanup();

    renderAt(0);
    await act(async () => {
      fireEvent.click(screen.getByText('open cave'));
    });
    expect(await screen.findByText('cave page')).toBeInTheDocument();
  });

  it('has the browser ask before a reload or a closed tab only while edits are unsaved', () => {
    renderAt(2);
    expect(unloadIsQuestioned()).toBe(true);
    cleanup();

    renderAt(0);
    expect(unloadIsQuestioned()).toBe(false);
  });
});
