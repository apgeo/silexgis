// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type {
  CaveViewSectionBounds,
  CaveViewTreeNode,
  CaveViewViewpoint,
  Cv2Namespace,
} from '../../caveview/loadCaveView.ts';
import { COMPARE_COLOURS, type SurveyCompareOffer } from '../../caveview/surveyCompare.ts';
import { resetViewControlsForTests } from '../../viewlinks/viewTargets.ts';
import CaveViewPanel from './CaveViewPanel.tsx';

// The panel reads these to choose the viewer's own controls. jsdom lays nothing out and answers
// every media query false, so both are stated.
vi.mock('../../hooks/useIsMobile.ts', () => ({ useIsMobile: () => false }));
vi.mock('../../hooks/useCoarsePointer.ts', () => ({ useCoarsePointer: () => false }));

// ---- a fake of the vendored viewer, one object per viewer built ----
//
// A comparison is two or three viewers at once, and everything asserted here is about which of
// them was told what: so each fake keeps its own listeners, its own hidden surveys and its own
// record of what it was loaded with, rather than sharing one of each across the file.

type Listener = (event: unknown) => void;
/** A survey tree written as nested names: an object is a survey, null a station. */
type Spec = { [name: string]: Spec | null };

const tree = (spec: Spec, name = ''): CaveViewTreeNode => ({
  name,
  isStation: () => false,
  children: Object.entries(spec).map(([child, inner]) =>
    inner === null ? { name: child, isStation: () => true, children: [] } : tree(inner, child),
  ),
});

interface Loaded {
  name: string;
  label?: string;
  color?: string;
}

const SHADING_SURVEY = 6;
const viewers: FakeViewer[] = [];
const toolbars: { viewer: FakeViewer; buttons: readonly string[] }[] = [];

class FakeViewer {
  listeners = new Map<string, Set<Listener>>();
  loaded: Loaded[] = [];
  disposed = false;
  shadingMode: number | undefined = undefined;
  liveMarkerLabels = true;
  survey: CaveViewTreeNode = tree({});
  bounds = new Map<string, CaveViewSectionBounds>();
  hidden = new Set<string>();
  viewpoint: CaveViewViewpoint = {
    azimuth: 0,
    polar: 0,
    zoom: 1,
    target: { x: 0, y: 0, z: 0 },
    cameraType: 2,
  };

  containerId: string;
  config: Record<string, unknown>;

  constructor(containerId: string, config: Record<string, unknown>) {
    this.containerId = containerId;
    this.config = config;
    viewers.push(this);
  }

  addEventListener(type: string, listener: Listener) {
    const heard = this.listeners.get(type) ?? new Set();
    heard.add(listener);
    this.listeners.set(type, heard);
  }
  removeEventListener(type: string, listener: Listener) {
    this.listeners.get(type)?.delete(listener);
  }
  emit(type: string, event: unknown = {}) {
    for (const listener of [...(this.listeners.get(type) ?? [])]) {
      listener(event);
    }
  }

  renderView = vi.fn();
  resize = vi.fn();
  clearHighlight = vi.fn();
  setLiveMarkerClusterLabel = vi.fn();
  getLiveMarkers = vi.fn(() => []);

  getSurveyTree = () => this.survey;
  getSectionBounds = (ref: unknown) => this.bounds.get(JSON.stringify(ref)) ?? null;
  getHiddenSections = () => [...this.hidden].map((path) => JSON.parse(path) as string[]);
  setSectionVisible = vi.fn((ref: unknown, visible: boolean) => {
    if (visible) {
      this.hidden.delete(JSON.stringify(ref));
    } else {
      this.hidden.add(JSON.stringify(ref));
    }
    return true;
  });
  showAllSections = vi.fn(() => {
    this.hidden.clear();
    return true;
  });
  getViewpoint = vi.fn((): CaveViewViewpoint | null => this.viewpoint);
  setViewpoint = vi.fn(() => true);

  /** The model arriving, as the real viewer says it: with the survey it has just drawn. */
  arrive() {
    act(() => this.emit('newCave', { survey: {} }));
  }
}

class FakeUi {
  private viewer: FakeViewer;

