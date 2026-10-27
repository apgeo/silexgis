// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TripAttention } from '../../api/hooks/recordings.ts';

const post = vi.fn();

vi.mock('../../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts');
  return { ...actual, api: { POST: (...args: unknown[]) => post(...args) } };
});

const { default: TripAttentionNotice } = await import('./TripAttentionNotice.tsx');

const TRIP = '99999999-9999-4999-8999-999999999999';
const RAISED: TripAttention = {
  raised: true,
  raisedAt: '2026-10-20T10:00:00Z',
  clearedAt: null,
  reasons: [
    { code: 'place_unknown', count: 2 },
    { code: 'unmapped_person', count: 5 },
    { code: 'invented_by_a_later_version', count: 1 },
  ],
};

function show(attention = RAISED, canEdit = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <App>
        <TripAttentionNotice tripId={TRIP} attention={attention} canEdit={canEdit} />
      </App>
    </QueryClientProvider>,
  );
}

afterEach(() => {
  cleanup();
  post.mockReset();
});

describe('TripAttentionNotice', () => {
  it('lists every reason in plain words with its count, and a code it does not know in general words', () => {
    show();
    expect(screen.getByTestId('trip-attention-raised')).toBeTruthy();
    expect(screen.getByTestId('trip-attention-place_unknown').textContent).toContain('2');
    expect(screen.getByTestId('trip-attention-place_unknown').textContent).toContain(
      'scans at no place',
    );
    expect(screen.getByTestId('trip-attention-unmapped_person').textContent).toContain(
      'Phone users page',
    );
    expect(screen.getByText(/something else that needed looking at/)).toBeTruthy();
    // No raw code ever reaches the reader.
    expect(screen.queryByText(/unmapped_person/)).toBeNull();
  });

  it('clears the flag of this trip when a reader who may change it presses the button', async () => {
    post.mockResolvedValue({ response: { status: 204 } });
    show();
    fireEvent.click(screen.getByTestId('trip-attention-clear'));
    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    expect(post.mock.calls[0][0]).toBe('/api/v1/trip-logs/{id}/attention/clear');
    expect(post.mock.calls[0][1]).toEqual({ params: { path: { id: TRIP } } });
  });

  it('offers no button to a reader who may only read the trip', () => {
    show(RAISED, false);
    expect(screen.queryByTestId('trip-attention-clear')).toBeNull();
    expect(screen.getByTestId('trip-attention-raised')).toBeTruthy();
  });

  it('keeps the reasons after the flag is cleared, as a note with no button', () => {
    show({ ...RAISED, raised: false, clearedAt: '2026-10-21T09:00:00Z' });
    expect(screen.getByTestId('trip-attention-cleared')).toBeTruthy();
    expect(screen.queryByTestId('trip-attention-clear')).toBeNull();
    expect(screen.getByTestId('trip-attention-place_unknown')).toBeTruthy();
  });
});
