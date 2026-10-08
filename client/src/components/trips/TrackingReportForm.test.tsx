// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const recordEvents = vi.fn();
/**
 * What the depth in the box means, as the server answers it. Held as a whole query result because
 * that is what the card reads: the candidates, whether the answer is still coming, and the refusal
 * where there is one.
 */
const depthReading = vi.fn();

/** The places the cave has declared, shallowest first, as the server sends them. */
const declaredPlaces = vi.fn<() => { depthM: number; stationName: string; placeLabel: string | null }[]>();
/** Whether the card asked for those places at all, which it must not while the watch is off. */
const placesAsked = vi.fn<(enabled: boolean) => void>();

vi.mock('../../api/hooks.ts', () => ({
  TRACKING_EVENT_KINDS: ['entered', 'atStation', 'atDepth', 'note', 'exited'],
  useRecordTrackingEvents: () => ({ mutateAsync: recordEvents, isPending: false }),
  useTrackingDepthReading: (_tripLogId: string, depthM: number | null) => depthReading(depthM),
  useTrackingPlaces: (_tripLogId: string, enabled = true) => {
    placesAsked(enabled);
    return { data: declaredPlaces() };
  },
  // The survey's stations offered under the station field. Nothing found here: what the field
  // offers is the shared block's own business and is tested with it.
  useSurveyModelStationSearch: () => ({ data: undefined }),
}));

// Who the tab is signed in as, which decides whether a report nobody answered can be kept for
// them. Nobody by default, so every case that is not about keeping one leaves storage alone.
let account: string | null = null;
vi.mock('../../auth/accountId.ts', () => ({
  signedInAccountId: () => Promise.resolve(account),
}));

/** Asking the check again, the way the button on a failed reading does. */
const checkAgain = vi.fn();

// What decides how big every target on this card is drawn, and how big the two panels that open
// out of it are built. False by default: the machine this suite is read on has a mouse.
let coarse = false;
vi.mock('../../hooks/useCoarsePointer.ts', () => ({ useCoarsePointer: () => coarse }));

const { default: TrackingReportForm } = await import('./TrackingReportForm.tsx');

