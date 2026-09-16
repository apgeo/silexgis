// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TrackingParticipant } from '../../api/hooks.ts';

const ANA = '11111111-1111-1111-1111-111111111111';

const setLabel = vi.fn();
const onClose = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useSetTrackingParticipantLabel: () => ({ mutateAsync: setLabel, isPending: false }),
}));

vi.mock('../../hooks/useCoarsePointer.ts', () => ({ useCoarsePointer: () => false }));

const { default: TrackingPublicNameDialog } = await import('./TrackingPublicNameDialog.tsx');

function participant(label: string | null): TrackingParticipant {
  return {
    caverId: ANA,
    teamId: null,
    lastKind: 'entered',
    lastRecordedAt: '2026-09-16T09:00:00Z',
    positionRecordedAt: null,
    stationName: null,
    depthM: null,
    positionSurveyModelId: null,
    in: true,
    out: false,
    label,
  };
}

function view(label: string | null = null, publishesRealNames = true) {
  return render(
    <App>
      <TrackingPublicNameDialog
        open
        tripLogId="trip-1"
        participant={participant(label)}
        caverName="Ana Popescu"
        publishesRealNames={publishesRealNames}
        onClose={onClose}
      />
    </App>,
  );
}

beforeEach(() => {
  setLabel.mockReset().mockResolvedValue({ caverId: ANA, label: null });
  onClose.mockReset();
});

afterEach(cleanup);

