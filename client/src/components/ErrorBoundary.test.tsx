// SPDX-License-Identifier: AGPL-3.0-or-later
import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import '../i18n';
import { ErrorFallback } from './ErrorBoundary.tsx';

describe('ErrorFallback', () => {
  it('calls a screen whose code did not arrive a download that did not finish, not a broken screen', () => {
    render(
      <ErrorFallback detail="error loading dynamically imported module: https://example.test/assets/MapPage-x.js" />,
    );
    expect(screen.getByText('This screen did not finish downloading')).toBeInTheDocument();
    expect(screen.queryByText('Something went wrong on this screen')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reload the page' })).toBeInTheDocument();
  });

  it('still says a screen went wrong when its own code failed', () => {
    render(<ErrorFallback detail="Cannot read properties of undefined (reading 'x')" />);
    expect(screen.getByText('Something went wrong on this screen')).toBeInTheDocument();
  });
});
