// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import '../../i18n';

const update = vi.fn();
/** The places the watch's cave has declared, as the server sends them — or its refusal to say. */
const declaredPlaces = vi.fn();
/** What a depth means on the watch's survey, and which depth was asked about. */
const depthReading = vi.fn();
/** The survey's stations beginning with what has been typed, and which survey was asked. */
const stationSearch = vi.fn();

// The kinds and the station rules are the real ones — what is stubbed is only the write, so a
// change to what a valid report looks like is felt here rather than mocked away.
vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useUpdateTrackingEvent: () => ({ mutateAsync: update, isPending: false }),
    useTrackingPlaces: () => ({ data: declaredPlaces() }),
    useTrackingDepthReading: (_tripLogId: string, depthM: number | null) => depthReading(depthM),
    useSurveyModelStationSearch: (surveyModelId: string | undefined, q: string) =>
      stationSearch(surveyModelId, q),
  };
});

// The pointer, which is what every control's size follows. False by default — the desk this suite
// is read on; the cases about a finger set it and say so.
let coarse = false;
vi.mock('../../hooks/useCoarsePointer.ts', () => ({ useCoarsePointer: () => coarse }));

const { default: TrackingEventEditDialog } = await import('./TrackingEventEditDialog.tsx');
type TrackingEvent = import('../../api/hooks.ts').TrackingEvent;

const REPORT: TrackingEvent = {
  id: 'ev-1',
  caverId: 'caver-1',
  teamId: null,
  kind: 'atStation',
  surveyModelId: 'model-1',
  stationName: 'cave.upper.2',
  toStationName: null,
  depthEnteredM: null,
  note: 'first call',
  recordedAt: '2026-09-12T10:00:00Z',
  corrected: false,
  outsideDeclaredParts: false,
};

/** The watch the report is on: its survey, and when it was started — two hours before the report. */
const WATCH = { surveyModelId: 'model-1', armedAt: '2026-09-12T08:00:00Z' } as const;

beforeEach(() => {
  coarse = false;
  update.mockReset().mockResolvedValue({ ...REPORT });
  declaredPlaces.mockReset().mockReturnValue([]);
  depthReading
    .mockReset()
    .mockReturnValue({ data: undefined, isFetching: false, error: null, refetch: vi.fn() });
  stationSearch.mockReset().mockReturnValue({ data: undefined });
});
afterEach(cleanup);

/**
 * antd names a deprecated prop through `console.error`, once per render — and in development the
 * application's own error sweep files every one of those as a defect. So every case in this file
 * also checks that nothing it drew spoke that way; a notice written with the old spelling of a
 * prop fails here rather than in somebody's diagnostics.
 */
let consoleError: MockInstance<typeof console.error>;
beforeEach(() => {
  consoleError = vi.spyOn(console, 'error');
});
afterEach(() => {
  const deprecations = consoleError.mock.calls
    .map(([first]) => String(first))
    .filter((line) => /\[antd: [^\]]+\].*deprecated/.test(line));
  consoleError.mockRestore();
  expect(deprecations).toEqual([]);
});

/**
 * Correcting a report already on the log.
 *
 * The cases here are the ones where a correction could quietly send something other than what the
 * reader typed — a field left over from the kind the report used to be, or the previous row's
 * values still in a form that is mounted once and reused.
 */
