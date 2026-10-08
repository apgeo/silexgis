// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { ExpeditionSharing, ExpeditionSharingOutcome } from '../../api/hooks.ts';

const applyMutate = vi.fn();
const reapplyMutate = vi.fn();
const withdrawMutate = vi.fn();
let sharing: ExpeditionSharing = { memberTrips: 0, rules: [] };
let refused = false;

vi.mock('../../api/hooks.ts', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/hooks.ts')>();
  return {
    parseAccessActions: actual.parseAccessActions,
    useExpeditionSharing: () => ({ data: refused ? undefined : sharing, isError: refused }),
    useApplyExpeditionSharing: () => ({ mutateAsync: applyMutate, isPending: false }),
    useReapplyExpeditionSharing: () => ({ mutateAsync: reapplyMutate, isPending: false }),
    useWithdrawExpeditionSharing: () => ({ mutateAsync: withdrawMutate, isPending: false }),
    useCavingGroups: () => ({ data: [{ id: 'club-1', name: 'Partner Club' }] }),
    useUserSearch: () => ({ data: [] }),
  };
});

const { default: ExpeditionSharingModal } = await import('./ExpeditionSharingModal.tsx');

function show() {
  return render(
    <App>
      <MemoryRouter>
        <ExpeditionSharingModal expeditionId="camp-1" open onClose={() => {}} />
      </MemoryRouter>
    </App>,
  );
}

/** What an act answers when every one of the camp's trips took the sharing. */
function everyTrip(trips: number): ExpeditionSharingOutcome {
  return { sharing, sharedTrips: trips, skippedTrips: [], skippedTripsNotNamed: 0 };
}

/** Composes one rule for the partner club through the dialog's own controls. */
async function stagePartnerClub() {
  // The kind picker already says "Caving group"; the next combobox is the club.
  fireEvent.mouseDown(screen.getAllByRole('combobox')[1]);
  fireEvent.click(await screen.findByTitle('Partner Club'));
  fireEvent.click(screen.getByTestId('expedition-sharing-add'));
}

