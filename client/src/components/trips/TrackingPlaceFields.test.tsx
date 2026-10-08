// SPDX-License-Identifier: AGPL-3.0-or-later
import { App, Form } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import '../../i18n';

/** The places the watch's cave has declared, as the server sends them — or undefined, its refusal. */
const declaredPlaces = vi.fn();
/** What a depth means on the watch's survey, and which depth was asked about. */
const depthReading = vi.fn();
/** The survey's stations beginning with what has been typed, and which survey was asked. */
const stationSearch = vi.fn();

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useTrackingPlaces: () => ({ data: declaredPlaces() }),
    useTrackingDepthReading: (_tripLogId: string, depthM: number | null) => depthReading(depthM),
    useSurveyModelStationSearch: (surveyModelId: string | undefined, q: string) =>
      stationSearch(surveyModelId, q),
  };
});

const { default: TrackingPlaceFields, TrackingStationField } = await import(
  './TrackingPlaceFields.tsx'
);
type Kind = import('../../api/hooks.ts').TripPositionEventKind;

interface Values {
  kind: Kind;
  stationName?: string;
  depthM?: number | null;
}

/** What the form around the block holds, read whenever a case wants to know what would be sent. */
let held: () => Values = () => ({ kind: 'entered' });

/**
 * The block inside a form of the shape every surface gives it: a kind it is told, and a station
 * and a depth it reads and fills. The kind is watched as the surfaces watch it, because choosing a
 * declared place or a candidate station changes it.
 */
function Surface({
  kind,
  surveyModelId = 'model-1',
  caveId = null,
  seedDepth,
}: {
  kind: Kind;
  surveyModelId?: string | null;
  caveId?: string | null;
  seedDepth?: number | null;
}) {
  const [form] = Form.useForm<Values>();
  const watched = Form.useWatch('kind', form) ?? kind;
  held = () => form.getFieldsValue(true);
  return (
    <Form form={form} layout="vertical" initialValues={{ kind, depthM: seedDepth ?? undefined }}>
      <Form.Item name="kind" hidden>
        <input />
      </Form.Item>
      <TrackingPlaceFields
        tripLogId="trip-1"
        kind={watched}
        size="middle"
        idPrefix="surface"
        surveyModelId={surveyModelId}
        caveId={caveId}
        seedDepth={seedDepth}
      />
    </Form>
  );
}

function show(props: Parameters<typeof Surface>[0]) {
  return render(
    <MemoryRouter>
      <App>
        <Surface {...props} />
      </App>
    </MemoryRouter>,
  );
}

/** The options of whichever list is open under a field, found through one the caller expects. */
async function optionTitled(title: string) {
  return waitFor(() => {
    const found = document.querySelector<HTMLElement>(`.ant-select-item-option[title="${title}"]`);
    expect(found).not.toBeNull();
    return found!;
  });
}

const stations = (...names: string[]) => ({
  items: names.map((viewerName) => ({ viewerName })),
  totalItems: names.length,
});

beforeEach(() => {
  declaredPlaces.mockReset().mockReturnValue([]);
  depthReading
    .mockReset()
    .mockReturnValue({ data: undefined, isFetching: false, error: null, refetch: vi.fn() });
  stationSearch.mockReset().mockReturnValue({ data: undefined });
});
afterEach(cleanup);

// antd names a deprecated prop through `console.error`, which the browser sweep files as a defect
// on every page that draws it — so nothing drawn here may speak that way.
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
 * A station's name, offered while it is typed.
 *
 * The cases are the ways an offer could turn into something else: names from a survey the report
 * will not be measured against, a list that refuses what it does not hold, or a field that asks
 * about a survey its reader was never told of.
 */