  constructor(viewer: FakeViewer) {
    this.viewer = viewer;
  }
  loadCave(file: File) {
    this.viewer.loaded = [{ name: file.name }];
  }
  loadCaves(files: readonly { file: File; label?: string; color?: string }[]) {
    this.viewer.loaded = files.map(({ file, label, color }) => ({ name: file.name, label, color }));
  }
  dispose() {
    this.viewer.disposed = true;
  }
}

class FakeToolbar {
  constructor(viewer: FakeViewer, _container: unknown, options: { buttons?: readonly string[] }) {
    toolbars.push({ viewer, buttons: options.buttons ?? [] });
  }
  dispose() {}
}

// ---- two surveys of one cave ----

const EARLY = 'Survey 2023';
const LATE = 'Survey 2024';

const offer: SurveyCompareOffer = {
  current: { id: 'model-early', name: EARLY, fileUrl: 'http://files.local/early', fileName: 'Survey 2023.3d' },
  others: [{ id: 'model-late', name: LATE, fileUrl: 'http://files.local/late', fileName: 'Survey 2024.lox' }],
};

/** What the earlier survey holds, and what the later one does: a series was found in between. */
const earlySurveys: Spec = { p8: { main: { 1: null }, bens_dig: { 1: null } } };
const lateSurveys: Spec = { p8: { main: { 1: null }, bens_dig: { 1: null }, new_series: { 1: null } } };

const here: CaveViewSectionBounds = { min: { x: 0, y: 0, z: -40 }, max: { x: 300, y: 450, z: 20 } };
const elsewhere: CaveViewSectionBounds = {
  min: { x: 512_300, y: 468_900, z: 1180 },
  max: { x: 512_600, y: 469_350, z: 1240 },
};

const text = (key: string, options?: Record<string, unknown>) => i18n.t(key, options);

/** A panel on the earlier survey with its model drawn, offered the later one to compare with. */
async function renderPanel(props: Partial<Parameters<typeof CaveViewPanel>[0]> = {}) {
  const view = render(
    <CaveViewPanel
      fileUrl="http://files.local/early"
      fileName="Survey 2023.3d"
      surveyModelId="model-early"
      compare={offer}
      {...props}
    />,
  );
  await waitFor(() => expect(viewers.length).toBe(1));
  viewers[0].survey = tree(earlySurveys);
  viewers[0].arrive();
  await waitFor(() => expect(screen.queryByTestId('caveview-loading')).not.toBeInTheDocument());
  return view;
}

/** Chooses the other survey and an arrangement from the panel's own menu. */
async function compare(arrangement: 'overlaid' | 'sideBySide') {
  fireEvent.click(screen.getByTestId('caveview-compare-open'));
  fireEvent.click(await screen.findByRole('menuitem', { name: text(`caveview.compare.${arrangement}`) }));
  const built = viewers.length;
  await waitFor(() => expect(viewers.length).toBe(built + 1));
  return viewers[viewers.length - 1];
}

/** The viewer drawing both surveys, with each where the test puts it, and its model arrived. */
function arriveOverlaid(viewer: FakeViewer, late: CaveViewSectionBounds = here) {
  viewer.survey = tree({ [EARLY]: earlySurveys, [LATE]: lateSurveys });
  viewer.bounds.set(JSON.stringify([EARLY]), here);
  viewer.bounds.set(JSON.stringify([LATE]), late);
  viewer.arrive();
}

function arriveBeside(viewer: FakeViewer) {
  viewer.survey = tree(lateSurveys);
  viewer.arrive();
}

const tick = (side: 'primary' | 'other', path: string) =>
  screen.getByTestId(`caveview-compare-survey-${side}-${path}`) as HTMLInputElement;

const arrangement = (name: 'overlaid' | 'sideBySide') =>
  within(screen.getByTestId('caveview-compare-bar')).getByRole('radio', {
    name: text(`caveview.compare.${name}`),
  });

beforeEach(() => {
  viewers.length = 0;
  toolbars.length = 0;
  resetViewControlsForTests();
  window.CV2 = {
    CaveViewer: FakeViewer,
    CaveViewUI: FakeUi,
    CaveViewToolbar: FakeToolbar,
    SHADING_SURVEY,
  } as unknown as Cv2Namespace;
  vi.stubGlobal('fetch', vi.fn(async () => new Response(new Blob(['survey']))));
});