function show(state: 'off' | 'armed' | 'closed' = 'armed') {
  return render(
    <App>
      <TrackingReportForm
        tripLogId="trip-1"
        state={state}
        surveyModelId="model-1"
        caveId={null}
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

/**
 * Opens the declared-places chooser and returns its own options.
 *
 * <b>Scoped to this select's own popup, which is the whole reason it is a helper.</b> Every antd
 * dropdown that has been opened stays in the document, so a bare query for options collects the
 * kind chooser's five as well — and a click on "the first option" then lands on "Went in" and
 * silently changes the kind. The popup is found through the combobox's `aria-controls`, which is
 * the only link between a select and the element it portalled out of itself.
 */
async function openPlaces(known: string) {
  const chooser = screen.getByTestId('trip-tracking-place');
  fireEvent.mouseDown(chooser.querySelector('.ant-select-selector') ?? chooser);

  // Found through one option this caller knows is in the list, then widened to that option's own
  // dropdown. Every antd popup that has been opened stays in the document, so a bare query for
  // options collects the kind chooser's as well — and a click on "the first option" then lands on
  // "Went in" and silently changes what is being reported. The select's `aria-controls` is no help:
  // it names the hidden listbox, not the list the options are drawn in.
  const anchor = await waitFor(() => {
    const found = document.querySelector<HTMLElement>(
      `.ant-select-item-option[title="${known}"]`,
    );
    expect(found).not.toBeNull();
    return found!;
  });

  const popup = anchor.closest('.ant-select-dropdown')!;
  return Array.from(popup.querySelectorAll<HTMLElement>('.ant-select-item-option'));
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
  account = null;
  window.localStorage.clear();
  placesAsked.mockClear();
  recordEvents.mockReset().mockResolvedValue([{}]);
  // Nothing declared by default, which is the state of every cave until somebody declares
  // something — so the cases below that do not mention places are drawn as they are today.
  declaredPlaces.mockReset().mockReturnValue([]);
  checkAgain.mockReset();
  depthReading
    .mockReset()
    .mockReturnValue({ data: undefined, isFetching: false, error: null, refetch: checkAgain });
});

afterEach(cleanup);

/**
 * Reporting by the name of a place rather than by a number.
 *
 * <b>What this chooser is actually for.</b> A caver relaying word out of a cave says "at the
 * Meander". Nobody says "at 96 metres" and nobody at all says "at station 3.14" — so the chooser is
 * the fastest of the three ways to report, and the one a coordinator under pressure will reach for.
 * The cases below are the ones where it could quietly report something else: a chooser drawn for a
 * cave that declared nothing, an order that is not the order the server decided, or a choice that
 * sends a station while the log records a depth.
 */
describe('TrackingReportForm, reporting a declared place', () => {
  it('does not ask for the places while the watch is off, and asks once it is on', () => {
    // The hooks run whether or not the card is drawn, and the server refuses the question for a
    // watch with no model. A refusal fetched while the watch was off would be held and drawn as
    // no chooser the moment the watch comes on — so the question is not asked until then.
    const { rerender } = render(
      <App>
        <TrackingReportForm
          tripLogId="trip-1"
          state="off"
          surveyModelId="model-1"
          caveId={null}
          caverIds={['caver-1']}
          teams={[]}
          onRecorded={vi.fn()}
        />
      </App>,
    );
    expect(screen.getByTestId('trip-tracking-not-armed')).toBeInTheDocument();
    // Not asked at all, rather than asked and switched off: the fields that ask are not drawn
    // until there is a log to write on.
    expect(placesAsked).not.toHaveBeenCalled();

    rerender(
      <App>
        <TrackingReportForm
          tripLogId="trip-1"
          state="armed"
          surveyModelId="model-1"
          caveId={null}
          caverIds={['caver-1']}
          teams={[]}
          onRecorded={vi.fn()}
        />
      </App>,
    );
    expect(placesAsked).toHaveBeenLastCalledWith(true);
  });

  it('points at the watch\'s cave when that cave has declared nothing', async () => {
    // The card is told which cave the watch is in so that this line can lead somewhere. Told no
    // cave — a reader from whom it is withheld — the line stands with no link, which the default
    // rendering of every other case here is.
    render(
      <MemoryRouter>
        <App>
          <TrackingReportForm
            tripLogId="trip-1"
            state="armed"
            surveyModelId="model-1"
            caveId="cave-7"
            caverIds={['caver-1']}
            teams={[]}
            onRecorded={vi.fn()}
          />
        </App>
      </MemoryRouter>,
    );
    await toDepth('96');

    expect(screen.getByTestId('trip-tracking-place-none-cave')).toHaveAttribute(
      'href',
      '/caves/cave-7',
    );
    cleanup();

    show();
    await toDepth('96');
    expect(screen.getByTestId('trip-tracking-place-none')).toBeInTheDocument();
    expect(screen.queryByTestId('trip-tracking-place-none-cave')).toBeNull();
  });

  it('is not drawn at all for a cave that has declared nothing', () => {
    // An empty chooser is worse than no chooser: it says this cave has places and offers none.
    show();

    expect(screen.queryByTestId('trip-tracking-place')).not.toBeInTheDocument();
  });

  it('is drawn for the kinds that claim a place and for no others', async () => {
    declaredPlaces.mockReturnValue([{ depthM: 96, stationName: 'upper.2', placeLabel: 'Meandru' }]);
    show();

    // The card opens on "went in", which claims no station — so there is nothing for a place to
    // say, and offering one would invite a report that silently drops it.
    expect(screen.queryByTestId('trip-tracking-place')).not.toBeInTheDocument();

    await toDepth('96');
    expect(screen.getByTestId('trip-tracking-place')).toBeInTheDocument();
  });

  it('names the chooser by its label, so a screen reader says what it chooses', async () => {
    // The chooser's value is read off the depth field rather than held by the form, so its form
    // item has no field name — and a form item without one gives its label no `for`. Without the
    // tie, the fastest way to report was announced as a bare "combobox".
    declaredPlaces.mockReturnValue([{ depthM: 96, stationName: 'upper.2', placeLabel: 'Meandru' }]);
    show();
    await toDepth('96');

    const chooser = screen.getByRole('combobox', { name: 'Place' });
    expect(screen.getByTestId('trip-tracking-place')).toContainElement(chooser);
    expect(screen.getByText('Place', { selector: 'label' })).toHaveAttribute('for', chooser.id);
  });

  it('offers the places in the order the server decided, which is by depth', async () => {
    // Shallowest first, because a party goes down past the places in that order — and the order is
    // the server's so that every surface showing this list agrees about it. Re-sorting here, or
    // taking whatever order a cache happened to hold, is how two screens come to disagree.
    declaredPlaces.mockReturnValue([
      { depthM: 12, stationName: 'ent.1', placeLabel: 'Puțul de intrare' },
      { depthM: 96, stationName: 'upper.2', placeLabel: 'Meandru' },
      { depthM: 150, stationName: 'deep.3', placeLabel: null },
    ]);
    show();
    await toDepth('96');

    const offered = (await openPlaces('Meandru — 96 m')).map((option) => option.textContent);
    expect(offered).toEqual(['Puțul de intrare — 12 m', 'Meandru — 96 m', 'deep.3 — 150 m']);
  });

  it('records the declared depth, so the station that lands is the one the cave declared', async () => {
    // <b>The point of sending a depth rather than the station beside it.</b> The server resolves a
    // depth through the very declaration that was chosen here, so the place the person picked and
    // the station written on the log cannot disagree. Sending the station instead would be a second
    // answer to a question the server already answers, and the two could drift.
    declaredPlaces.mockReturnValue([{ depthM: 96, stationName: 'upper.2', placeLabel: 'Meandru' }]);
    show();
    await toDepth('0');

    fireEvent.click((await openPlaces('Meandru — 96 m'))[0]);

    await waitFor(() =>
      expect(screen.getByTestId('trip-tracking-depth')).toHaveValue('96'),
    );

    fireEvent.click(screen.getByTestId('trip-tracking-record'));
    await waitFor(() => expect(recordEvents).toHaveBeenCalledOnce());
    const sent = recordEvents.mock.calls[0][0];
    expect(sent.kind).toBe('atDepth');
    expect(sent.depthM).toBe(96);
    // And no station, because the station is the server's answer and not this card's guess at it.
    expect(sent.stationName ?? null).toBeNull();
  });

  /**
   * The chooser and the depth field are two ways of saying one thing, and they must never say two.
   *
   * <b>The chooser shows a place only while the depth in the box is that place's depth.</b> Left to
   * hold its own choice, it went on naming the place after the report it was chosen for had landed
   * and the depth had been emptied — the next report's card read "Meandru — 96 m" over an empty
   * field and refused to record a place its reader believed was chosen — and after the depth had
   * been retyped by hand, which is two answers on one card with nothing saying which is sent.
   */
  it('lets the place go once the report it was chosen for has landed', async () => {
    declaredPlaces.mockReturnValue([{ depthM: 96, stationName: 'upper.2', placeLabel: 'Meandru' }]);
    show();
    await toDepth('0');
    fireEvent.click((await openPlaces('Meandru — 96 m'))[0]);
    await waitFor(() =>
      expect(screen.getByTestId('trip-tracking-place')).toHaveTextContent('Meandru — 96 m'),
    );

    fireEvent.click(screen.getByTestId('trip-tracking-record'));
    await waitFor(() => expect(recordEvents).toHaveBeenCalledOnce());

    await waitFor(() => expect(screen.getByTestId('trip-tracking-depth')).toHaveValue(''));
    const chooser = screen.getByTestId('trip-tracking-place');
    expect(chooser).not.toHaveTextContent('Meandru — 96 m');
    expect(chooser).toHaveTextContent('Name the place instead of a depth');
  });

  it('lets the place go when a different depth is typed over it', async () => {
    declaredPlaces.mockReturnValue([{ depthM: 96, stationName: 'upper.2', placeLabel: 'Meandru' }]);
    show();
    await toDepth('0');
    fireEvent.click((await openPlaces('Meandru — 96 m'))[0]);
    await waitFor(() => expect(screen.getByTestId('trip-tracking-depth')).toHaveValue('96'));

    fireEvent.change(screen.getByTestId('trip-tracking-depth'), { target: { value: '120' } });

    await waitFor(() =>
      expect(screen.getByTestId('trip-tracking-place')).not.toHaveTextContent('Meandru — 96 m'),
    );
  });

  it('takes the depth it filled in back out when the place is cleared', async () => {
    declaredPlaces.mockReturnValue([{ depthM: 96, stationName: 'upper.2', placeLabel: 'Meandru' }]);
    show();
    await toDepth('0');
    fireEvent.click((await openPlaces('Meandru — 96 m'))[0]);
    await waitFor(() => expect(screen.getByTestId('trip-tracking-depth')).toHaveValue('96'));

    const chooser = screen.getByTestId('trip-tracking-place');
    fireEvent.mouseDown(chooser.querySelector('.ant-select-clear')!);

    await waitFor(() => expect(screen.getByTestId('trip-tracking-depth')).toHaveValue(''));
    expect(chooser).not.toHaveTextContent('Meandru — 96 m');
  });
});

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
 * time nobody said — a wrong record, until somebody notices and corrects it by hand. So the clock
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

  /**
   * A depth the cave has declared lands where the cave declared it, and the warning says so.
   *
   * <b>The distance is still worth saying; the reason given for it was wrong.</b> The server puts a
   * declared station first, flagged as declared and with its distance honestly measured, because
   * that is where recording the depth will put the party. A wide gap there is not a depth that
   * snapped to the nearest station — it is a declaration the survey disagrees with — and telling
   * the coordinator that "a depth is always recorded at the nearest station" names the right place
   * for a false reason and sends them to doubt the number rather than the declaration.
   */
  it('says a declared depth landed where the cave declared it, not at the nearest station', async () => {
    // 96 m is declared as the Meander's station, which the survey and this trip's datum put at
    // 136.2 m — 40.2 m out, past the quarter-of-the-depth tolerance at 96 m.
    const meander = {
      stationName: 'p.g.meandru',
      surveyName: 'p.g',
      depthM: 136.2,
      deltaM: 40.2,
      declared: true,
    };
    depthReading.mockReturnValue({ data: [meander], isFetching: false, error: null });
    show();
    await toDepth('96');

    const warning = await screen.findByTestId('trip-tracking-depth-gap');
    expect(warning).toHaveTextContent('This cave declares that depth as p.g.meandru');
    expect(warning).toHaveTextContent('40.2');
    expect(warning).toHaveTextContent('136.2');
    expect(warning).not.toHaveTextContent('nearest station');
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

/**
 * What the card asks about "when", in each of the three states a watch can be in.
 *
 * <b>The closed one is the reason this is asked of the state rather than of a yes or no.</b> A
 * finished trip is written up afterwards, and while the card took only "may this log be written"
 * it went on offering "now" there and stamping an empty moment with the server's clock: a call
 * forgotten on Saturday and typed on Monday landed on Monday, the replay ran two days past the
 * trip, and an "entered" typed that way read as somebody underground on a trip that was over.
 * Each case below is stated beside the one it differs from.
 */
describe('TrackingReportForm, the moment in each state of the watch', () => {
  /** Types a moment into "when it was said" and accepts it, the way the keyboard does. */
  function nameTheMoment(text: string) {
    const field = screen.getByTestId('trip-tracking-recorded-at');
    fireEvent.mouseDown(field);
    fireEvent.focus(field);
    fireEvent.change(field, { target: { value: text } });
    fireEvent.keyDown(field, { key: 'Enter', code: 'Enter' });
  }

  it('offers no card on a watch that was never started', () => {
    show('off');

    expect(screen.getByTestId('trip-tracking-not-armed')).toBeInTheDocument();
    expect(screen.queryByTestId('trip-tracking-record')).toBeNull();
    expect(screen.queryByTestId('trip-tracking-record-after')).toBeNull();
  });

  it('leaves the moment optional and the quick answers on while the watch is running', async () => {
    show('armed');

    expect(screen.queryByTestId('trip-tracking-record-after')).toBeNull();
    expect(screen.getByTestId('trip-tracking-when-now')).toBeInTheDocument();
    expect(screen.getByTestId('trip-tracking-when-15')).toBeInTheDocument();

    // An empty moment is the server's clock, which is right while it is happening — for a
    // report and for marking somebody out alike.
    await act(async () => {
      fireEvent.click(screen.getByTestId('trip-tracking-record'));
    });
    await act(async () => {
      fireEvent.click(screen.getByTestId('trip-tracking-mark-out'));
    });
    expect(recordEvents).toHaveBeenCalledTimes(2);
    expect(recordEvents.mock.calls[0][0]).toMatchObject({ kind: 'entered', recordedAt: null });
    expect(recordEvents.mock.calls[1][0]).toMatchObject({ kind: 'exited', recordedAt: null });
  });

  it('says a closed watch is being written up afterwards, and offers no "now" to answer with', () => {
    show('closed');

    expect(screen.getByTestId('trip-tracking-record-after')).toHaveTextContent(
      'being written up afterwards',
    );
    // The card is still there — a closed log is written — and what is gone is every answer that
    // measures back from this minute.
    expect(screen.getByTestId('trip-tracking-record')).toBeInTheDocument();
    expect(screen.getByTestId('trip-tracking-recorded-at')).toBeInTheDocument();
    expect(screen.queryByTestId('trip-tracking-when-now')).toBeNull();
    for (const minutes of [5, 15, 30, 60, 120]) {
      expect(screen.queryByTestId(`trip-tracking-when-${minutes}`)).toBeNull();
    }
  });

  it('sends nothing from a closed watch until the moment has been named', async () => {
    show('closed');

    await act(async () => {
      fireEvent.click(screen.getByTestId('trip-tracking-record'));
    });
    expect(recordEvents).not.toHaveBeenCalled();
    expect(await screen.findByText(/Say when this was said/)).toBeInTheDocument();

    // Marking out is the one control that never read the form, so it is held to the same rule
    // by name rather than assumed to be.
    await act(async () => {
      fireEvent.click(screen.getByTestId('trip-tracking-mark-out'));
    });
    expect(recordEvents).not.toHaveBeenCalled();
  });

  it('marks somebody out of a closed watch at the moment that was named', async () => {
    show('closed');
    nameTheMoment('2026-09-12 16:30:00');

    await act(async () => {
      fireEvent.click(screen.getByTestId('trip-tracking-mark-out'));
    });

    await waitFor(() => expect(recordEvents).toHaveBeenCalledTimes(1));
    const body = recordEvents.mock.calls[0][0] as { kind: string; recordedAt: string | null };
    expect(body.kind).toBe('exited');
    // The instant typed, in the reader's own zone — whatever zone this suite runs in.
    expect(body.recordedAt).toBe(new Date(2026, 8, 12, 16, 30, 0).toISOString());
  });

  it('records a report on a closed watch at the moment that was named', async () => {
    show('closed');
    nameTheMoment('2026-09-12 11:05:00');

    await act(async () => {
      fireEvent.click(screen.getByTestId('trip-tracking-record'));
    });

    await waitFor(() => expect(recordEvents).toHaveBeenCalledTimes(1));
    expect((recordEvents.mock.calls[0][0] as { recordedAt: string }).recordedAt).toBe(
      new Date(2026, 8, 12, 11, 5, 0).toISOString(),
    );
  });
});

/**
 * What the card does with itself once a report has been sent and nobody answered.
 *
 * Three outcomes that look alike from the button and must not be treated alike: kept in the
 * browser to be sent later (the card is done with it and clears, as for a report that landed),
 * refused by the server (the text stays, to be changed and sent again), and neither answered nor
 * kept (the text stays, because the card is the only copy).
 */
describe('TrackingReportForm, a report nobody answered', () => {
  function showWith(onRecorded: () => void) {
    return render(
      <App>
        <TrackingReportForm
          tripLogId="trip-1"
          state="armed"
          surveyModelId="model-1"
          caveId={null}
          caverIds={['caver-1']}
          teams={[]}
          onRecorded={onRecorded}
        />
      </App>,
    );
  }

  async function recordANote(text: string) {
    fireEvent.change(screen.getByTestId('trip-tracking-note'), { target: { value: text } });
    await act(async () => {
      fireEvent.click(screen.getByTestId('trip-tracking-record'));
    });
    await waitFor(() => expect(recordEvents).toHaveBeenCalledOnce());
  }

  it('clears and lets the selection go when the report is kept to be sent later', async () => {
    account = 'account-ana';
    recordEvents.mockRejectedValue(new TypeError('Failed to fetch'));
    const onRecorded = vi.fn();
    showWith(onRecorded);

    await recordANote('all four at the sump');

    await waitFor(() => expect(onRecorded).toHaveBeenCalled());
    expect(screen.getByTestId('trip-tracking-note')).toHaveValue('');
    expect(await screen.findByText(/kept in this browser/)).toBeInTheDocument();
    expect(Object.keys(window.localStorage)).toHaveLength(1);
  });

  it('keeps what was typed when the server refused it', async () => {
    account = 'account-ana';
    const { ApiError } = await import('../../api/client.ts');
    recordEvents.mockRejectedValue(new ApiError(409, 'tracking.model_missing'));
    const onRecorded = vi.fn();
    showWith(onRecorded);

    await recordANote('all four at the sump');

    await waitFor(() => expect(Object.keys(window.localStorage)).toEqual([]));
    expect(screen.getByTestId('trip-tracking-note')).toHaveValue('all four at the sump');
    expect(onRecorded).not.toHaveBeenCalled();
  });

  it('keeps what was typed when nobody answered and the browser could not keep it', async () => {
    recordEvents.mockRejectedValue(new TypeError('Failed to fetch'));
    const onRecorded = vi.fn();
    showWith(onRecorded);

    await recordANote('all four at the sump');

    expect(await screen.findByText(/^Not sent:/)).toBeInTheDocument();
    expect(screen.getByTestId('trip-tracking-note')).toHaveValue('all four at the sump');
    expect(onRecorded).not.toHaveBeenCalled();
  });
});
