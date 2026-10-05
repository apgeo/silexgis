// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ExpeditionInfo, ExpeditionWrite } from '../../api/hooks.ts';

const createCamp = vi.fn();
const updateCamp = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useCavingGroups: () => ({ data: [{ id: 'club-1', name: 'Clubul Speo' }] }),
  useCreateExpedition: () => ({ mutateAsync: createCamp, isPending: false }),
  useUpdateExpedition: () => ({ mutateAsync: updateCamp, isPending: false }),
}));

// The working-area map builds an OpenLayers map against a container jsdom cannot size; the
// field is a form control like any other here, so a stub that reports the value it holds is
// what this test needs from it.
vi.mock('../../components/trips/TripGeometryField.tsx', () => ({
  default: ({ value }: { value?: unknown }) => (
    <div data-testid="expedition-working-area">{value ? 'a shape' : 'no shape'}</div>
  ),
}));

const { default: ExpeditionFormModal } = await import('./ExpeditionFormModal.tsx');

function camp(overrides: Partial<ExpeditionInfo> = {}): ExpeditionInfo {
  return {
    id: 'camp-1',
    name: 'Bihor summer camp',
    description: null,
    startDate: '2026-07-18',
    endDate: '2026-08-01',
    geom: null,
    ownerUserId: 'owner-1',
    cavingGroupId: null,
    visibility: 'cavingGroup',
    state: 'draft',
    publishedAt: null,
    createdAt: '2026-07-01T00:00:00Z',
    updatedAt: '2026-07-01T00:00:00Z',
    ...overrides,
  } as ExpeditionInfo;
}

/** The write body the modal handed its mutation, once the save has been awaited. */
async function savedBody(mutation: typeof createCamp): Promise<ExpeditionWrite> {
  fireEvent.click(screen.getByRole('button', { name: 'OK' }));
  await vi.waitFor(() => expect(mutation).toHaveBeenCalled());
  const call = mutation.mock.calls[0][0] as ExpeditionWrite | { body: ExpeditionWrite };
  return 'body' in call ? call.body : call;
}

function show(subject: ExpeditionInfo | null, onClose: (savedId?: string) => void = () => {}) {
  return render(
    <App>
      <ExpeditionFormModal open camp={subject} onClose={onClose} />
    </App>,
  );
}

describe('ExpeditionFormModal', () => {
  beforeEach(() => {
    createCamp.mockReset().mockResolvedValue({ id: 'new-camp' });
    updateCamp.mockReset().mockResolvedValue({ id: 'camp-1' });
  });
  afterEach(cleanup);

  it('creates a one-day camp today from a name alone, and hands the new id back', async () => {
    // The dates pre-fill today at both ends, and an end equal to its start is sent as no end at
    // all: the stored end means "and it ran on to", so a one-day camp must never be stored as
    // a range of itself.
    const onClose = vi.fn();
    show(null, onClose);
    fireEvent.change(screen.getByTestId('expedition-form-name'), {
      target: { value: 'Recce weekend' },
    });

    const body = await savedBody(createCamp);
    expect(body.name).toBe('Recce weekend');
    expect(body.startDate).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(body.endDate).toBeNull();
    expect(body.geom).toBeNull();
    expect(body.cavingGroupId).toBeNull();
    // A camp is what a club announces, so it starts readable by the club.
    expect(body.visibility).toBe('cavingGroup');
    await vi.waitFor(() => expect(onClose).toHaveBeenCalledWith('new-camp'));
  });

  it('edits an existing camp through the update route, keeping the range it ran on', async () => {
    show(camp());
    expect((screen.getByTestId('expedition-form-name') as HTMLInputElement).value).toBe(
      'Bihor summer camp',
    );
    fireEvent.change(screen.getByTestId('expedition-form-name'), {
      target: { value: 'Bihor summer camp 2026' },
    });

    const body = await savedBody(updateCamp);
    expect(updateCamp.mock.calls[0][0]).toMatchObject({ id: 'camp-1' });
    expect(body.name).toBe('Bihor summer camp 2026');
    expect(body.startDate).toBe('2026-07-18');
    expect(body.endDate).toBe('2026-08-01');
    expect(createCamp).not.toHaveBeenCalled();
  });

  it('refuses to save a camp with no name rather than sending one the server would refuse', async () => {
    show(null);
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    // The form's own required rule stops the submit; nothing reaches the mutation.
    await screen.findByText(/required/i);
    expect(createCamp).not.toHaveBeenCalled();
  });
});