afterEach(() => {
  cleanup();
  resetViewControlsForTests();
  vi.unstubAllGlobals();
  window.CV2 = undefined;
});

describe('whether a comparison is offered', () => {
  it('is not, on a panel that was given nothing to compare with', async () => {
    await renderPanel({ compare: undefined });

    expect(screen.queryByTestId('caveview-compare-bar')).not.toBeInTheDocument();
    expect(screen.getByTestId('caveview-container').parentElement).not.toHaveClass('caveview-panel-compare');
  });

  it('is, naming each other survey with both arrangements', async () => {
    await renderPanel();

    fireEvent.click(screen.getByTestId('caveview-compare-open'));

    expect(await screen.findByText(LATE)).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: text('caveview.compare.overlaid') })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: text('caveview.compare.sideBySide') })).toBeInTheDocument();
    // Nothing is compared until one of them is chosen: one viewer, and its surface on the screen.
    expect(viewers).toHaveLength(1);
    expect(screen.getByTestId('caveview-container')).toBeVisible();
  });

  it('keeps the panel\'s surface in place when the offer arrives after the model', async () => {
    // The cave's list is a second request and usually lands after the survey has been drawn. The
    // viewer owns the surface element: an offer that replaced it would leave a viewer drawing into
    // an element no longer on the page.
    const view = await renderPanel({ compare: undefined });
    const surface = screen.getByTestId('caveview-container');

    view.rerender(
      <CaveViewPanel
        fileUrl="http://files.local/early"
        fileName="Survey 2023.3d"
        surveyModelId="model-early"
        compare={offer}
      />,
    );

    expect(screen.getByTestId('caveview-compare-bar')).toBeInTheDocument();
    expect(screen.getByTestId('caveview-container')).toBe(surface);
    expect(viewers).toHaveLength(1);
  });
});

describe('one survey laid over the other', () => {
  it('draws both in one viewer of its own, each under its name and in its colour', async () => {
    await renderPanel();
    const overlay = await compare('overlaid');

    expect(overlay.loaded).toEqual([
      { name: 'Survey 2023.3d', label: EARLY, color: COMPARE_COLOURS.primary },
      { name: 'Survey 2024.lox', label: LATE, color: COMPARE_COLOURS.other },
    ]);
    // Built like every viewer here: its language and its coordinate systems are not its own to choose.
    expect(overlay.config).toMatchObject({ language: 'en', home: viewers[0].config.home });
    // The panel's own viewer is never loaded with a second file.
    expect(viewers[0].loaded).toEqual([{ name: 'Survey 2023.3d' }]);

    arriveOverlaid(overlay);

    // Coloured by survey, which is what makes the two colours the two files.
    expect(overlay.shadingMode).toBe(SHADING_SURVEY);
    const legend = await screen.findByTestId('caveview-compare-legend');
    expect(within(legend).getByText(EARLY)).toBeInTheDocument();
    expect(within(legend).getByText(LATE)).toBeInTheDocument();
    // The panel's own surface is out of sight, kept for the way back; the one drawing both is not.
    const own = screen.getByTestId('caveview-container');
    expect(own).not.toBeVisible();
    // Hidden, never taken out of the layout: its viewer sizes itself to it at every resize of the
    // window, and from an element with no size it would come back showing nothing.
    expect(own.style.display).toBe('');
    expect(own.style.visibility).toBe('hidden');
    expect(screen.getByTestId('caveview-compare-overlay')).toBeVisible();
    expect(viewers[0].disposed).toBe(false);
  });

  it('shows nothing of the two until they are known to lie together', async () => {
    await renderPanel();
    const overlay = await compare('overlaid');

    // Still being read: whether the two coincide is not known, and no legend claims they do.
    expect(screen.getByTestId('caveview-compare-overlay')).not.toBeVisible();
    expect(screen.queryByTestId('caveview-compare-legend')).not.toBeInTheDocument();

    arriveOverlaid(overlay);

    await waitFor(() => expect(screen.getByTestId('caveview-compare-overlay')).toBeVisible());
  });

  it('refuses two surveys that are not in the same coordinates, and offers them side by side', async () => {
    await renderPanel();
    const overlay = await compare('overlaid');

    arriveOverlaid(overlay, elsewhere);

    // Said in plain words, in place of the picture.
    const refusal = await screen.findByTestId('caveview-compare-apart');
    expect(within(refusal).getByText(text('caveview.compare.apart.title'))).toBeInTheDocument();
    expect(screen.queryByTestId('caveview-compare-overlay')).not.toBeInTheDocument();
    expect(screen.queryByTestId('caveview-compare-legend')).not.toBeInTheDocument();
    expect(screen.queryByTestId('caveview-compare-lists')).not.toBeInTheDocument();
    // The viewer that drew them apart is let go of, not left drawing behind the message.
    expect(overlay.disposed).toBe(true);

    fireEvent.click(screen.getByTestId('caveview-compare-apart-side-by-side'));

    await waitFor(() => expect(viewers).toHaveLength(3));
    expect(viewers[2].loaded).toEqual([{ name: 'Survey 2024.lox' }]);
    expect(screen.queryByTestId('caveview-compare-apart')).not.toBeInTheDocument();
    expect(screen.getByTestId('caveview-container')).toBeVisible();
  });
});

