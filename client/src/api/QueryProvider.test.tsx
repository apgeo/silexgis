// SPDX-License-Identifier: AGPL-3.0-or-later
import { render, screen } from '@testing-library/react';
import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { App as AntApp } from 'antd';
import { useEffect } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import '../i18n';
import { ApiError, isConcurrencyConflict, retryDelay, retryQuery } from './client.ts';
import { QueryProvider } from './QueryProvider.tsx';

const CONFLICT = 'Someone else changed this since you loaded it. Reload and reapply your changes.';

let held: QueryClient | undefined;

/**
 * Fires one failing save on mount and, once that has settled, an optional second one. The second
 * is how a negative case gets a positive twin inside the same test: the cache's error handler has
 * certainly run for the first failure before the second starts, so counting the warnings after
 * the second tells whether the first drew one too.
 */
function Probe({ failWith, thenFailWith }: { failWith?: unknown; thenFailWith?: unknown }) {
  const client = useQueryClient();
  held = client;
  const save = useMutation({
    mutationFn: () => Promise.reject(failWith),
  });
  const later = useMutation({
    mutationFn: () => Promise.reject(thenFailWith),
  });
  useEffect(() => {
    if (failWith !== undefined) {
      save.mutate(undefined, {
        onSettled: () => {
          if (thenFailWith !== undefined) later.mutate();
        },
      });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- fired once per mount
  }, []);
  return (
    <span>
      {save.isError ? 'failed' : 'idle'}
      {later.isError ? ' then failed' : ''}
    </span>
  );
}

function host(failWith?: unknown, thenFailWith?: unknown) {
  return render(
    <AntApp>
      <QueryProvider>
        <Probe failWith={failWith} thenFailWith={thenFailWith} />
      </QueryProvider>
    </AntApp>,
  );
}

describe('the query client every screen shares', () => {
  afterEach(() => {
    held = undefined;
  });

  it('asks again by the installation-wide rule and waits as long as a refusal names', () => {
    host();
    const queries = held?.getDefaultOptions().queries;
    // The identity matters: 85 hooks override retry, and the ones that do not rely on exactly this.
    expect(queries?.retry).toBe(retryQuery);
    expect(queries?.retryDelay).toBe(retryDelay);
  });

  it.each([
    ['a 412 (the version sent is no longer current)', new ApiError(412, undefined)],
    ['a 428 (no version was sent)', new ApiError(428, undefined)],
    ['a concurrency code on another status', new ApiError(409, 'concurrency.stale')],
  ])('turns %s on any save into the one "changed since you opened it" warning', async (_, error) => {
    host(error);
    expect(await screen.findByText('failed')).toBeInTheDocument();
    expect(await screen.findByText(CONFLICT)).toBeInTheDocument();
  });

  it.each([
    ['a failed server', new ApiError(500, undefined)],
    ['a validation refusal', new ApiError(400, 'validation.failed')],
    ['no answer at all', new TypeError('Failed to fetch')],
  ])('says nothing about a conflict for %s', async (_, error) => {
    // The failure under test, then a stale save: the stale one must draw exactly one warning, so
    // a warning wrongly drawn for the first would show as a second copy.
    host(error, new ApiError(412, undefined));
    expect(await screen.findByText('failed then failed')).toBeInTheDocument();
    expect(await screen.findByText(CONFLICT)).toBeInTheDocument();
    expect(screen.getAllByText(CONFLICT)).toHaveLength(1);
  });
});

describe('isConcurrencyConflict', () => {
  it.each([
    [new ApiError(412, undefined), true],
    [new ApiError(428, undefined), true],
    [new ApiError(409, 'concurrency.stale'), true],
    [new ApiError(400, 'concurrency.version_required'), true],
    [new ApiError(409, 'feature.duplicate'), false],
    [new ApiError(500, undefined), false],
    [new TypeError('Failed to fetch'), false],
    [undefined, false],
  ])('%o → %s', (error, expected) => {
    expect(isConcurrencyConflict(error)).toBe(expected);
  });
});