describe('TrackingEventEditDialog', () => {
  it('opens on the report it was given, with its own values in the fields', () => {
    render(
      <App>
        <TrackingEventEditDialog {...WATCH} tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
      </App>,
    );
    expect(screen.getByTestId('trip-tracking-edit-station')).toHaveValue('cave.upper.2');
    expect(screen.getByTestId('trip-tracking-edit-note')).toHaveValue('first call');
  });

  it('says that a correction changes what the log records, rather than doing it silently', () => {
    render(
      <App>
        <TrackingEventEditDialog {...WATCH} tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
      </App>,
    );
    // The replay and the published page both follow the log, so a reader correcting one is
    // changing what a public surface says happened. That is the point of the feature and is worth
    // stating; a dialog that changed it silently would be the same feature with a worse conscience.
    expect(screen.getByText(/changes what the log says happened/i)).toBeInTheDocument();
  });

  it('keeps the report it corrects, and never offers to change whose report it is', () => {
    render(
      <App>
        <TrackingEventEditDialog {...WATCH} tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
      </App>,
    );
    // A report about a different person is a different report — the delete beside this control is
    // what that means. A caver chooser here would offer to rewrite a record's subject in place.
    expect(screen.queryByTestId('trip-tracking-edit-caver')).not.toBeInTheDocument();
  });

  it('sends the place belonging to the kind, and clears the one that does not', async () => {
    render(
      <App>
        <TrackingEventEditDialog {...WATCH} tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
      </App>,
    );

    // A station report corrected into an exit must not still carry the station: the server measures
    // the whole report again and refuses a place on a kind that has none, so a leftover field is a
    // correction that cannot be saved and says nothing about why.
    const kindField = screen.getByTestId('trip-tracking-edit-kind');
    fireEvent.mouseDown(kindField.querySelector('.ant-select-selector') ?? kindField);
    await act(async () => {
      fireEvent.click(document.querySelector('.ant-select-item-option[title="Came out"]')!);
    });
    fireEvent.click(screen.getByRole('button', { name: /save the correction/i }));

    await waitFor(() => expect(update).toHaveBeenCalled());
    const sent = update.mock.calls[0][0];
    expect(sent.eventId).toBe('ev-1');
    expect(sent.kind).toBe('exited');
    expect(sent.stationName).toBeNull();
    expect(sent.depthM).toBeNull();
  });

  /**
   * A report between two stations is corrected as one. It opens holding both ends, so that
   * correcting something else about it sends the stretch back unchanged; emptying the second
   * station turns it into a report at the first, said to the server as an absence; and a stretch
   * corrected into a kind that names no station carries neither end.
   */
  it('opens a stretch on both its stations and sends back what the fields then hold', async () => {
    const stretch = { ...REPORT, toStationName: 'cave.upper.5' };
    const save = () => fireEvent.click(screen.getByRole('button', { name: /save the correction/i }));
    render(
      <App>
        <TrackingEventEditDialog {...WATCH} tripLogId="trip-1" report={stretch} teams={[]} onClose={() => {}} />
      </App>,
    );

    expect(screen.getByTestId('trip-tracking-edit-station')).toHaveValue('cave.upper.2');
    expect(screen.getByTestId('trip-tracking-edit-to-station')).toHaveValue('cave.upper.5');

    save();
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1));
    expect(update.mock.calls[0][0]).toMatchObject({
      stationName: 'cave.upper.2',
      toStationName: 'cave.upper.5',
    });

    fireEvent.change(screen.getByTestId('trip-tracking-edit-to-station'), { target: { value: '  ' } });
    save();
    await waitFor(() => expect(update).toHaveBeenCalledTimes(2));
    expect(update.mock.calls[1][0]).toMatchObject({ stationName: 'cave.upper.2', toStationName: null });
  });

  it('carries neither end of a stretch on a correction into a kind that names no station', async () => {
    render(
      <App>
        <TrackingEventEditDialog
          {...WATCH}
          tripLogId="trip-1"
          report={{ ...REPORT, toStationName: 'cave.upper.5' }}
          teams={[]}
          onClose={() => {}}
        />
      </App>,
    );

    const kindField = screen.getByTestId('trip-tracking-edit-kind');
    fireEvent.mouseDown(kindField.querySelector('.ant-select-selector') ?? kindField);
    await act(async () => {
      fireEvent.click(document.querySelector('.ant-select-item-option[title="Came out"]')!);
    });
    fireEvent.click(screen.getByRole('button', { name: /save the correction/i }));

    await waitFor(() => expect(update).toHaveBeenCalled());
    expect(update.mock.calls[0][0]).toMatchObject({ kind: 'exited', stationName: null, toStationName: null });
  });

  it('saves nothing and rejects nothing when a required field is left empty', async () => {
    // Clearing the station and pressing Save is an ordinary act, and what it must produce is the
    // field's own complaint and nothing else. The validation's refusal used to escape the click
    // handler as an unhandled rejection carrying the form's values — which the browser is watched
    // for, and reported as a defect.
    const escaped = vi.fn();
    process.on('unhandledRejection', escaped);
    try {
      render(
        <App>
          <TrackingEventEditDialog {...WATCH} tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
        </App>,
      );
      fireEvent.change(screen.getByTestId('trip-tracking-edit-station'), { target: { value: '' } });
      fireEvent.click(screen.getByRole('button', { name: /save the correction/i }));

      expect(await screen.findByText(/say which station/i)).toBeInTheDocument();
      // A rejection is reported on a later turn of the event loop than the click that caused it,
      // so one is waited out before the absence of one is taken to mean anything.
      await new Promise((resolve) => setTimeout(resolve, 0));
      expect(update).not.toHaveBeenCalled();
      expect(escaped).not.toHaveBeenCalled();
    } finally {
      process.off('unhandledRejection', escaped);
    }
  });

  it('fills itself from whichever report is opened, not from the one before it', async () => {
    const { rerender } = render(
      <App>
        <TrackingEventEditDialog {...WATCH} tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
      </App>,
    );
    expect(screen.getByTestId('trip-tracking-edit-station')).toHaveValue('cave.upper.2');

    // One dialog serves every row, so this is the fault that would otherwise offer one report's
    // place as a correction to another's — and it would look like working software.
    rerender(
      <App>
        <TrackingEventEditDialog
          {...WATCH}
          tripLogId="trip-1"
          report={{ ...REPORT, id: 'ev-2', stationName: 'cave.deep.3', note: 'second call' }}
          teams={[]}
          onClose={() => {}}
        />
      </App>,
    );
    await waitFor(() =>
      expect(screen.getByTestId('trip-tracking-edit-station')).toHaveValue('cave.deep.3'),
    );
    expect(screen.getByTestId('trip-tracking-edit-note')).toHaveValue('second call');
  });

  /**
   * Built for a finger where there is one.
   *
   * This is the dialog opened from a phone to fix an hour typed off a call, and it is one form with
   * the dialog a station press opens: the same touch target, the same enlarged calendar and list,
   * the same stylesheet that keeps its buttons on screen. What is asserted is that it asks for all
   * of that — the geometry itself is the stylesheet's and cannot be measured here.
   */
  describe('drawn for a finger', () => {
    it('builds every control a finger presses at forty-four pixels', () => {
      coarse = true;
      render(
        <App>
          <TrackingEventEditDialog
            {...WATCH}
            tripLogId="trip-1"
            report={REPORT}
            teams={[{ id: 'team-1', title: 'One' }]}
            onClose={() => {}}
          />
        </App>,
      );

      const styles = Array.from(document.querySelectorAll('style'))
        .map((style) => style.textContent ?? '')
        .join('\n');
      expect(styles).toContain('--ant-control-height-lg:44px');
      expect(screen.getByTestId('trip-tracking-edit-team')).toHaveClass('ant-select-lg');
      expect(screen.getByRole('button', { name: /save the correction/i })).toHaveClass('ant-btn-lg');
      expect(screen.getByTestId('trip-tracking-edit-when-15')).toHaveClass('ant-btn-lg');
    });

    it('wears the stylesheet that keeps the dialog on the screen', () => {
      render(
        <App>
          <TrackingEventEditDialog {...WATCH} tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
        </App>,
      );
      // The class is the whole of the contract with the stylesheet: no wider than the screen, and
      // a body that scrolls under a footer that never leaves it.
      expect(document.querySelector('.ant-modal.tracking-report-dialog')).not.toBeNull();
    });

    it('marks its calendar to be pinned to the screen, since its fields scroll inside a capped body', async () => {
      coarse = true;
      render(
        <App>
          <TrackingEventEditDialog {...WATCH} tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
        </App>,
      );

      fireEvent.mouseDown(screen.getByTestId('trip-tracking-edit-recorded-at'));
      fireEvent.click(screen.getByTestId('trip-tracking-edit-recorded-at'));

      const popup = await waitFor(() => {
        const found = document.querySelector('.tracking-report-when-popup');
        expect(found).not.toBeNull();
        return found!;
      });
      expect(popup).toHaveClass('tracking-report-when-popup-pinned');
    });

    it('leaves a mouse the dense chrome it has everywhere else', () => {
      render(
        <App>
          <TrackingEventEditDialog {...WATCH} tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
        </App>,
      );

      expect(screen.getByRole('button', { name: /save the correction/i })).not.toHaveClass('ant-btn-lg');
    });
  });

  /**
   * The place, asked as the report card asks it.
   *
   * A correction is where a wrong place is repaired, and it used to be the one surface that could
   * neither offer the cave's places by name nor say that a depth lands far from any station. Each
   * case here is something the card could do and this dialog could not.
   */
  describe('the place it corrects', () => {
    const DEEP: TrackingEvent = { ...REPORT, kind: 'atDepth', stationName: null, depthEnteredM: 400 };
    /** The station the survey puts nearest to 400 m, which is a long way from it. */
    const FAR = {
      stationName: 'cave.deep.3',
      surveyName: null,
      depthM: 140,
      deltaM: 260,
      declared: false,
    };

    it('says at once that the recorded depth lands far from any station, with nothing retyped', () => {
      depthReading.mockReturnValue({ data: [FAR], isFetching: false, error: null, refetch: vi.fn() });
      render(
        <App>
          <TrackingEventEditDialog {...WATCH} tripLogId="trip-1" report={DEEP} teams={[]} onClose={() => {}} />
        </App>,
      );

      // Asked about on the render that opens the dialog — not a third of a second later, which is
      // the wait a typed number gets and exactly when somebody is reading what they opened.
      expect(depthReading.mock.calls[0][0]).toBe(400);
      expect(screen.getByTestId('trip-tracking-edit-depth-gap')).toHaveTextContent('cave.deep.3');
    });

    it('says nothing about a depth on a report that is at a station', () => {
      // The other half: the warning is about the depth this report holds, so a station report —
      // which the same reading could not be asked about — draws none and asks about none.
      depthReading.mockReturnValue({ data: [FAR], isFetching: false, error: null, refetch: vi.fn() });
      render(
        <App>
          <TrackingEventEditDialog {...WATCH} tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
        </App>,
      );

      expect(screen.queryByTestId('trip-tracking-edit-depth-gap')).toBeNull();
      expect(depthReading.mock.calls.every(([depth]) => depth === null)).toBe(true);
    });

    it('offers the places the cave declared, and corrects the report to the one chosen', async () => {
      declaredPlaces.mockReturnValue([
        { depthM: 96, stationName: 'cave.upper.2', placeLabel: 'Meandru', stationInModel: true },
      ]);
      render(
        <App>
          <TrackingEventEditDialog {...WATCH} tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
        </App>,
      );

      const chooser = screen.getByTestId('trip-tracking-edit-place');
      fireEvent.mouseDown(chooser.querySelector('.ant-select-selector') ?? chooser);
      await act(async () => {
        fireEvent.click(
          await waitFor(() => {
            const option = document.querySelector('.ant-select-item-option[title="Meandru — 96 m"]');
            expect(option).not.toBeNull();
            return option!;
          }),
        );
      });
      fireEvent.click(screen.getByRole('button', { name: /save the correction/i }));

      // As its depth and with no station: the server reads the depth through the declaration that
      // was chosen, so what was picked and what lands on the log cannot disagree.
      await waitFor(() => expect(update).toHaveBeenCalled());
      expect(update.mock.calls[0][0]).toMatchObject({ kind: 'atDepth', depthM: 96, stationName: null });
    });

    it('offers the stations of the survey the correction will be measured against', async () => {
      stationSearch.mockImplementation((_model: string | undefined, q: string) => ({
        data: q === '' ? undefined : { items: [{ viewerName: 'cave.deep.3' }], totalItems: 1 },
      }));
      render(
        <App>
          <TrackingEventEditDialog {...WATCH} tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
        </App>,
      );

      // The field answers to its label, which is what makes it reachable without a pointer.
      const station = screen.getByLabelText('Station');
      expect(station).toBe(screen.getByTestId('trip-tracking-edit-station'));
      fireEvent.change(station, { target: { value: 'cave.d' } });

      await waitFor(() => expect(stationSearch).toHaveBeenCalledWith('model-1', 'cave.d'));
      fireEvent.click(
        await waitFor(() => {
          const option = document.querySelector('.ant-select-item-option[title="cave.deep.3"]');
          expect(option).not.toBeNull();
          return option!;
        }),
      );
      expect(station).toHaveValue('cave.deep.3');
    });
  });

  /**
   * Whose report it is, and a moment that is probably a slip.
   *
   * One dialog serves every row and covers the row that opened it, so the title is the only thing
   * left on screen saying which report this is. And a moment before the watch began is legitimate
   * but rare, while a mistyped day lands there often — so it is warned about and never refused.
   */
  describe('what it says about the report', () => {
    it('names the person the report is about, where it is told who that is', () => {
      render(
        <App>
          <TrackingEventEditDialog
            {...WATCH}
            tripLogId="trip-1"
            report={REPORT}
            caverName="Ana Pop"
            teams={[]}
            onClose={() => {}}
          />
        </App>,
      );
      expect(screen.getByText(/Correct the report about Ana Pop of /)).toBeInTheDocument();
    });

    it('keeps its plain title where nobody is named', () => {
      render(
        <App>
          <TrackingEventEditDialog {...WATCH} tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
        </App>,
      );
      expect(screen.getByText('Correct this report')).toBeInTheDocument();
    });

    it('warns about a moment before the watch was started, and saves it all the same', async () => {
      render(
        <App>
          <TrackingEventEditDialog
            {...WATCH}
            tripLogId="trip-1"
            report={{ ...REPORT, recordedAt: '2026-09-11T10:00:00Z' }}
            teams={[]}
            onClose={() => {}}
          />
        </App>,
      );
      expect(screen.getByTestId('trip-tracking-edit-before-armed')).toBeInTheDocument();

      fireEvent.click(screen.getByRole('button', { name: /save the correction/i }));
      await waitFor(() => expect(update).toHaveBeenCalled());
      expect(update.mock.calls[0][0].recordedAt).toBe('2026-09-11T10:00:00.000Z');
    });

    it('says nothing about a moment after the watch was started, or on a watch never started', () => {
      const { unmount } = render(
        <App>
          <TrackingEventEditDialog {...WATCH} tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
        </App>,
      );
      expect(screen.queryByTestId('trip-tracking-edit-before-armed')).toBeNull();
      unmount();

      render(
        <App>
          <TrackingEventEditDialog
            surveyModelId={null}
            armedAt={null}
            tripLogId="trip-1"
            report={{ ...REPORT, recordedAt: '2026-09-11T10:00:00Z' }}
            teams={[]}
            onClose={() => {}}
          />
        </App>,
      );
      expect(screen.queryByTestId('trip-tracking-edit-before-armed')).toBeNull();
    });
  });
});