describe('TrackingPlaceFields, the station', () => {
  it('answers to its label and offers the stations of the watch\'s own survey', async () => {
    stationSearch.mockImplementation((_model: string | undefined, q: string) => ({
      data: q === '' ? undefined : stations('cave.upper.1', 'cave.upper.2'),
    }));
    show({ kind: 'atStation' });

    const station = screen.getByLabelText('Station');
    expect(station).toBe(screen.getByTestId('surface-station'));
    fireEvent.change(station, { target: { value: 'cave.u' } });

    await waitFor(() => expect(stationSearch).toHaveBeenCalledWith('model-1', 'cave.u'));
    fireEvent.click(await optionTitled('cave.upper.2'));
    expect(held().stationName).toBe('cave.upper.2');
  });

  it('takes a name the survey\'s list does not hold', async () => {
    // The list offers and the server judges: a name it does not hold may be one the survey gained
    // a minute ago, and somebody must be able to write down what they were told.
    stationSearch.mockImplementation((_model: string | undefined, q: string) => ({
      data: q === '' ? undefined : stations('cave.upper.1'),
    }));
    show({ kind: 'atStation' });

    fireEvent.change(screen.getByTestId('surface-station'), { target: { value: 'cave.unheard.9' } });
    await waitFor(() => expect(stationSearch).toHaveBeenCalledWith('model-1', 'cave.unheard.9'));
    expect(held().stationName).toBe('cave.unheard.9');
  });

  it('says how many more names there are than it lists', async () => {
    stationSearch.mockImplementation((_model: string | undefined, q: string) => ({
      data: q === '' ? undefined : { ...stations('cave.a.1', 'cave.a.2'), totalItems: 240 },
    }));
    show({ kind: 'atStation' });

    fireEvent.change(screen.getByTestId('surface-station'), { target: { value: 'cave' } });
    const listed = await optionTitled('cave.a.1');
    const list = listed.closest('.ant-select-dropdown')!;
    expect(list.textContent).toContain('More stations: 238');
  });

  it('hangs no list under a name that is already the only match', async () => {
    // Nothing is left to choose, and a list open over the fields below would be in the way of
    // the next press. The other half is the case above: with more than one match it is drawn.
    stationSearch.mockImplementation((_model: string | undefined, q: string) => ({
      data: q === '' ? undefined : stations('cave.deep.3'),
    }));
    show({ kind: 'atStation' });

    fireEvent.change(screen.getByTestId('surface-station'), { target: { value: 'cave.deep.3' } });
    await waitFor(() => expect(stationSearch).toHaveBeenCalledWith('model-1', 'cave.deep.3'));
    expect(document.querySelector('.ant-select-item-option')).toBeNull();
  });

  it('asks about no survey where the reader is not told which one the watch is on', async () => {
    // The state a reader from whom the watch's setup is withheld is in: the survey arrives as
    // nothing. The field is then a text box, and beside it the same field told the survey asks.
    stationSearch.mockReturnValue({ data: stations('cave.upper.1') });
    const { unmount } = show({ kind: 'atStation', surveyModelId: null });

    fireEvent.change(screen.getByTestId('surface-station'), { target: { value: 'cave.u' } });
    await new Promise((resolve) => setTimeout(resolve, 350));
    expect(stationSearch.mock.calls.every(([model]) => model === undefined)).toBe(true);
    expect(held().stationName).toBe('cave.u');
    unmount();

    stationSearch.mockClear();
    show({ kind: 'atStation', surveyModelId: 'model-1' });
    fireEvent.change(screen.getByTestId('surface-station'), { target: { value: 'cave.u' } });
    await waitFor(() => expect(stationSearch).toHaveBeenCalledWith('model-1', 'cave.u'));
  });

  it('is the same field on its own, for the surface that asks for nothing else', () => {
    render(
      <App>
        <Form layout="vertical">
          <TrackingStationField idPrefix="pressed" surveyModelId="model-1" help="Why it is asked" />
        </Form>
      </App>,
    );
    expect(screen.getByLabelText('Station')).toBe(screen.getByTestId('pressed-station'));
    expect(screen.getByText('Why it is asked')).toBeInTheDocument();
  });
});

/**
 * The cave's declared places.
 *
 * What is worth pinning is what each state of the list says: a cave that declares places offers
 * them, one whose declaration the survey cannot follow says so where the choice is made, a cave
 * that declares nothing says where places are declared — and a list that was refused says nothing
 * at all, because "this cave declares no places" would then be a false statement about the cave.
 */
