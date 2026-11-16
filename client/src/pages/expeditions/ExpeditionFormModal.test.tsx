// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ExpeditionInfo, ExpeditionWrite } from '../../api/hooks.ts';
import { ApiError } from '../../api/client.ts';
import { noteVersionOfReading } from '../../api/readingVersion.ts';

const createCamp = vi.fn();
const updateCamp = vi.fn();

/** `reading` as a read answered at `version` hands it over: the version is kept with the values. */
function readAt<T extends object>(reading: T, version: string): T {
  noteVersionOfReading(reading, new Response(null, { headers: { ETag: version } }));
  return reading;
}

// The newest version filed for the camp's address, as the transport would answer it at this
// moment. A form must not take its version from here: the newest filed can be ahead of the copy
// the form was filled from.
const versionOnFile = vi.fn<(path: string) => string | undefined>(() => undefined);

vi.mock('../../api/client.ts', async (original) => ({
  ...(await original<typeof import('../../api/client.ts')>()),
  lastReadETag: (path: string) => versionOnFile(path),
}));

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
    versionOnFile.mockReset().mockReturnValue(undefined);
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

  it('sends the note for readers of published trips on every save, an untouched one included', async () => {
    // The server writes what a save gives it: a form that drew the note and left it out of what
    // it sends would take it off every published trip of a camp whose dates were corrected.
    show(camp({ publicNote: 'Figures are provisional.' }));
    expect(screen.getByTestId('expedition-form-public-note')).toHaveValue('Figures are provisional.');

    const body = await savedBody(updateCamp);
    expect(body.publicNote).toBe('Figures are provisional.');
  });

  it('sends an emptied note as none', async () => {
    show(camp({ publicNote: 'Figures are provisional.' }));
    fireEvent.change(screen.getByTestId('expedition-form-public-note'), { target: { value: '   ' } });

    const body = await savedBody(updateCamp);
    expect(body.publicNote).toBeNull();
  });

  it('refuses to save a camp with no name rather than sending one the server would refuse', async () => {
    show(null);
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    // The form's own required rule stops the submit; nothing reaches the mutation.
    await screen.findByText(/required/i);
    expect(createCamp).not.toHaveBeenCalled();
  });

  it('saves against the version it opened with, not the one a later read of the camp filed', async () => {
    // The page behind the dialog goes on reading the camp — on a return to the browser tab, after
    // a state move — and each read files a newer version. Sending that one with the values the
    // form was filled from would present somebody else's change as already seen and write over it.
    // A read made outside the cache has filed a version newer than either copy for the address.
    versionOnFile.mockImplementation((path) => (path === '/api/v1/expeditions/camp-1' ? '"3"' : undefined));
    const onClose = vi.fn();
    const view = render(
      <App>
        <ExpeditionFormModal open camp={readAt(camp(), '"1"')} onClose={onClose} />
      </App>,
    );

    view.rerender(
      <App>
        <ExpeditionFormModal
          open
          camp={readAt(camp({ name: 'Renamed by somebody else' }), '"2"')}
          onClose={onClose}
        />
      </App>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    await vi.waitFor(() => expect(updateCamp).toHaveBeenCalled());
    const sent = updateCamp.mock.calls[0][0] as { ifMatch?: string; body: ExpeditionWrite };
    expect(sent.ifMatch).toBe('"1"');
    expect(sent.body.name).toBe('Bihor summer camp');
  });

  it('leaves a refusal for a stale version to the one conflict warning and keeps the form open', async () => {
    // The application words a conflict once, for every save; a second, generic "failed" from the
    // form would say the opposite of what to do — the values are fine, the camp moved on.
    updateCamp.mockRejectedValue(
      new ApiError(412, 'concurrency.version_mismatch', 'Changed since you opened it.'),
    );
    const onClose = vi.fn();
    show(camp(), onClose);

    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    await vi.waitFor(() => expect(updateCamp).toHaveBeenCalled());
    // A message is drawn a moment after it is asked for; this is long enough for one to appear.
    await new Promise((resolve) => setTimeout(resolve, 100));
    expect(screen.queryByText('The operation failed. Please try again.')).toBeNull();
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByTestId('expedition-form-name')).toHaveValue('Bihor summer camp');
  });
});
