// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type {
  ExpeditionRosterEntry,
  ExpeditionRosterEntryWrite,
  ExpeditionRosterRole,
} from '../../api/hooks.ts';

/**
 * The dialog a stay is recorded and corrected through. What is pinned is the body it sends: who
 * the stay names, and in which of the two ways — because a row that says who in the wrong one of
 * them either overwrites a person with another or adds somebody the club already has.
 */

const createStay = vi.fn();
const updateStay = vi.fn();

const roles: ExpeditionRosterRole[] = [
  { id: 4, code: 'member', name: 'Member', description: null, sortOrder: 1, isSeeded: true },
  { id: 1, code: 'cook', name: 'Cook', description: null, sortOrder: 3, isSeeded: true },
];

vi.mock('../../api/hooks.ts', () => ({
  useExpeditionRosterRoles: () => ({ data: roles }),
  useCreateExpeditionRosterEntry: () => ({ mutateAsync: createStay, isPending: false }),
  useUpdateExpeditionRosterEntry: () => ({ mutateAsync: updateStay, isPending: false }),
  useCavers: () => ({ data: [{ id: 'caver-ana', name: 'Ana Pop', cavingGroups: [] }] }),
}));

// Asked at once rather than a little behind the typing, so nothing here waits on a timer.
vi.mock('../../hooks/useDebouncedValue.ts', () => ({
  useDebouncedValue: (value: string) => value,
}));

const { default: ExpeditionStayModal } = await import('./ExpeditionStayModal.tsx');

const CAMP = '77777777-8888-9999-aaaa-bbbbbbbbbbbb';

function stay(overrides: Partial<ExpeditionRosterEntry> = {}): ExpeditionRosterEntry {
  return {
    id: 7,
    expeditionId: CAMP,
    caverId: 'caver-1',
    caverName: 'Ana P.',
    roleId: 1,
    fromDate: '2026-07-20',
    toDate: '2026-07-26',
    note: 'Arrived two days late.',
    createdAt: '2026-07-01T00:00:00Z',
    updatedAt: '2026-07-01T00:00:00Z',
    ...overrides,
  };
}

function show(
  entry: ExpeditionRosterEntry | null,
  { campEnd = '2026-08-01', onClose = () => {} }: { campEnd?: string | null; onClose?: () => void } = {},
) {
  return render(
    <App>
      <ExpeditionStayModal
        open
        expeditionId={CAMP}
        campStart="2026-07-18"
        campEnd={campEnd}
        entry={entry}
        onClose={onClose}
      />
    </App>,
  );
}

const who = () => screen.getByTestId('expedition-stay-person') as HTMLInputElement;
const typeWho = (text: string) => fireEvent.change(who(), { target: { value: text } });
const press = () => fireEvent.click(screen.getByRole('button', { name: 'OK' }));

/** The body the dialog handed the create route, once the save has been awaited. */
async function created(): Promise<ExpeditionRosterEntryWrite> {
  press();
  await vi.waitFor(() => expect(createStay).toHaveBeenCalled());
  return createStay.mock.calls[0][0] as ExpeditionRosterEntryWrite;
}

/** The stay the dialog corrected and the body it sent for it. */
async function corrected(): Promise<{ entryId: number; body: ExpeditionRosterEntryWrite }> {
  press();
  await vi.waitFor(() => expect(updateStay).toHaveBeenCalled());
  return updateStay.mock.calls[0][0] as { entryId: number; body: ExpeditionRosterEntryWrite };
}

