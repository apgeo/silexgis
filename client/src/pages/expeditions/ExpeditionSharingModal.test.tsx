// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { ExpeditionSharing } from '../../api/hooks.ts';

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
      <ExpeditionSharingModal expeditionId="camp-1" open onClose={() => {}} />
    </App>,
  );
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
    applyMutate.mockReset().mockResolvedValue(sharing);
    reapplyMutate.mockReset().mockResolvedValue(sharing);
    withdrawMutate.mockReset().mockResolvedValue(undefined);
    sharing = { memberTrips: 0, rules: [] };
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

  it('tells the organiser how many trips refused the sharing, and never which', async () => {
    sharing = { memberTrips: 3, rules: [] };
    applyMutate.mockRejectedValue(
      new ApiError(403, 'access_cascade.incomplete', 'refused', { refusedTripCount: 2 }),
    );
    show();
    await stagePartnerClub();
    fireEvent.click(screen.getByTestId('expedition-sharing-apply'));

    await screen.findByText(/2 of this camp's trips refused it/);
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
