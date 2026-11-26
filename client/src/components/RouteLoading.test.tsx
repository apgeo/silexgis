// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import i18n from '../i18n';
import RouteLoading from './RouteLoading.tsx';

beforeEach(async () => {
  await i18n.changeLanguage('en');
});
afterEach(cleanup);

/**
 * What a page shows while its code arrives. It used to be a bare spinner pinned to the left edge
 * of the screen, which on a slow phone read as a page that had broken rather than one coming.
 */
describe('the loader a page shows while it arrives', () => {
  it('says in words that the page is loading, as a status a screen reader announces', () => {
    render(<RouteLoading />);

    const status = screen.getByRole('status');
    expect(status).toHaveTextContent('Loading…');
    expect(status).toHaveClass('route-loading');
  });

  it('says it in the language the page is in', async () => {
    await i18n.changeLanguage('ro');
    render(<RouteLoading />);
    expect(screen.getByRole('status')).toHaveTextContent('Se încarcă…');
  });
});
