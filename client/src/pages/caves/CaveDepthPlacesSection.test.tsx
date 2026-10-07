// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import '../../i18n';

const write = vi.fn();
const remove = vi.fn();
const places = vi.fn();
/** The cave's uploaded surveys, as the list this reader is given holds them. */
const models = vi.fn();
/** The stations of one survey beginning with what has been typed, and which survey was asked. */
const stationSearch = vi.fn();

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useCaveDepthPlaces: () => ({ data: places() }),
    useWriteCaveDepthPlace: () => ({ mutateAsync: write, isPending: false }),
    useDeleteCaveDepthPlace: () => ({ mutateAsync: remove, isPending: false }),
    useSurveyModels: () => ({ data: models() }),
    useSurveyModelStationSearch: (surveyModelId: string | undefined, q: string) =>
      stationSearch(surveyModelId, q),
  };
});

const { default: CaveDepthPlacesSection } = await import('./CaveDepthPlacesSection.tsx');

const DECLARED = [
  { id: 'p-1', depthM: 96, stationName: 'upper.2', placeLabel: 'Meandru', stationInSurvey: true },
  { id: 'p-2', depthM: 150, stationName: 'deep.3', placeLabel: null, stationInSurvey: true },
];

/** A cave's surveys: an older line plot, the one marked current, and a current file of walls. */
const SURVEYS = [
  { id: 'model-old', isCurrent: false, format: 'survex3d', status: 'ready' },
  { id: 'model-current', isCurrent: true, format: 'lox', status: 'ready' },
  { id: 'model-walls', isCurrent: true, format: 'stl', status: 'ready' },
];

beforeEach(() => {
  write.mockReset().mockResolvedValue(DECLARED[0]);
  remove.mockReset().mockResolvedValue(undefined);
  places.mockReset().mockReturnValue(DECLARED);
  models.mockReset().mockReturnValue(SURVEYS);
  stationSearch.mockReset().mockReturnValue({ data: undefined });
});
afterEach(cleanup);

/**
 * antd names a deprecated prop through `console.error`, once per render — and the browser sweep
 * files every one of those as a defect. The depth field carried one on every cave page this card
 * was drawn on, and nothing here noticed, so every case now also checks that nothing it drew spoke
 * that way.
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
 * Declaring what a cave's depths mean.
 *
 * What is worth testing here is not the table but the two things a reader could be misled about: a
 * cave that has declared nothing must say what happens instead, and somebody without write access
 * must not be offered controls that would be refused.
 */
