// SPDX-License-Identifier: AGPL-3.0-or-later
import { fireEvent, render, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import i18n from '../i18n/index.ts';
import NotFoundPage from './NotFoundPage.tsx';

describe('NotFoundPage', () => {
  it('names the address that was not found and leads back to the start', async () => {
    const router = createMemoryRouter(
      [
        { path: '/', element: <p>start page</p> },
        { path: '*', element: <NotFoundPage /> },
      ],
      { initialEntries: ['/caves/bookmarked-long-ago'] },
    );
    render(<RouterProvider router={router} />);

    expect(screen.getByText(i18n.t('notFoundPage.title'))).toBeInTheDocument();
    expect(screen.getByText('/caves/bookmarked-long-ago')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: i18n.t('notFoundPage.home') }));
    expect(await screen.findByText('start page')).toBeInTheDocument();
  });
});