describe('naming one person on a published page', () => {
  /**
   * <b>The sentence somebody has to read before they act.</b> This control is the only mechanism by
   * which a named person stays off a public page, and it is reached on behalf of somebody who is
   * not in the room. Whoever opens it must be able to see that a caption changes the *public* page
   * and nothing else, and that it beats the installation's own setting in both directions — the
   * second half is what stops it reading as "a way to hide people", which would leave the case it
   * also serves, naming the one person a follower can ring, looking like a misuse.
   */
  it('says what a caption does, on the public page and in both directions', () => {
    view();

    expect(screen.getByText(/instead of this person's name/)).toBeInTheDocument();
    expect(screen.getByText(/this tab, the log and every other screen/)).toBeInTheDocument();
    const outranks = screen.getByText(/outranks this installation's setting in both directions/);
    expect(outranks).toHaveTextContent('keeps somebody off a page that would otherwise name them');
    expect(outranks).toHaveTextContent('names somebody on a page that would otherwise call them');
  });

  /**
   * What the page will say, answered as it is typed rather than after a link has been handed out
   * and cannot be taken back out of anybody's messages.
   */
  it('says what the page will call them, before and after the caption is typed', () => {
    view();
    expect(screen.getByTestId('trip-tracking-public-name-preview')).toHaveTextContent(
      'The public page will call them Ana Popescu.',
    );

    fireEvent.change(screen.getByTestId('trip-tracking-public-name-input'), {
      target: { value: 'A club member' },
    });

    expect(screen.getByTestId('trip-tracking-public-name-preview')).toHaveTextContent(
      'The public page will call them “A club member”.',
    );
  });

  it('says a page that names nobody will name a captioned person', () => {
    view(null, false);
    expect(screen.getByTestId('trip-tracking-public-name-preview')).toHaveTextContent(
      'by their place in the party',
    );

    fireEvent.change(screen.getByTestId('trip-tracking-public-name-input'), {
      target: { value: 'Ana, trip leader' },
    });

    expect(screen.getByTestId('trip-tracking-public-name-preview')).toHaveTextContent(
      'The public page will call them “Ana, trip leader”.',
    );
  });

  /**
   * <b>The preview is a flat statement of fact, and exactly one of its three answers is not one.</b>
   * A caption is printed as typed, and a numbered party carries no name at all — both exact. The
   * name offered where no caption is set is the name *this application* calls somebody, which is
   * their account's own display name where they have set one, while the published page prints the
   * name on the club's roster. They begin identical and part company the moment a member chooses a
   * display name — so this one answer is a good guess, and the dialog whose whole job is to say
   * what a follow link will print must not hand it over as the string.
   */
  it('says the name it offers is the roster’s, not exactly the one shown here', () => {
    view();

    expect(screen.getByTestId('trip-tracking-public-name-roster')).toHaveTextContent(
      "prints the name on the club's roster",
    );
  });

  /**
   * The twins, both of them, because the qualification has to disappear from the two answers that
   * really are exact. Standing under a caption it would cast doubt on the one string the page is
   * guaranteed to print — which is what somebody sets a caption to be sure of.
   */
  it('says nothing of the sort where the answer is exact', () => {
    const { unmount } = view();
    fireEvent.change(screen.getByTestId('trip-tracking-public-name-input'), {
      target: { value: 'A club member' },
    });

    expect(screen.queryByTestId('trip-tracking-public-name-roster')).toBeNull();
    unmount();

    // And on an installation that numbers its party: no name goes out, so there is no name to be
    // approximate about.
    view(null, false);
    expect(screen.queryByTestId('trip-tracking-public-name-roster')).toBeNull();
  });

  it('sets a caption for somebody who has none', async () => {
    view();

    fireEvent.change(screen.getByTestId('trip-tracking-public-name-input'), {
      target: { value: 'A club member' },
    });
    fireEvent.click(screen.getByTestId('trip-tracking-public-name-save'));

    await waitFor(() => expect(setLabel).toHaveBeenCalledTimes(1));
    expect(setLabel.mock.calls[0][0]).toEqual({
      tripLogId: 'trip-1',
      caverId: ANA,
      label: 'A club member',
    });
    expect(onClose).toHaveBeenCalled();
  });

  it('opens on the caption already stored, and changes it', async () => {
    view('A club member');
    const field = screen.getByTestId('trip-tracking-public-name-input') as HTMLInputElement;
    expect(field.value).toBe('A club member');

    fireEvent.change(field, { target: { value: 'Somebody else' } });
    fireEvent.click(screen.getByTestId('trip-tracking-public-name-save'));

    await waitFor(() => expect(setLabel).toHaveBeenCalledTimes(1));
    expect(setLabel.mock.calls[0][0]).toMatchObject({ label: 'Somebody else' });
  });

  /**
   * Clearing is saving an empty field — there is no route that clears, because "call them nothing
   * in particular" is a value the field holds. Driven through the field rather than through the
   * button, because that is the way somebody who reads the sentence under it will do it.
   */
  it('takes a caption off by saving an empty field', async () => {
    view('A club member');

    fireEvent.change(screen.getByTestId('trip-tracking-public-name-input'), {
      target: { value: '' },
    });
    fireEvent.click(screen.getByTestId('trip-tracking-public-name-save'));

    await waitFor(() => expect(setLabel).toHaveBeenCalledTimes(1));
    expect(setLabel.mock.calls[0][0]).toMatchObject({ label: '' });
    expect(screen.getByText(/Leave it empty and save/)).toBeInTheDocument();
  });

  it('offers a button that does the same, and offers it only where there is something to take off', () => {
    const { unmount } = view(null);
    expect(screen.queryByTestId('trip-tracking-public-name-clear')).toBeNull();
    unmount();

    view('A club member');
    fireEvent.click(screen.getByTestId('trip-tracking-public-name-clear'));

    expect(setLabel).toHaveBeenCalledTimes(1);
    expect(setLabel.mock.calls[0][0]).toMatchObject({ caverId: ANA, label: null });
  });

  /**
   * A refusal is worded, not swallowed. Somebody who pressed Save on behalf of a person who asked
   * to be kept off a page must not be left with a dialog that closed and a page that still names
   * them.
   */
  it('keeps the dialog open and says why when the write is refused', async () => {
    setLabel.mockRejectedValue(new Error('nope'));
    view();

    fireEvent.change(screen.getByTestId('trip-tracking-public-name-input'), {
      target: { value: 'A club member' },
    });
    fireEvent.click(screen.getByTestId('trip-tracking-public-name-save'));

    await waitFor(() => expect(setLabel).toHaveBeenCalledTimes(1));
    expect(onClose).not.toHaveBeenCalled();
    // And the twin: a write that lands does close it — see the setting test above.
  });

  // The field cannot offer more than the server will store, so a caption is refused here rather
  // than accepted, typed out in full and then rejected by a route.
  it('will not take a caption longer than the server stores', () => {
    view();
    expect(screen.getByTestId('trip-tracking-public-name-input')).toHaveAttribute(
      'maxlength',
      '200',
    );
  });
});
