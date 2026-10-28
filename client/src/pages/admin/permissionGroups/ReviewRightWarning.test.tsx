// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { App } from 'antd';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../../i18n';
import type { AccessCatalog, PermissionGroup } from '../../../api/hooks.ts';

/**
 * The warning said wherever the right to review uploads is handed out: on a group's rules when
 * one of them gives it, and on a group's members when joining the group gives it — full
 * administration included. Over the real query hooks and a faked server; every name is invented.
 */

const get = vi.fn();

vi.mock('../../../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../../../api/client.ts')>('../../../api/client.ts');
  return { ...actual, api: { GET: (...args: unknown[]) => get(...args) } };
});

const { default: RulesTab } = await import('./RulesTab.tsx');
const { default: MembersTab } = await import('./MembersTab.tsx');

const answer = <T,>(data: T) => ({ data, response: new Response(null, { status: 200 }) });

const CATALOG = {
  domains: [
    {
      domain: 'features',
      name: 'features',
      supportsKindNarrowing: false,
      scopes: [{ scopeKind: 'all', actions: ['read'], requiresAnchor: false }],
    },
    {
      domain: 'uploadReview',
      name: 'uploadReview',
      supportsKindNarrowing: false,
      scopes: [{ scopeKind: 'all', actions: ['read', 'write'], requiresAnchor: false }],
    },
  ],
} as unknown as AccessCatalog;

const group = (slug: string, isProtected = false) =>
  ({ id: `group-${slug}`, name: `Invented ${slug}`, slug, isProtected }) as unknown as PermissionGroup;

const rule = (domain: string, effect = 'allow') => ({
  effect,
  domain,
  actions: 'read',
  scopeKind: 'all',
  scopeId: null,
  featureKind: null,
  featureTypeId: null,
});

let entries: unknown[] = [];
const NO_MEMBERS = 'No members yet.';

beforeEach(() => {
  entries = [];
  get.mockImplementation((path: string) => {
    if (path === '/api/v1/permission-groups/{id}/entries') {
      return Promise.resolve(answer(entries));
    }
    return Promise.resolve(answer([]));
  });
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

function show(node: ReactNode) {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <App>{node}</App>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const rulesTab = (slug: string) => (
  <RulesTab
    group={group(slug)}
    catalog={CATALOG}
    previewSeen
    onRulesChanged={() => undefined}
    onRequestPreview={() => undefined}
  />
);

describe('the rules of a group', () => {
  it('warn when a rule gives the right to review uploads', async () => {
    entries = [rule('features'), rule('uploadReview')];
    show(rulesTab('reviewers'));

    const warning = await screen.findByTestId('review-right-warning');
    expect(warning).toHaveTextContent('sees every waiting change in full');
    expect(warning).toHaveTextContent('exact positions of protected caves');
    expect(warning).toHaveTextContent('trusted with all caves');
  });

  it('say nothing when no rule gives it, or when the only one denies it', async () => {
    entries = [rule('features'), rule('uploadReview', 'deny')];
    show(rulesTab('readers'));

    await waitFor(() => expect(screen.getAllByRole('row').length).toBeGreaterThan(2));
    expect(screen.queryByTestId('review-right-warning')).toBeNull();
  });
});

describe('the members of a group', () => {
  it('warn when joining the group gives the right to review uploads', async () => {
    entries = [rule('uploadReview')];
    show(<MembersTab group={group('reviewers')} />);

    expect(await screen.findByTestId('review-right-warning')).toHaveTextContent('trusted with all caves');
  });

  it('warn for full administration, which gives every right', async () => {
    show(<MembersTab group={group('full-administrators', true)} />);

    expect(await screen.findByTestId('review-right-warning')).toBeInTheDocument();
  });

  it('say nothing for a group that does not give it', async () => {
    entries = [rule('features')];
    show(<MembersTab group={group('readers')} />);

    await waitFor(() => expect(get).toHaveBeenCalledWith('/api/v1/permission-groups/{id}/entries', expect.anything()));
    // Both answers have landed once the empty member list is drawn; only then is an absence proof.
    expect(await screen.findByText(NO_MEMBERS)).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByTestId('review-right-warning')).toBeNull());
  });
});