describe('ExpeditionSharingModal', () => {
  beforeEach(() => {
    sharing = { memberTrips: 0, rules: [] };
    applyMutate.mockReset().mockResolvedValue(everyTrip(0));
    reapplyMutate.mockReset().mockResolvedValue(everyTrip(0));
    withdrawMutate.mockReset().mockResolvedValue(undefined);
    refused = false;
  });
  afterEach(cleanup);

  it('says the camp is shared with nobody, and offers nothing to apply again or withdraw', () => {
    sharing = { memberTrips: 3, rules: [] };
    show();

    expect(screen.getByTestId('expedition-sharing-trips').textContent).toContain('3');
    expect(screen.getByText('This camp is not shared with anybody yet.')).toBeTruthy();
    expect(screen.getByTestId('expedition-sharing-reapply')).toBeDisabled();
    expect(screen.getByTestId('expedition-sharing-withdraw')).toBeDisabled();
  });

  it('shows each rule with how many of the trips carry it, and flags the ones left behind', () => {
    // Fewer trips carrying a rule than the camp has is the one state this dialog exists to make
    // visible: trips joined since the sharing was applied, and they are not covered.
    sharing = {
      memberTrips: 4,
      rules: [
        { subjectKind: 'cavingGroup', subjectId: 'club-1', subjectName: 'Partner Club', effect: 'allow', actions: 'read', trips: 4 },
        { subjectKind: 'user', subjectId: 'user-9', subjectName: 'Ana', effect: 'allow', actions: 'read, write', trips: 2 },
      ],
    };
    show();

    const coverage = screen.getAllByTestId('expedition-sharing-coverage');
    expect(coverage[0].textContent).toContain('4 of 4');
    expect(coverage[0].textContent).not.toContain('Not every trip');
    expect(coverage[1].textContent).toContain('2 of 4');
    expect(coverage[1].textContent).toContain('Not every trip');
    expect(screen.getByTestId('expedition-sharing-behind')).toBeTruthy();
    expect(screen.getByTestId('expedition-sharing-reapply')).not.toBeDisabled();
  });

  it('applies a composed rule onto every trip, reading by default', async () => {
    sharing = { memberTrips: 2, rules: [] };
    show();
    await stagePartnerClub();

    const drafts = screen.getByTestId('expedition-sharing-drafts');
    expect(within(drafts).getByText('Partner Club')).toBeTruthy();
    // Neither create nor exact location is offered: the first has no meaning on one trip and
    // the second the cascade refuses outright.
    expect(within(drafts).queryByText('Create')).toBeNull();
    expect(within(drafts).queryByText('Exact location')).toBeNull();

    fireEvent.click(screen.getByTestId('expedition-sharing-apply'));
    await vi.waitFor(() => expect(applyMutate).toHaveBeenCalledTimes(1));
    expect(applyMutate).toHaveBeenCalledWith([
      { subjectKind: 'cavingGroup', subjectId: 'club-1', effect: 'allow', actions: 'read' },
    ]);
  });

  it('says nothing was shared when no trip took it, with how many there were and never which', async () => {
    sharing = { memberTrips: 2, rules: [] };
    applyMutate.mockRejectedValue(
      new ApiError(403, 'access_cascade.incomplete', 'refused', { refusedTripCount: 2 }),
    );
    show();
    await stagePartnerClub();
    fireEvent.click(screen.getByTestId('expedition-sharing-apply'));

    await screen.findByText(/Not shared: you may not manage the permissions of any of this camp's trips \(2\)/);
    expect(screen.queryByTestId('expedition-sharing-skipped')).toBeNull();
  });

  it('lists the trips a sharing skipped, each with whose consent is missing, and only counts the ones it may not name', async () => {
    // The camp the change is for: two of the organiser's own trips, one lent by somebody who
    // has not delegated, one lent with its rules delegated but not the right being passed on,
    // and one the organiser cannot read at all — which the answer carries as a number.
    sharing = { memberTrips: 5, rules: [] };
    applyMutate.mockResolvedValue({
      sharing,
      sharedTrips: 2,
      skippedTrips: [
        { id: 'trip-lent', title: 'Lent by another club', reason: 'notAdministered' },
        { id: 'trip-wide', title: 'Delegated, but not this much', reason: 'beyondHolding' },
      ],
      skippedTripsNotNamed: 1,
    } satisfies ExpeditionSharingOutcome);
    show();
    await stagePartnerClub();
    fireEvent.click(screen.getByTestId('expedition-sharing-apply'));

    const panel = await screen.findByTestId('expedition-sharing-skipped');
    expect(panel.textContent).toContain('Trips shared: 2. Trips skipped: 3.');

    const named = within(panel).getAllByTestId('expedition-sharing-skipped-trip');
    expect(named).toHaveLength(2);
    expect(named[0].textContent).toContain('Lent by another club');
    expect(named[0].textContent).toContain('its owner must let you manage its permissions');
    // A named trip is one the organiser may open, so it is a way to it.
    expect(within(named[0]).getByRole('link').getAttribute('href')).toBe('/trip-logs/trip-lent');
    expect(named[1].textContent).toContain('you asked to pass on more than you hold');

    // The one they may not read: a count, in a sentence that says why there is no name.
    const unnamed = within(panel).getByTestId('expedition-sharing-skipped-unnamed');
    expect(unnamed.textContent).toContain('not named here: 1');

    // Said as a warning, not as "the camp is shared" — it is shared in part.
    await screen.findByText(/Trips shared: 2 of 5/);
    expect(screen.queryByText('The camp is shared.')).toBeNull();
  });

  it('says the camp is shared, and lists nothing, when every trip took it', async () => {
    sharing = { memberTrips: 2, rules: [] };
    applyMutate.mockResolvedValue(everyTrip(2));
    show();
    await stagePartnerClub();
    fireEvent.click(screen.getByTestId('expedition-sharing-apply'));

    await screen.findByText('The camp is shared.');
    expect(screen.queryByTestId('expedition-sharing-skipped')).toBeNull();
  });

  it('drops the list of skipped trips when applying again picks them all up', async () => {
    sharing = {
      memberTrips: 2,
      rules: [
        { subjectKind: 'cavingGroup', subjectId: 'club-1', subjectName: 'Partner Club', effect: 'allow', actions: 'read', trips: 1 },
      ],
    };
    reapplyMutate.mockResolvedValueOnce({
      sharing,
      sharedTrips: 1,
      skippedTrips: [{ id: 'trip-lent', title: 'Lent by another club', reason: 'notAdministered' }],
      skippedTripsNotNamed: 0,
    } satisfies ExpeditionSharingOutcome);
    reapplyMutate.mockResolvedValueOnce(everyTrip(2));
    show();

    fireEvent.click(screen.getByTestId('expedition-sharing-reapply'));
    await screen.findByTestId('expedition-sharing-skipped');

    // Its owner has delegated in the meantime; the same act now reaches it.
    fireEvent.click(screen.getByTestId('expedition-sharing-reapply'));
    await vi.waitFor(() => expect(screen.queryByTestId('expedition-sharing-skipped')).toBeNull());
    await screen.findByText('The sharing now covers every trip in the camp.');
  });

  it('re-applies and withdraws through their own acts', async () => {
    sharing = {
      memberTrips: 2,
      rules: [
        { subjectKind: 'cavingGroup', subjectId: 'club-1', subjectName: 'Partner Club', effect: 'allow', actions: 'read', trips: 1 },
      ],
    };
    show();

    fireEvent.click(screen.getByTestId('expedition-sharing-reapply'));
    await vi.waitFor(() => expect(reapplyMutate).toHaveBeenCalledTimes(1));

    fireEvent.click(screen.getByTestId('expedition-sharing-withdraw'));
    fireEvent.click(await screen.findByRole('button', { name: /OK/i }));
    await vi.waitFor(() => expect(withdrawMutate).toHaveBeenCalledTimes(1));
  });

  it('says plainly when the caller may not share the camp', () => {
    refused = true;
    show();
    expect(screen.getByText('You cannot share this camp.')).toBeTruthy();
  });
});