describe('the two surveys side by side', () => {
  it('gives the other survey a viewer of its own beside the panel\'s, each under its name', async () => {
    await renderPanel();
    const other = await compare('sideBySide');

    // Alone and under no label: each viewer frames its own survey, whatever its coordinates.
    expect(other.loaded).toEqual([{ name: 'Survey 2024.lox' }]);
    arriveBeside(other);

    expect(screen.getByTestId('caveview-compare-name-primary')).toHaveTextContent(EARLY);
    expect(screen.getByTestId('caveview-compare-name-other')).toHaveTextContent(LATE);
    expect(screen.getByTestId('caveview-container')).toBeVisible();
    expect(screen.getByTestId('caveview-compare-other')).toBeVisible();
    expect(other.shadingMode).toBeUndefined();
    // No colours stand for the surveys here, so no key claims any do.
    expect(screen.queryByTestId('caveview-compare-legend')).not.toBeInTheDocument();
  });

  it('moves each viewer as the other is moved, for as long as the views are linked', async () => {
    await renderPanel();
    const [first] = viewers;
    const other = await compare('sideBySide');
    first.viewpoint = { ...first.viewpoint, azimuth: 0.4, zoom: 2 };
    arriveBeside(other);

    // Linked from the start: the second model is brought to where the reader has the first.
    expect(screen.getByTestId('caveview-compare-link')).toBeChecked();
    await waitFor(() => expect(other.setViewpoint).toHaveBeenLastCalledWith(first.viewpoint));
    expect(first.setViewpoint).not.toHaveBeenCalled();

    first.viewpoint = { ...first.viewpoint, azimuth: 1.1, polar: 0.9 };
    act(() => first.emit('viewpoint'));
    expect(other.setViewpoint).toHaveBeenLastCalledWith(first.viewpoint);

    other.viewpoint = { ...other.viewpoint, zoom: 3.5 };
    act(() => other.emit('viewpoint'));
    expect(first.setViewpoint).toHaveBeenLastCalledWith(other.viewpoint);

    // Unlinked, each is turned on its own.
    fireEvent.click(screen.getByTestId('caveview-compare-link'));
    const before = [first.setViewpoint.mock.calls.length, other.setViewpoint.mock.calls.length];
    act(() => first.emit('viewpoint'));
    act(() => other.emit('viewpoint'));
    expect([first.setViewpoint.mock.calls.length, other.setViewpoint.mock.calls.length]).toEqual(before);

    // And linked again, the second is brought back to the first.
    fireEvent.click(screen.getByTestId('caveview-compare-link'));
    await waitFor(() => expect(other.setViewpoint.mock.calls.length).toBe(before[1] + 1));
  });

  it('does not pass on a view from a viewer that has none to give', async () => {
    await renderPanel();
    const [first] = viewers;
    const other = await compare('sideBySide');
    arriveBeside(other);
    await waitFor(() => expect(other.setViewpoint).toHaveBeenCalled());
    const calls = other.setViewpoint.mock.calls.length;

    // A viewer whose surface is not displayed answers with no view at all.
    first.getViewpoint.mockReturnValue(null);
    act(() => first.emit('viewpoint'));

    expect(other.setViewpoint.mock.calls.length).toBe(calls);
  });
});

