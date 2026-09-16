// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const recordEvents = vi.fn();
/**
 * What the depth in the box means, as the server answers it. Held as a whole query result because
 * that is what the card reads: the candidates, whether the answer is still coming, and the refusal
 * where there is one.
 */
const depthReading = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  TRACKING_EVENT_KINDS: ['entered', 'atStation', 'atDepth', 'note', 'exited'],
  useRecordTrackingEvents: () => ({ mutateAsync: recordEvents, isPending: false }),
  useTrackingDepthReading: (_tripLogId: string, depthM: number | null) => depthReading(depthM),
}));

/** Asking the check again, the way the button on a failed reading does. */
const checkAgain = vi.fn();

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

/** Puts the card on a depth report with a number typed into it, the way a coordinator does. */
async function toDepth(value: string) {
  const kind = screen.getByTestId('trip-tracking-kind');
  fireEvent.mouseDown(kind.querySelector('.ant-select-selector') ?? kind);
  fireEvent.click(document.querySelector('.ant-select-item-option[title="At a depth"]')!);
  fireEvent.change(await screen.findByTestId('trip-tracking-depth'), { target: { value } });
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
  checkAgain.mockReset();
  depthReading
    .mockReset()
    .mockReturnValue({ data: undefined, isFetching: false, error: null, refetch: checkAgain });
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

  it('leaves its own panel free to follow the field, which it has room to do', () => {
    // The dialog a station press opens marks its copy of this panel to be pinned to the screen,
    // because its fields scroll inside a modal body with 197px of range in all and the field can
    // never rise far enough for a finger-sized calendar to open under it. This card is the surface
    // that *can*: it scrolls with the page, and the same panel opened here was measured at y=83
    // ending at 815 of an 839px screen, entirely on. Pinning it here would move a panel that has
    // room for itself, so the mark is the dialog's and not this card's.
    coarse = true;
    show();

    expect(openWhen()).not.toHaveClass('tracking-report-when-popup-pinned');
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

/**
 * The quick answers to "when was it said".
 *
 * <b>What is asserted is the instant, not the button.</b> A row of buttons that set a field to
 * roughly the right moment would pass any test about labels and still put a report on a log at a
 * time nobody said — and the log is never edited, so a wrong moment is a wrong record. So the clock
 * is held still and the value that reaches the request is read back: the report body carries the
 * instant as a string, which is the form the server actually stores.
 */
describe('TrackingReportForm, naming when a report was said', () => {
  /** A fixed moment to count back from, so every offset below is an arithmetic claim. */
  const NOW = new Date('2026-09-12T14:00:00.000Z');

  beforeEach(() => {
    // Held exactly still. An advancing fake clock makes every assertion below a claim about the
    // millisecond a test happened to reach, which is the one thing these offsets must not be
    // measured in: what is being pinned is that "15 minutes ago" is fifteen minutes and not
    // roughly fifteen.
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  /**
   * Presses a quick answer and sends the report, answering with the body that went.
   *
   * Driven through `act` rather than `waitFor` because the clock is frozen: everything between the
   * press and the request is a chain of promises, which settle on microtasks, while `waitFor` waits
   * on timers that a frozen clock never fires.
   */
  async function record(press?: string) {
    show();
    if (press !== undefined) {
      fireEvent.click(screen.getByTestId(press));
    }
    await act(async () => {
      fireEvent.click(screen.getByTestId('trip-tracking-record'));
    });
    expect(recordEvents).toHaveBeenCalled();
    return recordEvents.mock.calls[0][0] as { recordedAt: string | null };
  }

  it('sets each offset to the instant it claims', async () => {
    // Every one of them is in the past, and that is not a detail: the server refuses a moment more
    // than a couple of minutes ahead of its own clock, so a quick answer must never be the thing
    // that gets a report turned away.
    const cases: [string, number][] = [
      ['trip-tracking-when-5', 5],
      ['trip-tracking-when-15', 15],
      ['trip-tracking-when-30', 30],
      ['trip-tracking-when-60', 60],
      ['trip-tracking-when-120', 120],
    ];
    for (const [testId, minutes] of cases) {
      recordEvents.mockClear();
      const body = await record(testId);
      expect(new Date(body.recordedAt!).getTime(), testId).toBe(
        NOW.getTime() - minutes * 60_000,
      );
      cleanup();
    }
  });

  it('fills "now" in rather than emptying the field', async () => {
    // Leaving it empty is the truer "now" — the server stamps the report with its own clock — but
    // a button that answers by making a field go blank reads as a button that did nothing, and
    // this one is pressed in a hurry. The instant it writes is inside the skew the server allows.
    const body = await record('trip-tracking-when-now');

    expect(body.recordedAt).not.toBeNull();
    expect(new Date(body.recordedAt!).getTime()).toBe(NOW.getTime());
  });

  it('leaves an untouched field meaning the server’s own clock', async () => {
    expect((await record()).recordedAt).toBeNull();
  });
});

/**
 * The stations a depth could mean, and taking one of them as the answer.
 *
 * The whole reason to ask which station a depth means is that you may want to report that station
 * rather than the depth — so a preview that could only be read was a question with no way to act
 * on its answer.
 */
describe('TrackingReportForm, the depth preview', () => {
  const candidate = {
    stationName: 'p.g.12',
    surveyName: 'p.g',
    depthM: 84,
    deltaM: 1.5,
  };

  /** Takes the form to a depth report with the preview asked for and answered. */
  async function preview() {
    depthReading.mockReturnValue({ data: [candidate], isFetching: false, error: null });
    show();
    await toDepth('85');
    fireEvent.click(screen.getByTestId('trip-tracking-depth-preview'));
    return screen.findByTestId('trip-tracking-depth-candidates');
  }

  it('offers each station as the one the report will name', async () => {
    await preview();

    fireEvent.click(screen.getByTestId('trip-tracking-depth-choose-p.g.12'));

    // The report changes kind as well as value, and it has to: what is being said once a station
    // has been chosen is "they are at this station", which is a different claim about the world
    // from "they are this far down" — and recording the depth would leave the server to resolve it
    // a second time, against a filter that could have moved in between.
    expect(await screen.findByTestId('trip-tracking-station')).toHaveValue('p.g.12');
    expect(screen.queryByTestId('trip-tracking-depth-candidates')).toBeNull();

    fireEvent.click(screen.getByTestId('trip-tracking-record'));
    await waitFor(() => expect(recordEvents).toHaveBeenCalled());
    expect(recordEvents).toHaveBeenCalledWith(
      expect.objectContaining({ kind: 'atStation', stationName: 'p.g.12', depthM: null }),
    );
  });

  it('sizes the control that takes a candidate for the finger that presses it', async () => {
    coarse = true;
    await preview();

    expect(screen.getByTestId('trip-tracking-depth-choose-p.g.12')).toHaveClass('ant-btn-lg');
  });
});

/**
 * A depth that nothing in the cave is anywhere near, said before it is recorded.
 *
 * <b>This is the defect the preview button could not reach.</b> Resolution takes whichever station
 * of the trip's filter is nearest and has no tolerance underneath it at all, so a coordinator
 * taking relayed word who types 1200 for 120 against a 140 m cave is not refused and is not asked
 * anything — the party is written down at the bottom of the system. The preview that would have
 * shown it is a button on the other side of the form, and somebody typing a number off a phone call
 * presses Record.
 *
 * The scenario below is the same mistake one digit smaller — 700 for 70 — so that the wording being
 * asserted does not depend on how a thousands separator is drawn.
 */
describe('TrackingReportForm, a depth nothing in the cave is near', () => {
  /** A 140 m cave: the deepest station there is, and how far 700 m is from it. */
  const bottom = { stationName: 'p.g.140', surveyName: 'p.g', depthM: 139.4, deltaM: 560.6 };
  /** The ordinary case: the survey has no station at exactly 120 m, and one 40 cm away. */
  const nearby = { stationName: 'p.g.119', surveyName: 'p.g', depthM: 119.6, deltaM: 0.4 };

  it('warns while the number is still in the box, with nothing pressed to ask it', async () => {
    depthReading.mockReturnValue({ data: [bottom], isFetching: false, error: null });
    show();
    await toDepth('700');

    const warning = await screen.findByTestId('trip-tracking-depth-gap');
    // Both halves of what is wrong: where it would land, and how far that is from what was said.
    expect(warning).toHaveTextContent('p.g.140');
    expect(warning).toHaveTextContent('560.6');
    expect(warning).toHaveTextContent('139.4');
    // Nothing was pressed to get it — the list of candidates is still unasked for, which is the
    // whole point: the answer used to be on the other side of a button.
    expect(screen.queryByTestId('trip-tracking-depth-candidates')).toBeNull();
  });

  it('warns rather than refuses, because the number may be right and the survey thin', async () => {
    depthReading.mockReturnValue({ data: [bottom], isFetching: false, error: null });
    show();
    await toDepth('700');
    await screen.findByTestId('trip-tracking-depth-gap');

    fireEvent.click(screen.getByTestId('trip-tracking-record'));

    await waitFor(() => expect(recordEvents).toHaveBeenCalled());
    expect(recordEvents).toHaveBeenCalledWith(
      expect.objectContaining({ kind: 'atDepth', depthM: 700 }),
    );
  });

  /**
   * The twin of the two above, and the reason the threshold is a judgement rather than a zero.
   * A survey has no station at exactly the depth anybody reports, so a card that remarked on every
   * one of them would teach a coordinator to read past the one that mattered.
   */
  it('says nothing when the nearest station is all but where the report said', async () => {
    depthReading.mockReturnValue({ data: [nearby], isFetching: false, error: null });
    show();
    await toDepth('120');

    // Waited on rather than asserted at once: the question has to have been asked and answered
    // before "no warning" means anything, or "not yet" passes for "never".
    await waitFor(() => expect(depthReading).toHaveBeenCalledWith(120));
    expect(screen.queryByTestId('trip-tracking-depth-gap')).toBeNull();
  });
});

/**
 * A check that did not happen must not look like a check that passed.
 *
 * <b>Everything this card now does about a wild depth rests on a warning being absent meaning "it
 * was checked, and it is fine".</b> One failed request — a 500, a connection dropped mid-callout, a
 * session that lapsed behind the form — makes the card pixel-identical to a depth that resolved
 * half a metre from a station: no warning either way. Record then succeeds, because the write
 * resolves on the server's own path and is never blocked, and the party is stored at the bottom of
 * the cave with nothing on screen having disagreed. That is the original defect restored by one
 * dropped request, so the refusal is drawn whenever a depth has been typed, not once somebody has
 * pressed the button that asks for the list.
 */
describe('TrackingReportForm, a depth the survey could not be asked about', () => {
  /** What a station half a metre away looks like, for the twin of every silence below. */
  const nearby = { stationName: 'p.g.119', surveyName: 'p.g', depthM: 119.6, deltaM: 0.4 };
  /** A request that failed rather than a question that was answered. */
  const failed = { data: undefined, isFetching: false, error: new Error('gone'), refetch: checkAgain };

  it('says the check failed without anything having been pressed to ask for it', async () => {
    depthReading.mockReturnValue(failed);
    show();
    await toDepth('120');

    const notice = await screen.findByTestId('trip-tracking-depth-check-failed');
    expect(notice).toHaveTextContent('has not been checked against the survey');
    // And what it means for the act in front of them: recording is not blocked and the number
    // still lands at the nearest station, so the check is theirs to make.
    expect(notice).toHaveTextContent('Recording is not blocked');
    // The list of candidates is still unasked for. That button is exactly what this used to be
    // hidden behind, and hiding it there is why one failed request was silent.
    expect(screen.queryByTestId('trip-tracking-depth-candidates')).toBeNull();
  });

  /**
   * The twin. Without it the assertion above would pass on a card that cried failure at a depth
   * that resolved perfectly well.
   */
  it('says nothing of the sort when the check came back', async () => {
    depthReading.mockReturnValue({ data: [nearby], isFetching: false, error: null, refetch: checkAgain });
    show();
    await toDepth('120');

    await waitFor(() => expect(depthReading).toHaveBeenCalledWith(120));
    expect(screen.queryByTestId('trip-tracking-depth-check-failed')).toBeNull();
  });

  /**
   * A rejection is held under the same key as the question, so retyping the same number re-reads it
   * and stays quiet. Asking again has to be something a person can do.
   */
  it('offers the check again rather than leaving a refusal standing for ever', async () => {
    depthReading.mockReturnValue(failed);
    show();
    await toDepth('120');
    await screen.findByTestId('trip-tracking-depth-check-failed');

    fireEvent.click(screen.getByTestId('trip-tracking-depth-check-again'));

    expect(checkAgain).toHaveBeenCalled();
  });

  /** Warned, never refused: the number may be right and the check merely unavailable. */
  it('still records the depth, because a failed check is not a reason to refuse a report', async () => {
    depthReading.mockReturnValue(failed);
    show();
    await toDepth('120');
    await screen.findByTestId('trip-tracking-depth-check-failed');

    fireEvent.click(screen.getByTestId('trip-tracking-record'));

    await waitFor(() => expect(recordEvents).toHaveBeenCalled());
    expect(recordEvents).toHaveBeenCalledWith(
      expect.objectContaining({ kind: 'atDepth', depthM: 120 }),
    );
  });

  /**
   * And the third state silence could mean. A number typed and Record pressed within the second is
   * the hurry a callout is conducted in, and "not asked yet" reads exactly like "asked, and fine".
   */
  it('says the check is still running while it is', async () => {
    depthReading.mockReturnValue({ data: undefined, isFetching: true, error: null, refetch: checkAgain });
    show();
    await toDepth('120');

    expect(await screen.findByTestId('trip-tracking-depth-checking')).toBeTruthy();
  });

  it('stops saying it once the answer is in', async () => {
    depthReading.mockReturnValue({ data: [nearby], isFetching: false, error: null, refetch: checkAgain });
    show();
    await toDepth('120');

    await waitFor(() => expect(depthReading).toHaveBeenCalledWith(120));
    expect(screen.queryByTestId('trip-tracking-depth-checking')).toBeNull();
  });
});