describe('recording a stay', () => {
  beforeEach(() => {
    createStay.mockReset().mockResolvedValue(stay());
    updateStay.mockReset().mockResolvedValue(stay());
  });
  afterEach(cleanup);

  it('sends a name that was typed in as a name, for the whole camp, in the ordinary role', async () => {
    const onClose = vi.fn();
    show(null, { onClose });
    typeWho('  Vasile Bucătarul ');

    expect(await created()).toEqual({
      caverId: null,
      newCaverName: 'Vasile Bucătarul',
      // Most people at a camp were simply there, for all of it: a dialog nobody touched beyond
      // the name records exactly that.
      roleId: 4,
      fromDate: '2026-07-18',
      toDate: '2026-08-01',
      note: null,
    });
    await vi.waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(updateStay).not.toHaveBeenCalled();
  });

  it('sends somebody chosen from the directory by their entry, in the role chosen', async () => {
    show(null);
    typeWho('Ana');
    fireEvent.click(document.querySelector('.ant-select-item-option[title="Ana Pop"]')!);

    const role = screen.getByTestId('expedition-stay-role');
    fireEvent.mouseDown(role.querySelector('.ant-select-selector') ?? role);
    fireEvent.click(document.querySelector('.ant-select-item-option[title="Cook"]')!);

    const body = await created();
    expect(body.caverId).toBe('caver-ana');
    expect(body.newCaverName).toBeNull();
    expect(body.roleId).toBe(1);
  });

  it('records one day with no end on a camp of one day', async () => {
    // The stay starts out as the camp's own days, and the camp's convention is the stay's: an
    // end equal to the first day is sent as no end at all, so one day never reads as a range.
    show(null, { campEnd: null });
    typeWho('Day visitor');

    const body = await created();
    expect(body.fromDate).toBe('2026-07-18');
    expect(body.toDate).toBeNull();
  });

  it('refuses to send a stay that says nobody was there', async () => {
    show(null);
    press();

    expect(await screen.findByText('Say who was there')).toBeTruthy();
    expect(createStay).not.toHaveBeenCalled();
  });

  it('treats a name of nothing but spaces as no name', async () => {
    show(null);
    typeWho('   ');
    press();

    expect(await screen.findByText('Say who was there')).toBeTruthy();
    expect(createStay).not.toHaveBeenCalled();
  });
});

describe('correcting a stay', () => {
  beforeEach(() => {
    createStay.mockReset().mockResolvedValue(stay());
    updateStay.mockReset().mockResolvedValue(stay());
  });
  afterEach(cleanup);

  it('opens on the stay as it stands and sends it back about the same person', async () => {
    // Opened and saved without touching the name. The name on screen is the label the roster
    // showed, which need not be the name the directory records, so the row has to go back as
    // the entry it came with — sent as a name it would make a second person out of the first.
    show(stay());
    expect(who().value).toBe('Ana P.');
    fireEvent.change(screen.getByTestId('expedition-stay-note'), {
      target: { value: '  Arrived three days late. ' },
    });

    expect(await corrected()).toEqual({
      entryId: 7,
      body: {
        caverId: 'caver-1',
        newCaverName: null,
        roleId: 1,
        fromDate: '2026-07-20',
        toDate: '2026-07-26',
        note: 'Arrived three days late.',
      },
    });
    expect(createStay).not.toHaveBeenCalled();
  });

  it('sends whoever the new text names once the name is typed over', async () => {
    // The opposite mistake: keeping the entry beside a corrected name stores nothing at all,
    // because the entry is what the write is read by.
    show(stay());
    typeWho('Somebody Else');

    const { body } = await corrected();
    expect(body.caverId).toBeNull();
    expect(body.newCaverName).toBe('Somebody Else');
  });

  it('shows a one-day stay as that day and sends it back with no end', async () => {
    show(stay({ toDate: null }));

    const { body } = await corrected();
    expect(body.fromDate).toBe('2026-07-20');
    expect(body.toDate).toBeNull();
  });

  it('clears a note that was emptied rather than keeping the old one', async () => {
    show(stay());
    fireEvent.change(screen.getByTestId('expedition-stay-note'), { target: { value: '' } });

    expect((await corrected()).body.note).toBeNull();
  });

  it('says so in its own words when the person is no longer in the directory', async () => {
    // The entry the row was holding was merged away or removed after the roster was read. That
    // is a refusal somebody can act on — choose them again — so it is not folded into "could
    // not be saved".
    updateStay.mockRejectedValue(new ApiError(400, 'expedition_roster.caver_unknown'));
    const onClose = vi.fn();
    show(stay(), { onClose });
    press();

    expect(await screen.findByText(/no longer in the list of cavers/)).toBeTruthy();
    // And the dialog stays open on what was typed, so the correction is not lost with it.
    expect(onClose).not.toHaveBeenCalled();
    expect(who().value).toBe('Ana P.');
  });

  it('stays open and says the save failed on any other refusal', async () => {
    updateStay.mockRejectedValue(new ApiError(403));
    const onClose = vi.fn();
    show(stay(), { onClose });
    press();

    await vi.waitFor(() => expect(updateStay).toHaveBeenCalled());
    await vi.waitFor(() =>
      expect(document.querySelector('.ant-message-error')).not.toBeNull(),
    );
    expect(screen.queryByText(/no longer in the list of cavers/)).toBeNull();
    expect(onClose).not.toHaveBeenCalled();
  });
});
