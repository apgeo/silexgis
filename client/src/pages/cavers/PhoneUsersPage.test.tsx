// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

/**
 * The phone users page, over the real query hooks and a faked server.
 *
 * What is pinned: the page shows the server's order (unmapped first) without re-sorting it, says
 * who made each mapping, offers the mapping controls only to an account that may write the roster,
 * and sends exactly the link and unlink the server expects. Every name below is invented.
 */

const get = vi.fn();
const put = vi.fn();
const del = vi.fn();

vi.mock('../../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts');
  return {
    ...actual,
    api: {
      GET: (...args: unknown[]) => get(...args),
      PUT: (...args: unknown[]) => put(...args),
      DELETE: (...args: unknown[]) => del(...args),
    },
  };
});

const { default: PhoneUsersPage } = await import('./PhoneUsersPage.tsx');
const { ApiError } = await import('../../api/client.ts');

const WHEN = '2026-10-20T10:00:00Z';
const UNMAPPED = {
  deviceUserId: 'aaaaaaaa-0000-7000-8000-000000000001',
  username: 'newcomer',
  firstName: 'Nia',
  lastName: 'Invented',
  caverId: null,
  caverName: null,
  linkSource: null,
  firstSeenAt: WHEN,
  lastSeenAt: WHEN,
};
const MAPPED = {
  deviceUserId: 'bbbbbbbb-0000-7000-8000-000000000002',
  username: 'democaver',
  firstName: null,
  lastName: null,
  caverId: 'caver-demo',
  caverName: 'Demo Caver',
  linkSource: 'administrator',
  firstSeenAt: WHEN,
  lastSeenAt: WHEN,
};
const DEVICE_MAPPED = {
  ...MAPPED,
  deviceUserId: 'cccccccc-0000-7000-8000-000000000003',
  username: 'phoneset',
  linkSource: 'device',
};
const IMPORTED = { ...DEVICE_MAPPED, deviceUserId: 'dddddddd-0000-7000-8000-000000000004', linkSource: 'archiveImport' };
/** A phone user a phone named on a photo or a recording and never described. */
const NAMELESS = {
  ...UNMAPPED,
  deviceUserId: 'eeeeeeee-0000-7000-8000-000000000005',
  username: null,
  firstName: null,
  lastName: null,
  sentByUserId: 'user-invented',
  sentByName: 'Ion Inventat',
};

const answer = <T,>(data: T) => ({ data, response: new Response(null, { status: 200 }) });

let rights = 'read, write';
let rows: unknown[] = [];
let listFailure: unknown = null;

