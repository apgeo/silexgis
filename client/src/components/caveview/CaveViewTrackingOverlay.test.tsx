// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type { TrackedCaver } from '../../caveview/trackedCavers.ts';
import CaveViewTrackingOverlay, { type TrackedPlace } from './CaveViewTrackingOverlay.tsx';

const caver = (overrides: Partial<TrackedCaver> = {}): TrackedCaver => ({
  caverId: 'caver-1',
  name: 'Ana',
  teamId: null,
  teamTitle: 'Echipa 1',
  position: { kind: 'station', station: 'pestera.galerie.7' },
  lastRecordedAt: '2026-09-12T09:00:00Z',
  enteredAt: '2026-09-12T08:00:00Z',
  out: false,
  ...overrides,
  // Follows the latest report unless a test separates them: a position is usually as old as the
  // last thing somebody said, because the last thing they said was where they were.
  positionAt:
    'positionAt' in overrides ? (overrides.positionAt ?? null) : (overrides.lastRecordedAt ?? '2026-09-12T09:00:00Z'),
});

/** Every place the list has asked the viewer to show, in the order it asked. */
const asked: (TrackedPlace | null)[] = [];

/** The overlay with the panel's own state around it, which is where the open card lives. */
function Harness({ cavers }: { cavers: readonly TrackedCaver[] }) {
  const [open, setOpen] = useState<string | null>(null);
  const [times, setTimes] = useState(false);
  const [labels, setLabels] = useState(true);
  const [shown, setShown] = useState<TrackedPlace | null>(null);
  return (
    <CaveViewTrackingOverlay
      cavers={cavers}
      showTimes={times}
      onShowTimesChange={setTimes}
      showLabels={labels}
      onShowLabelsChange={setLabels}
      openCaverId={open}
      onOpenCaver={setOpen}
      shown={shown}
      onShow={(place) => {
        asked.push(place);
        setShown(place);
      }}
    />
  );
}

afterEach(() => {
  cleanup();
  asked.length = 0;
});