/**
 * Correcting a note about the cave. It stays what it is: the server refuses turning a report about
 * a person into one about the cave or the reverse, so neither is offered the other side.
 */
describe('TrackingEventEditDialog, a note about the cave', () => {
  const CAVE_NOTE: TrackingEvent = {
    ...REPORT,
    id: 'ev-cave',
    caverId: null,
    kind: 'caveNote',
    stationName: 'cave.upper.2',
    note: 'Loose rock above the pitch',
  };

  const kindsOffered = () => {
    const kind = screen.getByTestId('trip-tracking-edit-kind');
    fireEvent.mouseDown(kind.querySelector('.ant-select-selector') ?? kind);
    return Array.from(document.querySelectorAll('.ant-select-item-option')).map((option) =>
      option.getAttribute('title'),
    );
  };

  it('offers a report about a person no way to become a note about the cave', () => {
    render(
      <App>
        <TrackingEventEditDialog {...WATCH} tripLogId="trip-1" report={REPORT} teams={[]} onClose={() => {}} />
      </App>,
    );

    expect(kindsOffered()).toEqual(['Went in', 'At a station', 'At a depth', 'Note', 'Came out']);
  });

  it('states the kind of a note about the cave rather than offering another, and sends its station and words about no team', async () => {
    render(
      <App>
        <TrackingEventEditDialog
          {...WATCH}
          tripLogId="trip-1"
          report={CAVE_NOTE}
          teams={[{ id: 'team-1', title: 'Echipa 1' }]}
          onClose={() => {}}
        />
      </App>,
    );

    expect(screen.getByTestId('trip-tracking-edit-kind')).toHaveClass('ant-select-disabled');
    expect(screen.getByTestId('trip-tracking-edit-kind')).toHaveTextContent('About the cave');
    expect(screen.queryByTestId('trip-tracking-edit-team')).toBeNull();
    expect(screen.getByTestId('trip-tracking-edit-station')).toHaveValue('cave.upper.2');

    fireEvent.change(screen.getByTestId('trip-tracking-edit-note'), {
      target: { value: 'Loose rock above the second pitch' },
    });
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /save the correction/i }));
    });

    await waitFor(() => expect(update).toHaveBeenCalledOnce());
    expect(update.mock.calls[0][0]).toMatchObject({
      eventId: 'ev-cave',
      kind: 'caveNote',
      stationName: 'cave.upper.2',
      toStationName: null,
      depthM: null,
      teamId: null,
      note: 'Loose rock above the second pitch',
    });
  });
});