beforeEach(() => {
  rights = 'read, write';
  rows = [UNMAPPED, MAPPED, DEVICE_MAPPED, IMPORTED];
  listFailure = null;
  get.mockImplementation((path: string) => {
    if (path === '/api/v1/me/capabilities') {
      return Promise.resolve(answer({ domains: { cavers: rights } }));
    }
    if (path === '/api/v1/phone-users') {
      return listFailure
        ? Promise.reject(listFailure)
        : Promise.resolve(answer({ items: rows, page: 1, pageSize: 25, totalItems: rows.length }));
    }
    if (path === '/api/v1/cavers') {
      return Promise.resolve(answer([{ id: 'caver-ana', name: 'Ana Invented' }]));
    }
    return Promise.reject(new Error(`unexpected read of ${path}`));
  });
  put.mockResolvedValue(answer({ ...UNMAPPED, caverId: 'caver-ana', caverName: 'Ana Invented', linkSource: 'administrator' }));
  del.mockResolvedValue(answer({ ...MAPPED, caverId: null, caverName: null, linkSource: null }));
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

function show() {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <App>
          <PhoneUsersPage />
        </App>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('the phone users list', () => {
  it('keeps the server order and says who made each mapping', async () => {
    show();

    const first = await screen.findByTestId('phone-user-source-aaaaaaaa');
    expect(first).toHaveTextContent('Not mapped');
    expect(screen.getByTestId('phone-user-source-bbbbbbbb')).toHaveTextContent('Somebody keeping the roster');
    expect(screen.getByTestId('phone-user-source-cccccccc')).toHaveTextContent('The phone');
    expect(screen.getByTestId('phone-user-source-dddddddd')).toHaveTextContent('An archive import');

    const body = screen.getAllByRole('row').slice(1);
    expect(body[0]).toHaveTextContent('Nia Invented');
    expect(body[1]).toHaveTextContent('democaver');
    expect(screen.getByTestId('phone-users-unmapped')).toHaveTextContent('Some phone users are not mapped to a caver yet');
  });

  it('shows a phone user the phone never described as such, with who sent it and since when', async () => {
    rows = [NAMELESS, UNMAPPED];
    show();

    const label = await screen.findByTestId('phone-user-nameless-eeeeeeee');
    expect(label).toHaveTextContent('Not yet described by the phone');
    const row = label.closest('tr') as HTMLElement;
    expect(row).toHaveTextContent('Sent by Ion Inventat');
    expect(row).toHaveTextContent('First seen');
    // Linked like any other, from the same picker.
    expect(within(row).getByTestId('phone-user-pick-eeeeeeee')).toBeInTheDocument();
    // A described user is shown by its name, never as undescribed.
    expect(screen.queryByTestId('phone-user-nameless-aaaaaaaa')).toBeNull();
    expect(screen.getByText('Nia Invented')).toBeInTheDocument();
  });

  it('shows a mapped row with the caver, and an unmapped one with nobody proposed', async () => {
    show();

    await screen.findByTestId('phone-user-source-aaaaaaaa');
    const mapped = screen.getByTestId('phone-user-pick-bbbbbbbb');
    expect(within(mapped).getByText('Demo Caver')).toBeInTheDocument();
    const open = screen.getByTestId('phone-user-pick-aaaaaaaa');
    expect(within(open).queryByText('Ana Invented')).toBeNull();
    expect(within(open).getByText('Say which caver this is')).toBeInTheDocument();
  });
});

describe('who may map', () => {
  it('offers no controls to an account that may only read the roster', async () => {
    rights = 'read';
    show();

    await screen.findByTestId('phone-user-source-aaaaaaaa');
    expect(screen.queryByTestId('phone-user-pick-aaaaaaaa')).toBeNull();
    expect(screen.queryByTestId('phone-user-unlink-bbbbbbbb')).toBeNull();
    expect(screen.getAllByText('Demo Caver').length).toBeGreaterThan(0);
  });

  it('says the list is closed when the server refuses it', async () => {
    listFailure = new ApiError(403, 'access.forbidden');
    show();

    expect(await screen.findByTestId('phone-users-error')).toHaveTextContent('open to accounts that may read the roster');
  });
});

describe('mapping and unmapping', () => {
  it('links the chosen caver to the phone user', async () => {
    show();

    const pick = await screen.findByTestId('phone-user-pick-aaaaaaaa');
    fireEvent.mouseDown(within(pick).getByRole('combobox'));
    fireEvent.click(await screen.findByText('Ana Invented'));

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1));
    expect(put).toHaveBeenCalledWith('/api/v1/phone-users/{deviceUserId}/link', {
      params: { path: { deviceUserId: UNMAPPED.deviceUserId } },
      body: { caverId: 'caver-ana' },
    });
  });

  it('removes a mapping only after the confirmation is answered', async () => {
    show();

    fireEvent.click(await screen.findByTestId('phone-user-unlink-bbbbbbbb'));
    const confirm = await screen.findByText('Remove this mapping?');
    expect(del).not.toHaveBeenCalled();
    const popup = confirm.closest('.ant-popover') as HTMLElement;
    fireEvent.click(within(popup).getByRole('button', { name: 'Remove mapping' }));

    await waitFor(() => expect(del).toHaveBeenCalledTimes(1));
    expect(del).toHaveBeenCalledWith('/api/v1/phone-users/{deviceUserId}/link', {
      params: { path: { deviceUserId: MAPPED.deviceUserId } },
    });
  });

  it('names the reason when the caver has gone from the roster meanwhile', async () => {
    put.mockRejectedValue(new ApiError(404, 'sync.caver_not_found'));
    show();

    const pick = await screen.findByTestId('phone-user-pick-aaaaaaaa');
    fireEvent.mouseDown(within(pick).getByRole('combobox'));
    fireEvent.click(await screen.findByText('Ana Invented'));

    expect(await screen.findByText('That caver is no longer on the roster.')).toBeInTheDocument();
  });
});
