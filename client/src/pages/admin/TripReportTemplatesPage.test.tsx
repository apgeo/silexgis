// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { TripReportTemplateWrite } from '../../api/hooks.ts';

const createMutate = vi.fn();
const updateMutate = vi.fn();
const deleteMutate = vi.fn();
const download = vi.fn();

const templates = [
  {
    id: '11111111-1111-1111-1111-111111111111',
    name: 'Club bulletin',
    body: '# a layout\n{title}\n',
    isDefault: true,
    createdAt: '2026-07-01T00:00:00Z',
    updatedAt: '2026-07-01T00:00:00Z',
  },
];

const capabilities = { domains: { taxonomies: 'read, write' } };

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    hasAccessAction: actual.hasAccessAction,
    useCapabilities: () => ({ data: capabilities }),
    useTripReportTemplates: () => ({ data: templates, isLoading: false }),
    useCreateTripReportTemplate: () => ({ mutateAsync: createMutate, isPending: false }),
    useUpdateTripReportTemplate: () => ({ mutateAsync: updateMutate, isPending: false }),
    useDeleteTripReportTemplate: () => ({ mutateAsync: deleteMutate, isPending: false }),
  };
});

vi.mock('../../api/download.ts', () => ({
  downloadFile: (url: string) => download(url) as Promise<void>,
  tripReportTemplateDefaultUrl: () => '/api/v1/trip-report-templates/default',
}));

const { default: TripReportTemplatesPage } = await import('./TripReportTemplatesPage.tsx');

beforeEach(() => {
  createMutate.mockReset().mockResolvedValue({});
  updateMutate.mockReset().mockResolvedValue({});
  deleteMutate.mockReset().mockResolvedValue(undefined);
  download.mockReset().mockResolvedValue(undefined);
});
afterEach(cleanup);

function show() {
  return render(
    <App>
      <TripReportTemplatesPage />
    </App>,
  );
}

describe('TripReportTemplatesPage', () => {
  it('hands out the layout the system ships, which is where a club’s own starts', () => {
    show();
    fireEvent.click(screen.getByTestId('report-template-shipped'));
    expect(download).toHaveBeenCalledWith('/api/v1/trip-report-templates/default');
  });

  it('stores an edited layout, and which one write-ups use when nobody chooses', async () => {
    show();
    fireEvent.click(screen.getByRole('button', { name: /Add a layout/ }));
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: '  Bulletin  ' } });
    fireEvent.change(screen.getByTestId('report-template-body'), {
      target: { value: '{title}\n{dates}\n' },
    });
    fireEvent.click(screen.getByRole('switch'));
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await vi.waitFor(() => expect(createMutate).toHaveBeenCalled());
    const body = createMutate.mock.calls[0][0] as TripReportTemplateWrite;
    expect(body).toMatchObject({ name: 'Bulletin', body: '{title}\n{dates}\n', isDefault: true });
  });

  it('says which line the server refused, in the server’s own words', async () => {
    // No wording this page holds could name a line of a file it never parsed, so a refusal that
    // arrived as "the layout could not be read" would leave the editor with nothing to act on.
    createMutate.mockRejectedValue(
      new ApiError(400, 'trip_report_template.invalid', 'Line 4: unknown field {porridge}.'),
    );
    show();
    fireEvent.click(screen.getByRole('button', { name: /Add a layout/ }));
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Broken' } });
    fireEvent.change(screen.getByTestId('report-template-body'), { target: { value: '{porridge}' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Line 4: unknown field {porridge}.')).toBeTruthy();
  });

  it('shows the layout write-ups fall back to when nobody chooses one', () => {
    show();
    expect(screen.getByText('Club bulletin')).toBeTruthy();
    expect(screen.getByText('Used when nobody chooses')).toBeTruthy();
  });
});