describe('showing and hiding surveys', () => {
  it('lists each file\'s own parts, and in step hides the same part of both — laid over one another', async () => {
    await renderPanel();
    const overlay = await compare('overlaid');
    arriveOverlaid(overlay);

    const early = await screen.findByTestId('caveview-compare-list-primary');
    const late = screen.getByTestId('caveview-compare-list-other');
    await waitFor(() => expect(within(late).getAllByRole('checkbox')).toHaveLength(3));
    expect(within(early).getAllByRole('checkbox')).toHaveLength(2);
    expect(screen.getByTestId('caveview-compare-sync')).toBeChecked();

    fireEvent.click(tick('primary', 'p8.bens_dig'));

    // Both files are in the one viewer, each under its name.
    await waitFor(() =>
      expect(overlay.getHiddenSections()).toEqual([
        [EARLY, 'p8', 'bens_dig'],
        [LATE, 'p8', 'bens_dig'],
      ]),
    );
    expect(tick('primary', 'p8.bens_dig')).not.toBeChecked();
    expect(tick('other', 'p8.bens_dig')).not.toBeChecked();
    expect(tick('other', 'p8.main')).toBeChecked();
  });

  it('out of step, acts on its own file only — laid over one another', async () => {
    await renderPanel();
    const overlay = await compare('overlaid');
    arriveOverlaid(overlay);
    fireEvent.click(await screen.findByTestId('caveview-compare-survey-primary-p8.bens_dig'));
    await waitFor(() => expect(overlay.hidden.size).toBe(2));

    fireEvent.click(screen.getByTestId('caveview-compare-sync'));
    fireEvent.click(tick('primary', 'p8.bens_dig'));

    await waitFor(() => expect(overlay.getHiddenSections()).toEqual([[LATE, 'p8', 'bens_dig']]));
    expect(tick('primary', 'p8.bens_dig')).toBeChecked();
    expect(tick('other', 'p8.bens_dig')).not.toBeChecked();
  });

  it('in step and out of it behaves the same with a viewer for each', async () => {
    await renderPanel();
    const [first] = viewers;
    const other = await compare('sideBySide');
    arriveBeside(other);
    await waitFor(() => expect(within(screen.getByTestId('caveview-compare-list-other')).getAllByRole('checkbox')).toHaveLength(3));

    fireEvent.click(tick('other', 'p8.main'));

    // Each survey in its own viewer, by its own paths.
    await waitFor(() => expect(first.getHiddenSections()).toEqual([['p8', 'main']]));
    expect(other.getHiddenSections()).toEqual([['p8', 'main']]);
    expect(tick('primary', 'p8.main')).not.toBeChecked();

    fireEvent.click(screen.getByTestId('caveview-compare-sync'));
    fireEvent.click(tick('other', 'p8.main'));

    await waitFor(() => expect(other.getHiddenSections()).toEqual([]));
    expect(first.getHiddenSections()).toEqual([['p8', 'main']]);
    expect(tick('primary', 'p8.main')).not.toBeChecked();
    expect(tick('other', 'p8.main')).toBeChecked();
  });

  it('hides a part only one survey has without touching the other', async () => {
    await renderPanel();
    const [first] = viewers;
    const other = await compare('sideBySide');
    arriveBeside(other);

    fireEvent.click(await screen.findByTestId('caveview-compare-survey-other-p8.new_series'));

    await waitFor(() => expect(other.getHiddenSections()).toEqual([['p8', 'new_series']]));
    expect(first.setSectionVisible).not.toHaveBeenCalled();
    expect(screen.queryByTestId('caveview-compare-survey-primary-p8.new_series')).not.toBeInTheDocument();
  });

  it('shows all of a list again', async () => {
    await renderPanel();
    const [first] = viewers;
    const other = await compare('sideBySide');
    arriveBeside(other);
    fireEvent.click(await screen.findByTestId('caveview-compare-survey-other-p8.main'));
    fireEvent.click(tick('other', 'p8.new_series'));
    await waitFor(() => expect(other.hidden.size).toBe(2));
    expect(first.hidden.size).toBe(1);

    fireEvent.click(screen.getByTestId('caveview-compare-show-all-primary'));

    // In step: what the first survey has is shown in both, and the part only the second has is
    // not the first list's to show.
    await waitFor(() => expect(first.hidden.size).toBe(0));
    expect(other.getHiddenSections()).toEqual([['p8', 'new_series']]);
    expect(screen.getByTestId('caveview-compare-show-all-primary')).toBeDisabled();

    fireEvent.click(screen.getByTestId('caveview-compare-show-all-other'));
    await waitFor(() => expect(other.hidden.size).toBe(0));
  });

  it('keeps what was hidden when the arrangement is changed', async () => {
    await renderPanel();
    const [first] = viewers;
    const overlay = await compare('overlaid');
    arriveOverlaid(overlay);
    fireEvent.click(await screen.findByTestId('caveview-compare-survey-primary-p8.bens_dig'));
    await waitFor(() => expect(overlay.hidden.size).toBe(2));

    fireEvent.click(arrangement('sideBySide'));
    await waitFor(() => expect(viewers).toHaveLength(3));
    arriveBeside(viewers[2]);

    // The same parts, now in two viewers and by each file's own paths.
    await waitFor(() => expect(viewers[2].getHiddenSections()).toEqual([['p8', 'bens_dig']]));
    expect(first.getHiddenSections()).toEqual([['p8', 'bens_dig']]);
    expect(tick('primary', 'p8.bens_dig')).not.toBeChecked();
    expect(tick('other', 'p8.bens_dig')).not.toBeChecked();
  });

  it('can be put away to give the models the room', async () => {
    await renderPanel();
    const overlay = await compare('overlaid');
    arriveOverlaid(overlay);
    await screen.findByTestId('caveview-compare-lists');

    fireEvent.click(screen.getByTestId('caveview-compare-lists-toggle'));

    expect(screen.queryByTestId('caveview-compare-lists')).not.toBeInTheDocument();
    expect(screen.getByTestId('caveview-compare-lists-toggle')).toHaveAttribute('aria-pressed', 'false');
  });
});