describe('TrackingPlaceFields, the declared places', () => {
  const MEANDRU = { depthM: 96, stationName: 'cave.upper.2', placeLabel: 'Meandru' };

  it('marks a declared place whose station the watch\'s survey does not hold, and no other', async () => {
    declaredPlaces.mockReturnValue([
      { ...MEANDRU, stationInModel: true },
      { depthM: 150, stationName: 'cave.gone.7', placeLabel: 'Sala Mare', stationInModel: false },
    ]);
    show({ kind: 'atDepth' });

    const chooser = screen.getByTestId('surface-place');
    fireEvent.mouseDown(chooser.querySelector('.ant-select-selector') ?? chooser);

    const followed = await optionTitled('Meandru — 96 m');
    const list = followed.closest('.ant-select-dropdown')!;
    const titles = Array.from(list.querySelectorAll('.ant-select-item-option')).map((option) =>
      option.getAttribute('title'),
    );
    expect(titles).toEqual(['Meandru — 96 m', 'Sala Mare — 150 m — station not in this survey']);
  });

  it('is reached by its label, and choosing a place reports its depth', async () => {
    declaredPlaces.mockReturnValue([{ ...MEANDRU, stationInModel: true }]);
    show({ kind: 'atStation' });

    const chooser = screen.getByLabelText('Place');
    expect(chooser).toHaveAttribute('role', 'combobox');
    fireEvent.mouseDown(
      screen.getByTestId('surface-place').querySelector('.ant-select-selector') ??
        screen.getByTestId('surface-place'),
    );
    fireEvent.click(await optionTitled('Meandru — 96 m'));

    await waitFor(() => expect(held()).toMatchObject({ kind: 'atDepth', depthM: 96 }));
    expect(held().stationName).toBeUndefined();
  });

  it('says where places are declared when the cave declares none, and points at the cave', () => {
    show({ kind: 'atStation', caveId: 'cave-7' });

    expect(screen.getByTestId('surface-place-none')).toHaveTextContent(/declared no places/);
    expect(screen.getByTestId('surface-place-none-cave')).toHaveAttribute('href', '/caves/cave-7');
    expect(screen.queryByTestId('surface-place')).toBeNull();
  });

  it('points at no cave where the reader is not told which cave the watch is in', () => {
    show({ kind: 'atStation', caveId: null });

    expect(screen.getByTestId('surface-place-none')).toBeInTheDocument();
    expect(screen.queryByTestId('surface-place-none-cave')).toBeNull();
  });

  it('says nothing about the cave\'s places where the list was refused', () => {
    // Refused is not empty. A caller who may not place the cave is refused the list rather than
    // handed an empty one; the positive case is the two above, where the list did arrive empty.
    declaredPlaces.mockReturnValue(undefined);
    show({ kind: 'atStation', caveId: 'cave-7' });

    expect(screen.getByTestId('surface-station')).toBeInTheDocument();
    expect(screen.queryByTestId('surface-place-none')).toBeNull();
    expect(screen.queryByTestId('surface-place')).toBeNull();
  });

  it('draws nothing for a report that claims no place', () => {
    declaredPlaces.mockReturnValue([{ ...MEANDRU, stationInModel: true }]);
    show({ kind: 'exited', caveId: 'cave-7' });

    expect(screen.queryByTestId('surface-place')).toBeNull();
    expect(screen.queryByTestId('surface-place-none')).toBeNull();
    expect(screen.queryByTestId('surface-station')).toBeNull();
    expect(screen.queryByTestId('surface-depth')).toBeNull();
  });
});

/**
 * A depth the form already holds when it opens.
 *
 * A typed depth is asked about only once it has stopped changing. A depth that was never typed —
 * the one a report on the log was recorded with — has nothing to wait for, and waiting would leave
 * the form silent at exactly the moment it is read.
 */
describe('TrackingPlaceFields, a depth it opens on', () => {
  const FAR = { stationName: 'cave.deep.3', surveyName: null, depthM: 140, deltaM: 260, declared: false };

  it('asks about the depth it was opened on at once, and warns when it lands far away', () => {
    depthReading.mockReturnValue({ data: [FAR], isFetching: false, error: null, refetch: vi.fn() });
    show({ kind: 'atDepth', seedDepth: 400 });

    expect(depthReading.mock.calls[0][0]).toBe(400);
    expect(screen.getByTestId('surface-depth-gap')).toHaveTextContent('cave.deep.3');
  });

  it('waits for a typed depth to settle before asking about it', async () => {
    depthReading.mockImplementation((depth: number | null) => ({
      data: depth === null ? undefined : [FAR],
      isFetching: false,
      error: null,
      refetch: vi.fn(),
    }));
    show({ kind: 'atDepth' });

    fireEvent.change(screen.getByTestId('surface-depth'), { target: { value: '400' } });
    // Not yet: every digit on the way to a number is a depth somewhere.
    expect(depthReading.mock.calls.every(([depth]) => depth === null)).toBe(true);
    expect(screen.queryByTestId('surface-depth-gap')).toBeNull();

    expect(await screen.findByTestId('surface-depth-gap')).toBeInTheDocument();
  });
});
