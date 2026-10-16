// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../../i18n';

// What the dialog's code does when it arrives, as each test sets it: held back, failing to load, or
// failing as it is drawn.
const dialog = vi.hoisted(() => ({
  arrived: null as null | (() => void),
  behaviour: 'draws' as 'draws' | 'throws' | 'stale' | 'held',
}));
vi.mock('./TrackingMovieDialog.tsx', async () => {
  if (dialog.behaviour === 'held') {
    await new Promise<void>((resolve) => {
      dialog.arrived = resolve;
    });
  }
  return {
    default: () => {
      if (dialog.behaviour === 'throws') {
        throw new Error('the dialog failed while it was drawn');
      }
      if (dialog.behaviour === 'stale') {
        // What Chromium says when the page names a file an update has since replaced.
        throw new TypeError('Failed to fetch dynamically imported module: https://example.test/assets/TrackingMovieDialog-old.js');
      }
      return <div data-testid="movie-dialog">the dialog</div>;
    },
  };
});

const { default: LazyTrackingMovieDialog } = await import('./LazyTrackingMovieDialog.tsx');

function Page({ surveyModelId }: { surveyModelId: string | null }) {
  return (
    <App>
      <p data-testid="trip-page">the trip page</p>
      <LazyTrackingMovieDialog surveyModelId={surveyModelId} onClose={() => {}} />
    </App>
  );
}

beforeEach(() => {
  dialog.behaviour = 'draws';
  dialog.arrived = null;
  vi.spyOn(console, 'error').mockImplementation(() => {});
});
afterEach(() => vi.restoreAllMocks());

describe('LazyTrackingMovieDialog', () => {
  it('says the dialog is opening while its code arrives, then shows it', async () => {
    dialog.behaviour = 'held';
    render(<Page surveyModelId="model-1" />);
    expect(await screen.findByText('Opening the movie maker…')).toBeInTheDocument();
    await waitFor(() => expect(dialog.arrived).not.toBeNull());
    await act(async () => dialog.arrived!());
    expect(await screen.findByTestId('movie-dialog')).toBeInTheDocument();
  });

  it('says in a dialog, with the reason, when it cannot open — and leaves the page behind it alone', async () => {
    dialog.behaviour = 'throws';
    render(<Page surveyModelId="model-1" />);
    expect(await screen.findByText('The movie maker could not open.')).toBeInTheDocument();
    expect(screen.getByText('the dialog failed while it was drawn')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reload the page' })).toBeInTheDocument();
    expect(screen.getByTestId('trip-page')).toBeInTheDocument();
  });

  it('says a page opened before an update has to be reloaded, rather than quoting the browser', async () => {
    dialog.behaviour = 'stale';
    render(<Page surveyModelId="model-1" />);
    expect(await screen.findByText(/The application was updated after this page was opened/)).toBeInTheDocument();
    expect(screen.queryByText(/Failed to fetch dynamically imported module/)).not.toBeInTheDocument();
  });

  it('tries again on the next opening rather than showing the last one’s failure', async () => {
    dialog.behaviour = 'throws';
    const { rerender } = render(<Page surveyModelId="model-1" />);
    expect(await screen.findByText('The movie maker could not open.')).toBeInTheDocument();
    dialog.behaviour = 'draws';
    rerender(<Page surveyModelId={null} />);
    rerender(<Page surveyModelId="model-1" />);
    expect(await screen.findByTestId('movie-dialog')).toBeInTheDocument();
  });
});