describe('changing and ending a comparison', () => {
  it('switches the arrangement while comparing, letting go of the viewer it no longer needs', async () => {
    await renderPanel();
    const overlay = await compare('overlaid');
    arriveOverlaid(overlay);
    await screen.findByTestId('caveview-compare-legend');

    fireEvent.click(arrangement('sideBySide'));

    await waitFor(() => expect(viewers).toHaveLength(3));
    expect(overlay.disposed).toBe(true);
    expect(viewers[2].loaded).toEqual([{ name: 'Survey 2024.lox' }]);
    expect(screen.getByTestId('caveview-container')).toBeVisible();
    expect(screen.queryByTestId('caveview-compare-overlay')).not.toBeInTheDocument();

    fireEvent.click(arrangement('overlaid'));

    await waitFor(() => expect(viewers).toHaveLength(4));
    expect(viewers[2].disposed).toBe(true);
    expect(viewers[3].loaded.map((file) => file.label)).toEqual([EARLY, LATE]);
    expect(screen.getByTestId('caveview-container')).not.toBeVisible();
    // The panel's own viewer outlived every change of arrangement.
    expect(viewers[0].disposed).toBe(false);
  });

  it('stops comparing and returns to the single survey, with nothing left hidden', async () => {
    await renderPanel();
    const [first] = viewers;
    const other = await compare('sideBySide');
    arriveBeside(other);
    fireEvent.click(await screen.findByTestId('caveview-compare-survey-primary-p8.bens_dig'));
    await waitFor(() => expect(first.hidden.size).toBe(1));

    fireEvent.click(screen.getByTestId('caveview-compare-stop'));

    await waitFor(() => expect(screen.getByTestId('caveview-compare-open')).toBeInTheDocument());
    expect(other.disposed).toBe(true);
    expect(screen.queryByTestId('caveview-compare-other')).not.toBeInTheDocument();
    expect(screen.queryByTestId('caveview-compare-lists')).not.toBeInTheDocument();
    expect(screen.getByTestId('caveview-container')).toBeVisible();
    // The list that would show a part again has gone, so nothing is left hidden behind it.
    expect(first.showAllSections).toHaveBeenCalled();
    expect(first.hidden.size).toBe(0);
    expect(first.disposed).toBe(false);
    // And the views are no longer one: moving the panel's viewer reaches nothing.
    act(() => first.emit('viewpoint'));
    expect(first.listeners.get('viewpoint')?.size ?? 0).toBe(0);
  });

  it('starts the next comparison with everything shown', async () => {
    await renderPanel();
    const overlay = await compare('overlaid');
    arriveOverlaid(overlay);
    fireEvent.click(await screen.findByTestId('caveview-compare-survey-primary-p8.bens_dig'));
    await waitFor(() => expect(overlay.hidden.size).toBe(2));
    fireEvent.click(screen.getByTestId('caveview-compare-stop'));
    await waitFor(() => expect(screen.getByTestId('caveview-compare-open')).toBeInTheDocument());

    const again = await compare('overlaid');
    arriveOverlaid(again);

    expect((await screen.findByTestId('caveview-compare-survey-primary-p8.bens_dig')) as HTMLInputElement).toBeChecked();
    expect(again.hidden.size).toBe(0);
  });

  it('ends when the other survey is taken off the cave\'s list', async () => {
    const view = await renderPanel();
    const other = await compare('sideBySide');
    arriveBeside(other);

    // Deleted by somebody else while it was being compared with: the list is re-read every few
    // minutes, and the survey is no longer on it.
    view.rerender(
      <CaveViewPanel
        fileUrl="http://files.local/early"
        fileName="Survey 2023.3d"
        surveyModelId="model-early"
        compare={undefined}
      />,
    );

    await waitFor(() => expect(other.disposed).toBe(true));
    expect(screen.queryByTestId('caveview-compare-bar')).not.toBeInTheDocument();
    expect(screen.getByTestId('caveview-container')).toBeVisible();
  });

  it('is not rebuilt because the surveys\' addresses were re-issued', async () => {
    const view = await renderPanel();
    const other = await compare('sideBySide');
    arriveBeside(other);

    // The same two surveys under fresh signed addresses, as the list brings them every few minutes.
    view.rerender(
      <CaveViewPanel
        fileUrl="http://files.local/early"
        fileName="Survey 2023.3d"
        surveyModelId="model-early"
        compare={{
          current: { ...offer.current, fileUrl: 'http://files.local/early?fresh' },
          others: [{ ...offer.others[0], fileUrl: 'http://files.local/late?fresh' }],
        }}
      />,
    );

    expect(viewers).toHaveLength(2);
    expect(other.disposed).toBe(false);
  });
});

