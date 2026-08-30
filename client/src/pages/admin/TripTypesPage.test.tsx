// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TripTypeWrite } from '../../api/hooks.ts';

const createMutate = vi.fn();
const updateMutate = vi.fn();
const deleteMutate = vi.fn();

const tripTypes = [
  {
    id: 1,
    code: 'survey',
    name: 'Survey / mapping',
    description: null,
    sortOrder: 10,
    isSeeded: true,
    fieldDataSchema: '{"type":"object","properties":{"survey_grade":{"type":"string","title":"Survey grade"}}}',
    fieldDataSchemaVersion: 1,
    logisticsSchema: null,
    logisticsSchemaVersion: 1,
    safetySchema: null,
    safetySchemaVersion: 1,
  },
  {
    id: 2,
    code: 'club_dig',
    name: 'Club dig',
    description: null,
    sortOrder: 90,
    isSeeded: false,
    fieldDataSchema: null,
    fieldDataSchemaVersion: 1,
    logisticsSchema: null,
    logisticsSchemaVersion: 1,
    safetySchema: null,
    safetySchemaVersion: 1,
  },
];

const capabilities = { domains: { taxonomies: 'read, write' } };

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    hasAccessAction: actual.hasAccessAction,
    useCapabilities: () => ({ data: capabilities }),
    useTripTypes: () => ({ data: tripTypes, isLoading: false }),
    useChecklists: () => ({ data: [] }),
    useCreateTripType: () => ({ mutateAsync: createMutate, isPending: false }),
    useUpdateTripType: () => ({ mutateAsync: updateMutate, isPending: false }),
    useDeleteTripType: () => ({ mutateAsync: deleteMutate, isPending: false }),
  };
});

const { default: TripTypesPage } = await import('./TripTypesPage.tsx');

beforeEach(() => {
  createMutate.mockReset().mockResolvedValue({});
  updateMutate.mockReset().mockResolvedValue({});
  deleteMutate.mockReset().mockResolvedValue(undefined);
});
afterEach(cleanup);

function show() {
  return render(
    <App>
      <TripTypesPage />
    </App>,
  );
}

describe('TripTypesPage', () => {
  it('offers a delete only for a purpose the installation added', () => {
    show();
    // Two rows, one delete: the shipped purpose keeps its code and its row, because other
    // installations read trips by that code.
    expect(screen.getAllByRole('button', { name: 'Edit' })).toHaveLength(2);
    expect(screen.getAllByRole('button', { name: 'Delete' })).toHaveLength(1);
  });

  it('warns while a required field is being written, not after every new trip is refused', async () => {
    show();
    fireEvent.click(screen.getAllByRole('button', { name: 'Edit' })[1]);
    const box = screen.getByLabelText('Field data schema (JSON Schema)');

    // Optional fields: nothing to warn about.
    fireEvent.change(box, {
      target: { value: '{"type":"object","properties":{"conditions":{"type":"string"}}}' },
    });
    expect(screen.queryByTestId('schema-required-warning-fieldDataSchema')).toBeNull();

    // A trip is recorded before its report is written, so a field the schema demands has
    // nowhere to be answered when the trip is created — and every new trip of this purpose is
    // refused. Said here, where the choice is being made.
    fireEvent.change(box, {
      target: {
        value:
          '{"type":"object","required":["conditions"],"properties":{"conditions":{"type":"string"}}}',
      },
    });
    await vi.waitFor(() =>
      expect(screen.getByTestId('schema-required-warning-fieldDataSchema')).toBeTruthy(),
    );
  });

  it('saves a blank schema box as nothing asked for rather than as an empty object', async () => {
    show();
    fireEvent.click(screen.getAllByRole('button', { name: 'Edit' })[1]);
    fireEvent.change(screen.getByLabelText('Logistics schema (JSON Schema)'), {
      target: { value: '   ' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await vi.waitFor(() => expect(updateMutate).toHaveBeenCalled());
    const body = updateMutate.mock.calls[0][0] as TripTypeWrite & { id: number };
    expect(body.id).toBe(2);
    // Blank means "this section asks for nothing"; an empty JSON object would accept anything
    // and publish a schema version for no reason.
    expect(body.logisticsSchema).toBeNull();
  });
});
