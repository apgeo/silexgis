// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const recordEvents = vi.fn();
const resolveDepth = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  TRACKING_EVENT_KINDS: ['entered', 'atStation', 'atDepth', 'note', 'exited'],
  useRecordTrackingEvents: () => ({ mutateAsync: recordEvents, isPending: false }),
  useResolveTrackingDepth: () => ({ mutateAsync: resolveDepth, isPending: false }),
}));

// What decides how big every target on this card is drawn, and how big the two panels that open
// out of it are built. False by default: the machine this suite is read on has a mouse.
let coarse = false;
vi.mock('../../hooks/useCoarsePointer.ts', () => ({ useCoarsePointer: () => coarse }));

const { default: TrackingReportForm } = await import('./TrackingReportForm.tsx');

function show() {
  return render(
    <App>
      <TrackingReportForm
        tripLogId="trip-1"
        armed
        caverIds={['caver-1']}
        teams={[]}
        onRecorded={vi.fn()}
      />
    </App>,
  );
}

/** Opens the calendar behind "when it was said", the way pressing the field does. */
function openWhen() {
  const field = screen.getByTestId('trip-tracking-recorded-at');
  fireEvent.mouseDown(field);
  fireEvent.click(field);
  return document.querySelector('.tracking-report-when-popup') as HTMLElement | null;
}

/**
 * The sizes antd actually built the open panel out of.
 *
 * <b>Read back from the stylesheet because there is nowhere else to read them.</b> The panel is
 * drawn in a portal, sized from component tokens rather than from the `size` given to the field
 * that opens it, and jsdom lays nothing out — so a test that measured it would measure zero. antd
 * publishes those tokens as custom properties scoped to a class the panel carries, which is both
 * the geometry itself and the only observable of it.
 *
 * Narrowed to the scope the *open* panel carries, because the stylesheet is cumulative: a render
 * earlier in this file leaves its own block in the document, and an unscoped search would happily
 * find a forty-pixel cell that belongs to a panel nobody is looking at.
 */
function panelSizes(): string {
  const popup = document.querySelector('.tracking-report-when-popup');
  if (popup === null) {
    throw new Error('the calendar is not open');
  }
  const scope = Array.from(popup.classList).find((name) => name.startsWith('css-var-'));
  return Array.from(document.querySelectorAll('style'))
    .map((style) => style.textContent ?? '')
    .filter(
      (text) =>
        scope !== undefined &&
        text.includes(`.${scope}`) &&
        text.includes('--ant-date-picker-cell-height'),
    )
    .join('\n');
}

beforeEach(() => {
  coarse = false;
  recordEvents.mockReset().mockResolvedValue([{}]);
  resolveDepth.mockReset().mockResolvedValue([]);
});

afterEach(cleanup);

/**
 * The calendar behind "when it was said" — the one panel on this card that is not drawn inside it,
 * and the one that has to be repaired from two directions at once.
 *
 * A relayed report is by definition in the past, so naming a past moment is the only thing this
 * control is here for. Sized for a cursor it opened onto 24px days; sized for a finger it grew to
 * 457px across on a 412px screen and resolved the overflow by hanging off the *left* edge, which
 * is where Sunday, Monday and both of the arrows that walk backwards live. Neither half is visible
 * from inside the component: the panel is portalled out of it, the geometry is in a stylesheet that
 * can only reach it by name, and the sizes come from tokens. So both are asserted here, because the
 * repair is otherwise deletable without a single test going red.
 */
describe('TrackingReportForm, the relayed-time calendar', () => {
  it('names the panel, so the stylesheet that shapes it has something to reach', () => {
    // The class is the only hook there is: the panel is rendered in a portal at the end of the
    // document, out of reach of any selector this card could otherwise write. Without it the whole
    // stylesheet beside this component matches nothing at all.
    show();

    expect(openWhen()).not.toBeNull();
  });

  it('builds the panel a finger lands in at forty pixels', () => {
    coarse = true;
    show();
    openWhen();

    const sizes = panelSizes();
    // The days, which were 24px square.
    expect(sizes).toContain('--ant-date-picker-cell-height:40px');
    expect(sizes).toContain('--ant-date-picker-cell-width:40px');
    // The hours beside them, which are the other list a finger has to land in. Their *width* is
    // deliberately left alone: three columns standing side by side is what pushes the panel off a
    // phone, and it is their height a finger misses.
    expect(sizes).toContain('--ant-date-picker-time-cell-height:40px');
    expect(sizes).not.toContain('--ant-date-picker-time-column-width:40px');
  });

  it('leaves the panel at antd’s own size under a mouse', () => {
    // The other half of the same rule, and the one it would be easy to lose: a desk loses nothing
    // to a calendar built for a cursor, and growing every panel for everybody would be a regression
    // dressed as a fix.
    show();
    openWhen();

    expect(panelSizes()).not.toContain('--ant-date-picker-cell-height:40px');
  });

  it('grows the list of report kinds for a finger as well', () => {
    // The second panel that opens out of this card, portalled and token-sized for the same reasons.
    coarse = true;
    show();

    const styles = Array.from(document.querySelectorAll('style'))
      .map((style) => style.textContent ?? '')
      .join('\n');
    expect(styles).toContain('--ant-select-option-height:40px');
  });

  it('asks for the whole card by size rather than pushing heights into it', () => {
    // Every control somebody presses on this card is on one branch, and it is the pointer's.
    coarse = true;
    show();

    expect(screen.getByTestId('trip-tracking-record')).toHaveClass('ant-btn-lg');
    expect(screen.getByTestId('trip-tracking-kind')).toHaveClass('ant-select-lg');
    expect(screen.getByTestId('trip-tracking-recorded-at').closest('.ant-picker')).toHaveClass(
      'ant-picker-large',
    );
  });
});