describe('the viewer\'s own controls while comparing', () => {
  it('gives each viewer of a comparison the panel\'s controls, without the ones a comparison cannot keep', async () => {
    await renderPanel({ toolbar: { buttons: ['stations', 'viewPlan', 'shadingMode', 'fullscreen'] } });
    const [first] = viewers;
    await waitFor(() => expect(toolbars.some((bar) => bar.viewer === first)).toBe(true));
    expect(toolbars.at(-1)?.buttons).toEqual(['stations', 'viewPlan', 'shadingMode', 'fullscreen']);

    const overlay = await compare('overlaid');
    arriveOverlaid(overlay);

    // Laid over one another the colours are the two surveys, so the shading is not offered; and
    // fullscreen would leave the names and the list of parts behind the one surface it shows.
    await waitFor(() => expect(toolbars.some((bar) => bar.viewer === overlay)).toBe(true));
    expect(toolbars.find((bar) => bar.viewer === overlay)?.buttons).toEqual(['stations', 'viewPlan']);

    fireEvent.click(arrangement('sideBySide'));
    await waitFor(() => expect(viewers).toHaveLength(3));
    arriveBeside(viewers[2]);

    await waitFor(() => expect(toolbars.some((bar) => bar.viewer === viewers[2])).toBe(true));
    expect(toolbars.find((bar) => bar.viewer === viewers[2])?.buttons).toEqual(['stations', 'viewPlan', 'shadingMode']);
    // The panel's own viewer is half of the comparison now, and is given the same.
    await waitFor(() => expect(toolbars.filter((bar) => bar.viewer === first).at(-1)?.buttons).toEqual(['stations', 'viewPlan', 'shadingMode']));
  });

  it('puts no controls over a comparison on a panel that shows none', async () => {
    await renderPanel();
    const other = await compare('sideBySide');
    arriveBeside(other);
    await screen.findByTestId('caveview-compare-name-other');

    expect(toolbars).toHaveLength(0);
  });
});
