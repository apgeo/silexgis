// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// A read of the list that did not land, switched on by the test that needs it.
const listRead = vi.hoisted(() => {
  const refetch = vi.fn();
  return {
    failed: false,
    refetch,
    answer: () => ({ data: undefined, isLoading: false, isError: true, error: new TypeError('offline'), refetch }),
  };
});
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { TripTypeWrite } from '../../api/hooks.ts';
import { afterRejectionsAreReported } from '../../testEventLoop.ts';

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
    useTripTypes: () => (listRead.failed ? listRead.answer() : { data: tripTypes, isLoading: false }),
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

  it('Save with the name emptied marks the field, sends nothing and leaves no unhandled rejection', async () => {
    const unhandled: unknown[] = [];
    const record = (reason: unknown) => unhandled.push(reason);
    process.on('unhandledRejection', record);
    try {
      show();
      fireEvent.click(screen.getAllByRole('button', { name: 'Edit' })[1]);
      const name = screen.getByLabelText('Name');
      fireEvent.change(name, { target: { value: '' } });
      fireEvent.click(screen.getByRole('button', { name: 'Save' }));
      await vi.waitFor(() => expect(name.getAttribute('aria-invalid')).toBe('true'));
      await afterRejectionsAreReported();
      expect(updateMutate).not.toHaveBeenCalled();
      expect(unhandled).toEqual([]);
    } finally {
      process.off('unhandledRejection', record);
    }
  });
});

describe('a failed read of the list', () => {
  it('says the read failed instead of showing an empty list, and offers Retry', () => {
    listRead.failed = true;
    try {
      show();
      expect(screen.getByTestId('trip-types-load-failed')).toBeTruthy();
      fireEvent.click(screen.getByRole('button', { name: i18n.t('common.retry') }));
      expect(listRead.refetch).toHaveBeenCalledTimes(1);
    } finally {
      listRead.failed = false;
      listRead.refetch.mockReset();
    }
  });
});

describe('leaving the TripTypes dialog with typed work', () => {
  afterEach(cleanup);

  it('closes at once when nothing was typed', async () => {
    show();
    fireEvent.click(screen.getAllByRole('button', { name: 'Edit' })[1]);
    fireEvent.click(await screen.findByRole('button', { name: 'Cancel' }));
    // A guard against over-asking: nothing was typed, so the dialog closes. Its leaving animation
    // is the witness: a question would have kept it open.
    await waitFor(() => expect(document.querySelector('.ant-modal')?.className).toContain('leave'));
    expect(document.querySelectorAll('.ant-modal')).toHaveLength(1);
    expect(screen.queryByText('Discard your changes?')).toBeNull();
  });

  it('asks before throwing typed work away, and keeps it on "keep editing"', async () => {
    show();
    fireEvent.click(screen.getAllByRole('button', { name: 'Edit' })[1]);
    await waitFor(() => expect(screen.getByLabelText('Field data schema (JSON Schema)')).toBeTruthy());
    fireEvent.change(screen.getByLabelText('Field data schema (JSON Schema)'), { target: { value: '{"type":"object"}' } });
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect((await screen.findAllByText('Discard your changes?')).length).toBeGreaterThan(0);
    fireEvent.click(screen.getAllByRole('button', { name: 'Keep editing' }).at(-1)!);
    expect(screen.getByLabelText('Field data schema (JSON Schema)')).toHaveValue('{"type":"object"}');
  });

  it('is not closed by a click beside it', async () => {
    show();
    fireEvent.click(screen.getAllByRole('button', { name: 'Edit' })[1]);
    await waitFor(() => expect(screen.getByLabelText('Field data schema (JSON Schema)')).toBeTruthy());
    fireEvent.change(screen.getByLabelText('Field data schema (JSON Schema)'), { target: { value: '{"type":"object"}' } });
    const wrap = document.querySelector('.ant-modal-wrap') as HTMLElement;
    fireEvent.mouseDown(wrap);
    fireEvent.mouseUp(wrap);
    fireEvent.click(wrap);
    // A click beside must not even reach the dialog's cancel handler: that handler asks about
    // typed work, so a question raised now would mean the click was routed, not let go.
    expect(screen.queryByText('Discard your changes?')).toBeNull();
    expect(document.querySelectorAll('.ant-modal-confirm')).toHaveLength(0);
    // The witness that the click beside was let go: Cancel, pressed next, asks about the typed
    // work, which only a dialog still open and still holding that work does.
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect((await screen.findAllByText('Discard your changes?')).length).toBeGreaterThan(0);
    expect(screen.getByLabelText('Field data schema (JSON Schema)')).toHaveValue('{"type":"object"}');
    // A close starts the dialog's leaving animation; it must not have begun.
    expect(document.querySelector('.ant-modal')?.className).not.toContain('leave');
  });
});

describe('TripTypesPage deleting an entry', () => {
  /** Presses Delete on the one deletable row and confirms in the question that follows. */
  async function deleteAndConfirm() {
    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    // The confirmation's own button carries the same word; the one in the popover is the last.
    const confirm = await screen.findAllByRole('button', { name: /^(Delete|OK|Yes)$/ });
    fireEvent.click(confirm[confirm.length - 1]);
  }

  it('deletes the entry the confirmation was for', async () => {
    show();
    await deleteAndConfirm();
    await waitFor(() => expect(deleteMutate).toHaveBeenCalledWith(2));
  });

  it('says why an entry still in use cannot be deleted, rather than a bare failure', async () => {
    deleteMutate.mockRejectedValue(new ApiError(409, 'trip_type.in_use'));
    show();
    await deleteAndConfirm();

    expect(await screen.findByText(i18n.t('admin.tripTypes.inUse'))).toBeInTheDocument();
    expect(screen.queryByText(i18n.t('common.deleteFailed'))).toBeNull();
    expect(screen.queryByText(i18n.t('common.saveFailed'))).toBeNull();
  });
});