describe('CaveDepthPlacesSection', () => {
  it('shows each declaration as a depth, a station and a name', () => {
    render(
      <App>
        <CaveDepthPlacesSection caveId="cave-1" canEdit />
      </App>,
    );
    expect(screen.getByText('96 m')).toBeInTheDocument();
    expect(screen.getByText('upper.2')).toBeInTheDocument();
    expect(screen.getByText('Meandru')).toBeInTheDocument();
    // A declaration with no name is still a declaration; it is said to be unnamed rather than
    // drawn as a blank cell somebody would read as a loading row.
    expect(screen.getByText(/Unnamed|Fără nume/)).toBeInTheDocument();
  });

  it('says what happens to a depth when the cave has declared nothing', () => {
    // An empty table would read as "this feature is broken" rather than "every depth here lands
    // on whichever station is nearest", which is the fact somebody needs.
    places.mockReturnValue([]);
    render(
      <App>
        <CaveDepthPlacesSection caveId="cave-1" canEdit />
      </App>,
    );
    const empty = screen.getByTestId('cave-depth-places-empty');
    expect(empty).toBeInTheDocument();
    // Asserted on the empty notice itself: the card's own help text also says "nearest", and a
    // whole-document match would pass on that sentence with the notice missing entirely.
    expect(empty.textContent).toMatch(/nearest|apropiat/);
  });

  it('declares a depth with the station and the name that were typed', async () => {
    render(
      <App>
        <CaveDepthPlacesSection caveId="cave-1" canEdit />
      </App>,
    );
    fireEvent.click(screen.getByTestId('cave-depth-place-add'));

    fireEvent.change(screen.getByTestId('cave-depth-place-depth'), { target: { value: '110' } });
    fireEvent.change(screen.getByTestId('cave-depth-place-station'), { target: { value: 'deep.4' } });
    fireEvent.change(screen.getByTestId('cave-depth-place-label'), { target: { value: 'Sala Mare' } });
    fireEvent.click(screen.getByTestId('cave-depth-place-save'));

    await waitFor(() => expect(write).toHaveBeenCalledOnce());
    expect(write.mock.calls[0][0]).toEqual({
      depthM: 110,
      stationName: 'deep.4',
      placeLabel: 'Sala Mare',
    });
  });

  it('declares a depth without a name rather than with an empty one', async () => {
    // Null and "" mean different things to the reader of a declaration: one is "this place has no
    // name", the other is a name that is a blank string and would be offered in a chooser.
    render(
      <App>
        <CaveDepthPlacesSection caveId="cave-1" canEdit />
      </App>,
    );
    fireEvent.click(screen.getByTestId('cave-depth-place-add'));
    fireEvent.change(screen.getByTestId('cave-depth-place-depth'), { target: { value: '110' } });
    fireEvent.change(screen.getByTestId('cave-depth-place-station'), { target: { value: 'deep.4' } });
    fireEvent.click(screen.getByTestId('cave-depth-place-save'));

    await waitFor(() => expect(write).toHaveBeenCalledOnce());
    expect(write.mock.calls[0][0].placeLabel).toBeNull();
  });

  it('will not declare a depth with no station, because that is not a declaration', async () => {
    render(
      <App>
        <CaveDepthPlacesSection caveId="cave-1" canEdit />
      </App>,
    );
    fireEvent.click(screen.getByTestId('cave-depth-place-add'));
    fireEvent.change(screen.getByTestId('cave-depth-place-depth'), { target: { value: '110' } });
    fireEvent.click(screen.getByTestId('cave-depth-place-save'));

    await waitFor(() => expect(screen.getByText(/which station|care stație/)).toBeInTheDocument());
    expect(write).not.toHaveBeenCalled();
  });

  /**
   * Correcting a declaration.
   *
   * The depth is the key, so a write at a changed depth is a second declaration rather than a
   * change to the first — and the control that led here is labelled Edit, so nobody expects the
   * old row to survive. Left in place, the same place is declared twice: the report chooser offers
   * two "Meandru", and an imported row naming it is refused because which station is meant cannot
   * be decided.
   */
  it('withdraws the old declaration when a correction moves it to another depth', async () => {
    render(
      <App>
        <CaveDepthPlacesSection caveId="cave-1" canEdit />
      </App>,
    );
    fireEvent.click(screen.getByTestId('cave-depth-place-edit-p-1'));
    // Filled from the row, to a tenth of a metre, which is how the field draws a whole number.
    expect((screen.getByTestId('cave-depth-place-depth') as HTMLInputElement).value).toMatch(
      /^96(\.0)?$/,
    );
    fireEvent.change(screen.getByTestId('cave-depth-place-depth'), { target: { value: '97' } });
    fireEvent.click(screen.getByTestId('cave-depth-place-save'));

    await waitFor(() => expect(remove).toHaveBeenCalledOnce());
    expect(write).toHaveBeenCalledOnce();
    expect(write.mock.calls[0][0]).toEqual({
      depthM: 97,
      stationName: 'upper.2',
      placeLabel: 'Meandru',
    });
    expect(remove.mock.calls[0][0]).toEqual({ id: 'p-1' });
    // Written first and withdrawn after, so a write that fails leaves the cave still declaring
    // the place under its old depth rather than declaring nothing there.
    expect(write.mock.invocationCallOrder[0]).toBeLessThan(remove.mock.invocationCallOrder[0]);
  });

  it('withdraws nothing when a correction keeps the depth', async () => {
    // Writing the same depth replaces the declaration on the server, which is the whole of what a
    // correction to the station or the name needs.
    render(
      <App>
        <CaveDepthPlacesSection caveId="cave-1" canEdit />
      </App>,
    );
    fireEvent.click(screen.getByTestId('cave-depth-place-edit-p-1'));
    fireEvent.change(screen.getByTestId('cave-depth-place-label'), {
      target: { value: 'Meandrul Mare' },
    });
    fireEvent.click(screen.getByTestId('cave-depth-place-save'));

    await waitFor(() => expect(write).toHaveBeenCalledOnce());
    expect(write.mock.calls[0][0]).toEqual({
      depthM: 96,
      stationName: 'upper.2',
      placeLabel: 'Meandrul Mare',
    });
    expect(remove).not.toHaveBeenCalled();
  });

  it('withdraws nothing for a fresh declaration, even one typed right after an edit was abandoned', async () => {
    // What Edit remembered belongs to that edit. Cancelled, it must not make the next declaration
    // withdraw a row it has nothing to do with.
    render(
      <App>
        <CaveDepthPlacesSection caveId="cave-1" canEdit />
      </App>,
    );
    fireEvent.click(screen.getByTestId('cave-depth-place-edit-p-1'));
    fireEvent.click(screen.getByRole('button', { name: /cancel|renunță/i }));
    fireEvent.click(screen.getByTestId('cave-depth-place-add'));
    fireEvent.change(screen.getByTestId('cave-depth-place-depth'), { target: { value: '110' } });
    fireEvent.change(screen.getByTestId('cave-depth-place-station'), { target: { value: 'deep.4' } });
    fireEvent.click(screen.getByTestId('cave-depth-place-save'));

    await waitFor(() => expect(write).toHaveBeenCalledOnce());
    expect(remove).not.toHaveBeenCalled();
  });

  it('says the place is declared twice when the old declaration could not be withdrawn', async () => {
    // The new declaration has landed by then, so this is not a failed save and must not be worded
    // as one: what the reader has to go and do is withdraw the old row by hand.
    remove.mockRejectedValue(new Error('refused'));
    render(
      <App>
        <CaveDepthPlacesSection caveId="cave-1" canEdit />
      </App>,
    );
    fireEvent.click(screen.getByTestId('cave-depth-place-edit-p-1'));
    fireEvent.change(screen.getByTestId('cave-depth-place-depth'), { target: { value: '97' } });
    fireEvent.click(screen.getByTestId('cave-depth-place-save'));

    expect(await screen.findByText(/could not be withdrawn|nu a putut fi retrasă/)).toBeInTheDocument();
    expect(screen.queryByText(/That could not be saved|Nu s-a putut salva/)).toBeNull();
  });

  it('offers no controls at all without write access', () => {
    render(
      <App>
        <CaveDepthPlacesSection caveId="cave-1" canEdit={false} />
      </App>,
    );
    // The list is still worth reading — it says where reported depths land — so it is shown; what
    // is absent is every way of changing it, rather than a button that would be refused.
    expect(screen.getByText('Meandru')).toBeInTheDocument();
    expect(screen.queryByTestId('cave-depth-place-add')).not.toBeInTheDocument();
    expect(screen.queryByTestId('cave-depth-place-save')).not.toBeInTheDocument();
  });

  /**
   * A declaration whose station the cave's survey does not hold.
   *
   * Such a declaration is not followed — reports of that depth go back to the nearest station —
   * and nothing used to say so. The server answers per row; what is tested here is that only an
   * outright "no" is marked, because the other two answers must not be told apart on screen: a row
   * the server would not speak about has to look exactly like one whose station is there.
   */
  describe('a station the survey does not hold', () => {
    it('marks the declaration the survey has no station for, and says what that means', () => {
      places.mockReturnValue([
        { ...DECLARED[0], stationInSurvey: false },
        { ...DECLARED[1], stationInSurvey: true },
      ]);
      render(
        <App>
          <CaveDepthPlacesSection caveId="cave-1" canEdit />
        </App>,
      );

      expect(screen.getByTestId('cave-depth-place-not-in-survey-p-1')).toHaveTextContent(
        'Not in the current survey',
      );
      expect(screen.queryByTestId('cave-depth-place-not-in-survey-p-2')).toBeNull();
      expect(screen.getByTestId('cave-depth-places-not-in-survey-help').textContent).toMatch(
        /nearest station/,
      );
    });

    it('draws nothing where the server does not say, exactly as where the station is there', () => {
      // Null is the server keeping quiet: no survey read yet, or a reader who may not be told the
      // cave's survey. A mark here would tell that reader something about a survey they were
      // refused; so would a "found" mark on the other row, which is why there is no such mark.
      places.mockReturnValue([
        { ...DECLARED[0], stationInSurvey: null },
        { ...DECLARED[1], stationInSurvey: true },
      ]);
      render(
        <App>
          <CaveDepthPlacesSection caveId="cave-1" canEdit />
        </App>,
      );

      expect(screen.getByText('upper.2')).toBeInTheDocument();
      expect(document.querySelectorAll('[data-testid^="cave-depth-place-not-in-survey-"]')).toHaveLength(0);
      expect(screen.queryByTestId('cave-depth-places-not-in-survey-help')).toBeNull();
    });
  });

  /**
   * The station, offered while it is typed.
   *
   * Offered out of the survey marked as the cave's current line plot and no other, and never
   * required: a cave can declare a place before its survey is uploaded, and a reader who is not
   * told the survey's stations is offered nothing and can still write the name down.
   */
  describe('the station as it is typed', () => {
    it('offers the current line plot\'s stations, and takes the one chosen', async () => {
      stationSearch.mockImplementation((_model: string | undefined, q: string) => ({
        data:
          q === ''
            ? undefined
            : { items: [{ viewerName: 'deep.4' }, { viewerName: 'deep.41' }], totalItems: 2 },
      }));
      render(
        <App>
          <CaveDepthPlacesSection caveId="cave-1" canEdit />
        </App>,
      );
      fireEvent.click(screen.getByTestId('cave-depth-place-add'));

      // Reached by its label, so without a pointer too.
      const station = screen.getByLabelText('Station');
      expect(station).toBe(screen.getByTestId('cave-depth-place-station'));
      fireEvent.change(screen.getByTestId('cave-depth-place-depth'), { target: { value: '110' } });
      fireEvent.change(station, { target: { value: 'deep' } });

      // The line plot marked current — not the older line plot, and not the current file of
      // walls, which holds no station at all.
      await waitFor(() => expect(stationSearch).toHaveBeenCalledWith('model-current', 'deep'));
      fireEvent.click(
        await waitFor(() => {
          const option = document.querySelector('.ant-select-item-option[title="deep.41"]');
          expect(option).not.toBeNull();
          return option!;
        }),
      );
      fireEvent.click(screen.getByTestId('cave-depth-place-save'));

      await waitFor(() => expect(write).toHaveBeenCalledOnce());
      expect(write.mock.calls[0][0]).toMatchObject({ depthM: 110, stationName: 'deep.41' });
    });

    it('asks no survey and still takes the name where the cave has no current line plot', async () => {
      // What a reader who may not place the cave is given too: their list of the cave's surveys
      // arrives empty, so there is nothing to offer from and nothing is asked.
      models.mockReturnValue([]);
      render(
        <App>
          <CaveDepthPlacesSection caveId="cave-1" canEdit />
        </App>,
      );
      fireEvent.click(screen.getByTestId('cave-depth-place-add'));
      fireEvent.change(screen.getByTestId('cave-depth-place-depth'), { target: { value: '110' } });
      fireEvent.change(screen.getByTestId('cave-depth-place-station'), { target: { value: 'deep.4' } });
      fireEvent.click(screen.getByTestId('cave-depth-place-save'));

      await waitFor(() => expect(write).toHaveBeenCalledOnce());
      expect(write.mock.calls[0][0]).toMatchObject({ stationName: 'deep.4' });
      expect(stationSearch.mock.calls.every(([model]) => model === undefined)).toBe(true);
    });

    it('offers nothing from a survey standing in for a current one that could not be read', async () => {
      // The marked upload failed to read and a later one was read fine. The later one answers
      // for the cave's figures, but it is not the survey the page calls current and not the one
      // the rows are judged against — the server says nothing about any row in this state — so
      // its names are not offered either. The two halves of the card are about one survey or none.
      models.mockReturnValue([
        { id: 'model-failed', isCurrent: true, format: 'lox', status: 'failed' },
        { id: 'model-stand-in', isCurrent: false, format: 'lox', status: 'ready' },
      ]);
      places.mockReturnValue(DECLARED.map((place) => ({ ...place, stationInSurvey: null })));
      render(
        <App>
          <CaveDepthPlacesSection caveId="cave-1" canEdit />
        </App>,
      );
      expect(document.querySelectorAll('[data-testid^="cave-depth-place-not-in-survey-"]')).toHaveLength(0);

      fireEvent.click(screen.getByTestId('cave-depth-place-add'));
      fireEvent.change(screen.getByTestId('cave-depth-place-depth'), { target: { value: '110' } });
      fireEvent.change(screen.getByTestId('cave-depth-place-station'), { target: { value: 'deep.4' } });
      fireEvent.click(screen.getByTestId('cave-depth-place-save'));

      await waitFor(() => expect(write).toHaveBeenCalledOnce());
      expect(write.mock.calls[0][0]).toMatchObject({ stationName: 'deep.4' });
      expect(stationSearch.mock.calls.every(([model]) => model === undefined)).toBe(true);
    });
  });
});