describe('CaveViewTrackingOverlay', () => {
  it('lists a withheld position as withheld rather than leaving the person out', () => {
    // The rule this surface exists to keep: somebody whose position was kept from this reader has
    // no marker, so the list is the only place they appear at all — and an absence there would
    // read as nobody knowing where they are.
    render(
      <Harness
        cavers={[
          caver({ caverId: 'a', name: 'Ana', position: { kind: 'withheld', certain: true } }),
          caver({ caverId: 'b', name: 'Bogdan', position: { kind: 'withheld', certain: false } }),
          caver({ caverId: 'c', name: 'Cora', position: { kind: 'unreported' } }),
        ]}
      />,
    );

    expect(screen.getByTestId('caveview-position-withheld')).toHaveTextContent('Not shown to you');
    expect(screen.getByTestId('caveview-position-maybe-withheld')).toHaveTextContent(
      'Not shown, or not reported',
    );
    expect(screen.getByTestId('caveview-caver-c')).toBeInTheDocument();
  });

  it('opens and closes a caver’s card from a tap, with no hover anywhere in it', () => {
    // The marker hover this card also opens never happens on a touchscreen, so the row is the
    // path that has to work: tap to open, a button to close, and Escape for the keyboard.
    render(<Harness cavers={[caver()]} />);

    fireEvent.click(screen.getByTestId('caveview-caver-caver-1'));
    const card = screen.getByTestId('caveview-caver-card');
    expect(card).toHaveTextContent('Echipa 1');
    expect(card).toHaveTextContent('pestera.galerie.7');

    fireEvent.click(screen.getByTestId('caveview-caver-card-close'));
    expect(screen.queryByTestId('caveview-caver-card')).not.toBeInTheDocument();

    fireEvent.click(screen.getByTestId('caveview-caver-caver-1'));
    fireEvent.keyDown(screen.getByTestId('caveview-caver-card'), { key: 'Escape' });
    expect(screen.queryByTestId('caveview-caver-card')).not.toBeInTheDocument();
  });

  it('marks the row of somebody reported out, not only the card behind it', () => {
    // The list is the one place the whole party is visible at once, and out-ness used to be
    // reachable from it only by opening each caver's card in turn — which on a crowded station is
    // out-ness nobody checks. The model says it on the marker; this is the surface beside the
    // model, and the two have to agree about who is still underground.
    render(
      <Harness
        cavers={[
          caver({ caverId: 'a', name: 'Ana' }),
          caver({ caverId: 'b', name: 'Bogdan', out: true }),
        ]}
      />,
    );

    expect(screen.getByTestId('caveview-row-out-b')).toHaveTextContent('Out');
    expect(screen.queryByTestId('caveview-row-out-a')).toBeNull();
  });

  /**
   * <b>The card carries two moments, and they answer different questions.</b> "Last update" moves
   * whenever anybody says anything at all about somebody — a radio check, a note, coming out — and
   * none of those says where they are. The place on the card keeps the moment of the report that
   * named it. A card that showed only the first dated a station heard at nine as five minutes old,
   * on the surface somebody reads while deciding whether a team is overdue.
   */
  it('dates the place on the card from the report that named it, not from the last word', () => {
    render(
      <Harness
        cavers={[
          caver({
            position: { kind: 'station', station: 'pestera.galerie.7' },
            lastRecordedAt: '2026-09-12T11:55:00Z',
            positionAt: '2026-09-12T09:00:00Z',
          }),
        ]}
      />,
    );

    fireEvent.click(screen.getByTestId('caveview-caver-caver-1'));
    const positionAt = screen.getByTestId('caveview-caver-card-position-at');

    // Written out in the reader's own locale, so the moment is compared rather than the wording:
    // the row carries the report that placed somebody at nine and not the word that arrived at
    // five to twelve.
    expect(positionAt.textContent).toBe(
      new Date('2026-09-12T09:00:00Z').toLocaleString(i18n.language),
    );
    expect(positionAt.textContent).not.toBe(
      new Date('2026-09-12T11:55:00Z').toLocaleString(i18n.language),
    );
    // The twins: the last word is still on the card under its own label, so the two moments are
    // drawn as two rather than one replacing the other.
    expect(screen.getByTestId('caveview-caver-card-last-heard').textContent).toBe(
      new Date('2026-09-12T11:55:00Z').toLocaleString(i18n.language),
    );
    expect(screen.getByTestId('caveview-caver-card')).toHaveTextContent('Position reported');
  });

  /**
   * <b>Which moment a reader binds to the station is decided by the label and by what is next to
   * it, and this card was getting both wrong.</b> The row carrying the last word said "Last update"
   * — the phrasing that reads as "this is how current the thing beside it is" — and it sat
   * immediately above the station. So a coordinator opening a marker read `Last update 11:55` on
   * the line over `pestera.galerie.7`, which is the exact sentence the position's own moment was
   * added to stop the card saying, in the place they look to decide whether a team is overdue.
   *
   * Two things close it and both are asserted here: the row is named for what it actually is, in
   * the words the tracking tab and the followed page already use for it, and it is drawn below the
   * place-and-its-moment pair rather than above the place. Neither is cosmetic — a label is what a
   * reader is told the number means, and adjacency is what they believe when the two disagree.
   */
  it('names the last word for what it is and keeps it away from the station it does not date', () => {
    render(
      <Harness
        cavers={[
          caver({
            position: { kind: 'station', station: 'pestera.galerie.7' },
            lastRecordedAt: '2026-09-12T11:55:00Z',
            positionAt: '2026-09-12T09:00:00Z',
          }),
        ]}
      />,
    );

    fireEvent.click(screen.getByTestId('caveview-caver-caver-1'));
    const card = screen.getByTestId('caveview-caver-card');

    // The wording the other two surfaces use for this moment, and not the one that reads as dating
    // whatever is beside it.
    expect(card).toHaveTextContent('Last heard');
    expect(card).not.toHaveTextContent('Last update');

    // And the order, read off the rendered card rather than assumed: the place, then the moment
    // that placed it, then the last word under both. What must never come back is the last word
    // sitting between "Went in" and the station.
    const labels = Array.from(
      card.querySelectorAll('.caveview-tracking-card-label'),
      (node) => node.textContent,
    );
    expect(labels.indexOf('Position')).toBeLessThan(labels.indexOf('Position reported'));
    expect(labels.indexOf('Position reported')).toBeLessThan(labels.indexOf('Last heard'));
  });

  /**
   * <b>A withheld position gets no moment, whatever the watch carries.</b> A replay reads the very
   * report whose place was removed, so it knows when that report was made — and printing it would
   * tell a reader that a position exists and when it was reported, which is half of what was being
   * kept from them. The row is drawn and says nothing, exactly as silence is drawn everywhere else.
   */
  it('gives a withheld position no moment, even when the watch knows one', () => {
    render(
      <Harness
        cavers={[
          caver({
            position: { kind: 'withheld', certain: true },
            lastRecordedAt: '2026-09-12T11:55:00Z',
            positionAt: '2026-09-12T09:00:00Z',
          }),
        ]}
      />,
    );

    fireEvent.click(screen.getByTestId('caveview-caver-caver-1'));

    expect(screen.getByTestId('caveview-caver-card-position-at')).toHaveTextContent('—');
    // And the twin, so this cannot pass by the row never saying anything: a place that was drawn
    // is dated.
    cleanup();
    render(
      <Harness
        cavers={[caver({ positionAt: '2026-09-12T09:00:00Z', lastRecordedAt: '2026-09-12T11:55:00Z' })]}
      />,
    );
    fireEvent.click(screen.getByTestId('caveview-caver-caver-1'));
    expect(screen.getByTestId('caveview-caver-card-position-at')).not.toHaveTextContent('—');
  });

  it('says why a position is missing where there is room to say it', () => {
    render(<Harness cavers={[caver({ position: { kind: 'withheld', certain: true }, out: true })]} />);

    fireEvent.click(screen.getByTestId('caveview-caver-caver-1'));

    const card = screen.getByTestId('caveview-caver-card');
    expect(card).toHaveTextContent('This position exists and you may not be told it');
    // Somebody reported out is marked as out: their last position is where they were.
    expect(screen.getByTestId('caveview-caver-out')).toBeInTheDocument();
  });

  /**
   * The card is what the reader asked for, and on a short screen it is what they get.
   *
   * The panel this overlay stands in is a share of the viewport, so a phone held sideways gives it
   * 216px and the overlay 200px of that. The list and a card do not both go in there: measured
   * live in landscape, the card was squeezed to 66px against a content height of 139px — the name
   * and the close button, and not one of the four rows under them. The first of those to go was
   * `Position`, the row that says a position was withheld, so a reader on a phone was told less
   * about protection than one at a desk. The overlay now says outright when a card is open, and
   * the stylesheet folds the list away on a screen with room for only one of them.
   *
   * Said as a class this component sets rather than as a `:has()` in the stylesheet, so that what
   * the layout branches on is a fact a test can read back.
   */
  it('says when a card is open, so a screen with room for one of them can show that one', () => {
    render(<Harness cavers={[caver()]} />);
    const overlay = screen.getByTestId('caveview-tracking');
    expect(overlay.className).not.toContain('caveview-tracking-carded');

    fireEvent.click(screen.getByTestId('caveview-caver-caver-1'));
    expect(overlay.className).toContain('caveview-tracking-carded');

    fireEvent.click(screen.getByTestId('caveview-caver-card-close'));
    expect(overlay.className).not.toContain('caveview-tracking-carded');
  });

  it('hands the last-update switch straight to whoever owns the markers', () => {
    const onChange = vi.fn();
    render(
      <CaveViewTrackingOverlay
        cavers={[caver()]}
        showTimes={false}
        onShowTimesChange={onChange}
        showLabels
        onShowLabelsChange={vi.fn()}
        openCaverId={null}
        onOpenCaver={vi.fn()}
        shown={null}
        onShow={vi.fn()}
      />,
    );

    fireEvent.click(screen.getByTestId('caveview-tracking-times'));

    expect(onChange).toHaveBeenCalledWith(true, expect.anything());
  });

  /**
   * The names on the model, and the switch that takes them off.
   *
   * It sits beside the last-update switch because the two are one question asked twice — whether
   * the markers say anything, and then what they say — and a reader looking for either looks in
   * the same place. Handed straight out for the same reason the other one is: the viewer is held
   * by the panel and this list has no way to reach it.
   */
  it('hands the names switch out, on by default, and greys the times out with it', () => {
    render(<Harness cavers={[caver()]} />);

    const labels = screen.getByTestId('caveview-tracking-labels');
    expect(labels).toBeChecked();

    fireEvent.click(labels);

    expect(labels).not.toBeChecked();
    // A time is drawn on a label, so with no labels the switch beside it governs nothing anybody
    // can see. Left pressable it would answer a press with no change at all, which reads as a
    // control that has stopped working rather than as one with nothing to work on.
    expect(screen.getByTestId('caveview-tracking-times')).toBeDisabled();

    fireEvent.click(labels);
    expect(screen.getByTestId('caveview-tracking-times')).toBeEnabled();
  });

  /**
   * A row is the way to the place, not only to the name of it.
   *
   * "P12" means something to somebody who knows the cave and nothing to anybody else, and the
   * whole reason this list is drawn over a model is that the model can answer the question the
   * name cannot. So the list hands a place back, and whoever owns the viewer flies to it.
   */
  describe('showing a place in the model', () => {
    it('hands back the station of the person pressed, and takes it back on a second press', () => {
      render(<Harness cavers={[caver()]} />);

      fireEvent.click(screen.getByTestId('caveview-caver-caver-1'));
      expect(asked).toEqual([{ kind: 'caver', id: 'caver-1', station: 'pestera.galerie.7' }]);
      expect(screen.getByTestId('caveview-caver-caver-1')).toHaveAttribute('aria-current', 'true');

      // The same row again: the card closes and so does the mark. Nothing else in the scene knows
      // this list put it there, so this press is the only way to take it off.
      fireEvent.click(screen.getByTestId('caveview-caver-caver-1'));
      expect(asked[1]).toBeNull();
      expect(screen.getByTestId('caveview-caver-caver-1')).not.toHaveAttribute('aria-current');
    });

    it('clears the mark when the person pressed has nowhere to stand', () => {
      // A mark left standing on the person pressed before would say the model is showing somebody
      // it is not — which on this surface reads as a position for a caver who has none.
      render(
        <Harness
          cavers={[
            caver({ caverId: 'a', name: 'Ana' }),
            caver({ caverId: 'b', name: 'Bogdan', position: { kind: 'withheld', certain: true } }),
          ]}
        />,
      );

      fireEvent.click(screen.getByTestId('caveview-caver-a'));
      fireEvent.click(screen.getByTestId('caveview-caver-b'));

      expect(asked).toEqual([{ kind: 'caver', id: 'a', station: 'pestera.galerie.7' }, null]);
      expect(screen.getByTestId('caveview-caver-a')).not.toHaveAttribute('aria-current');
    });

    it('heads each team and shows where the team was last reported', () => {
      render(
        <Harness
          cavers={[
            caver({
              caverId: 'a',
              teamId: 'team-1',
              teamTitle: 'One',
              position: { kind: 'station', station: 'p.g.3' },
              lastRecordedAt: '2026-09-12T09:00:00Z',
            }),
            caver({
              caverId: 'b',
              teamId: 'team-1',
              teamTitle: 'One',
              position: { kind: 'station', station: 'p.g.9' },
              lastRecordedAt: '2026-09-12T09:40:00Z',
            }),
            caver({ caverId: 'c', teamId: null, teamTitle: null }),
          ]}
        />,
      );

      // A team moves together and its position is whatever it last said, so the heading names the
      // station of the member who spoke most recently.
      fireEvent.click(screen.getByTestId('caveview-team-team-1'));
      expect(asked).toEqual([{ kind: 'team', id: 'team-1', station: 'p.g.9' }]);

      // Everybody on no team is gathered under a heading the page names, at the end.
      expect(screen.getByTestId('caveview-team-none')).toHaveTextContent('No team');
    });

    it('tells a team of one from its only member, which name the same station', () => {
      // Without the kind on the answer, the heading and the row would be the same place — so
      // pressing the heading again would fail to take the mark off and the row would light up.
      render(
        <Harness cavers={[caver({ caverId: 'a', teamId: 'team-1', teamTitle: 'One' })]} />,
      );

      fireEvent.click(screen.getByTestId('caveview-team-team-1'));
      expect(screen.getByTestId('caveview-team-team-1')).toHaveAttribute('aria-current', 'true');
      expect(screen.getByTestId('caveview-caver-a')).not.toHaveAttribute('aria-current');

      fireEvent.click(screen.getByTestId('caveview-team-team-1'));
      expect(asked[1]).toBeNull();
    });

    it('draws no heading at all for a party that was never divided into teams', () => {
      // A single heading reading "No team" over the whole list is a line of a phone's screen
      // spent restating that there is nothing to say.
      render(<Harness cavers={[caver({ caverId: 'a' }), caver({ caverId: 'b' })]} />);

      expect(screen.queryByTestId('caveview-team-none')).toBeNull();
      expect(screen.getByTestId('caveview-caver-a')).toBeInTheDocument();
    });

    it('leaves a team nobody has placed unpressable rather than answering with nothing', () => {
      render(
        <Harness
          cavers={[
            caver({
              caverId: 'a',
              teamId: 'team-1',
              teamTitle: 'One',
              position: { kind: 'unreported' },
              lastRecordedAt: null,
            }),
          ]}
        />,
      );

      const heading = screen.getByTestId('caveview-team-team-1');
      expect(heading).toBeDisabled();
      fireEvent.click(heading);
      expect(asked).toEqual([]);
    });
  });
});
