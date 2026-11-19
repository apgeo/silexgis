// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { App } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { CenterlineInfo } from '../../api/hooks.ts';

const remove = vi.fn();

/** A read of the list that did not land, switched on by the test that needs it. */
const failedRead = vi.hoisted(() => ({ on: false, refetch: vi.fn() }));
const failedAnswer = () => ({ data: undefined, isError: true, isFetching: false, refetch: failedRead.refetch });

vi.mock('../../api/hooks.ts', () => ({
  useCenterlines: () => failedRead.on ? failedAnswer() : ({
    data: [
      {
        id: 'cl-1',
        caveId: 'cave-1',
        name: 'Main line',
        isDefault: true,
        lengthM: 120,
        source: 'geojson',
      } as unknown as CenterlineInfo,
    ],
  }),
  useUploadCenterline: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUpdateCenterline: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteCenterline: () => ({ mutateAsync: remove }),
}));

const { default: CenterlineSection } = await import('./CenterlineSection.tsx');

beforeEach(() => {
  remove.mockReset();
});
afterEach(cleanup);

describe('CenterlineSection deleting', () => {
  // A refused delete leaves the row where it was. The reader is told, and the refusal does not
  // escape as an unhandled rejection, which the application's own error observers record.
  it('says a refused delete did not happen', async () => {
    remove.mockRejectedValue(new ApiError(403, 'acl.forbidden'));
    render(
      <App>
        <CenterlineSection caveId="cave-1" canEdit />
      </App>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    const confirm = await screen.findByRole('tooltip');
    fireEvent.click(within(confirm).getByRole('button', { name: /OK|Yes/ }));

    expect(remove).toHaveBeenCalledWith({ id: 'cl-1', caveId: 'cave-1' });
    expect(await screen.findByText('The deletion failed. Please try again.')).toBeInTheDocument();
  });

  // A version conflict is reported once, by the application-wide handler; a second toast here
  // would contradict it.
  it('leaves a version conflict to the application-wide handler', async () => {
    remove.mockRejectedValue(new ApiError(412, 'concurrency.version_mismatch'));
    render(
      <App>
        <CenterlineSection caveId="cave-1" canEdit />
      </App>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    const confirm = await screen.findByRole('tooltip');
    fireEvent.click(within(confirm).getByRole('button', { name: /OK|Yes/ }));

    expect(remove).toHaveBeenCalled();
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(screen.queryByText('The deletion failed. Please try again.')).toBeNull();
  });
});

describe('a read of the list that did not land', () => {
  afterEach(() => {
    failedRead.on = false;
    failedRead.refetch.mockReset();
  });

  it('says the read failed and offers Retry, rather than saying there is nothing', () => {
    failedRead.on = true;
    render(<App><CenterlineSection caveId="cave-1" canEdit /></App>);
    expect(screen.getByTestId('centerlines-load-failed')).toBeTruthy();
    expect(screen.queryByText(i18n.t('centerlines.empty'))).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: i18n.t('common.retry') }));
    expect(failedRead.refetch).toHaveBeenCalledTimes(1);
  });

  it('offers no upload under a list that was never read', () => {
    failedRead.on = true;
    render(<App><CenterlineSection caveId="cave-1" canEdit /></App>);
    expect(screen.queryByText(i18n.t('centerlines.upload'))).toBeNull();
  });
});
