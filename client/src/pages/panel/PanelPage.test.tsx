// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { SurveyModelInfo } from '../../api/hooks.ts';
import type { SurveyCompareOffer } from '../../caveview/surveyCompare.ts';

/**
 * The survey-model pop-out, and which model it decides to show.
 *
 * There are two behaviours here and they are opposites, which is why both are pinned. Opened bare,
 * the window follows whatever is selected in the main window and shows the first model of that
 * cave the viewer can read — a guess, and the right one, because nobody told it which. Opened on a
 * model, it shows THAT model and stops listening, because a window opened to look at one thing
 * must go on showing it while the reader clicks about elsewhere. Losing either half is silent: the
 * window still shows a cave model, just not the one that was asked for.
 */

let listed: SurveyModelInfo[] = [];
let pinned: SurveyModelInfo | undefined;
let askedForId: string | undefined;
let askedForCave: string | undefined;
let busListener: ((event: unknown) => void) | undefined;
let compareAnswer: SurveyCompareOffer | undefined;
let comparedModel: string | undefined;
// A read that did not land, per hook; undefined = the read answered.
let failing: { models?: unknown; pinned?: unknown; caves?: unknown } = {};
const refetch = vi.fn();
const failed = (error: unknown) => ({ data: undefined, isPending: false, isError: true, error, refetch });

vi.mock('../../api/hooks.ts', () => ({
  surveyModelReadableByViewer: (m: { format: string }) => m.format === 'lox' || m.format === 'survex3d',
  useCave: () => ({ data: { id: 'cave-1', name: 'Test cave' } }),
  useCaves: () => (failing.caves ? failed(failing.caves) : { data: { items: [], totalItems: 0 } }),
  useEntrances: () => ({ data: [] }),
  useSurveyModel: (id: string | undefined) => {
    askedForId = id;
    if (id && failing.pinned) return failed(failing.pinned);
    return { data: id ? pinned : undefined, isPending: false };
  },
  useSurveyModels: (caveId: string | undefined) => {
    askedForCave = caveId;
    if (caveId && failing.models) return failed(failing.models);
    return { data: caveId ? listed : undefined };
  },
}));

// What the shown model can be compared with is read off the cave's list by a hook of its own,
// faked here so that the list this file watches being asked for is only ever the page's own ask:
// which model to show. The two are different questions, and only the second must never be
// answered from a list in a window opened on one model.
vi.mock('../../caveview/useCompareOffer.ts', () => ({
  useCompareOffer: (shown: { id: string } | null | undefined) => {
    comparedModel = shown?.id;
    return compareAnswer;
  },
}));

// The viewer itself is a three.js bundle with a drawing context; what it is handed is the point.
vi.mock('../../components/caveview/CaveViewPanel.tsx', () => ({
  default: ({
    fileName,
    surveyModelId,
    compare,
  }: {
    fileName: string;
    surveyModelId?: string;
    compare?: SurveyCompareOffer;
  }) => (
    <div
      data-testid="viewer"
      data-file={fileName}
      data-model={surveyModelId}
      data-compare-with={compare?.others.map((other) => other.id).join(',') ?? ''}
    />
  ),
}));
vi.mock('../../components/map/SelectionPanel.tsx', () => ({ default: () => null }));
vi.mock('../../components/scene3d/Scene3DView.tsx', () => ({ default: () => null }));
vi.mock('../../textlink/AnnotatedTextPanel.tsx', () => ({ default: () => null }));
vi.mock('../../workspace/workspaceBus.ts', () => ({
  publish: vi.fn(),
  subscribe: (listener: (event: unknown) => void) => {
    busListener = listener;
    return () => {
      busListener = undefined;
    };
  },
}));

const { default: PanelPage } = await import('./PanelPage.tsx');

function model(id: string, name: string, format: SurveyModelInfo['format'] = 'lox'): SurveyModelInfo {
  return { id, name, format, caveId: 'cave-1', modelUrl: `https://x/${id}` } as unknown as SurveyModelInfo;
}

function show(search = '') {
  return render(
    <MemoryRouter initialEntries={[`/panel/viewer3d${search}`]}>
      <Routes>
        <Route path="/panel/:panelId" element={<PanelPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  listed = [];
  pinned = undefined;
  askedForId = undefined;
  askedForCave = undefined;
  busListener = undefined;
  compareAnswer = undefined;
  comparedModel = undefined;
  failing = {};
  refetch.mockReset();
});

afterEach(cleanup);

describe('the survey-model pop-out', () => {
  it('shows the model it was opened on, and never asks the cave for a list', async () => {
    pinned = model('chosen', 'Chosen plot');
    show('?model=chosen');

    const viewer = await screen.findByTestId('viewer');
    expect(viewer).toHaveAttribute('data-model', 'chosen');
    expect(askedForId).toBe('chosen');
    // Asserted rather than implied: listing the cave's models and picking through them is the
    // behaviour a chosen model exists to replace, and a window that did both would look correct
    // while quietly making the same guess.
    expect(askedForCave).toBeUndefined();
  });

  it('keeps showing it when the main window selects a different cave', async () => {
    pinned = model('chosen', 'Chosen plot');
    show('?model=chosen');
    await screen.findByTestId('viewer');

    // A pinned window does not subscribe at all, which is stronger than ignoring what arrives.
    expect(busListener).toBeUndefined();
    expect(screen.getByTestId('viewer')).toHaveAttribute('data-model', 'chosen');
  });

  it('follows the selection when it was opened without a model', async () => {
    listed = [model('mesh', 'Walls', 'stl'), model('plot', 'Grind plot')];
    show();

    expect(busListener).toBeDefined();
    busListener?.({ kind: 'selection', selection: { kind: 'cave', caveId: 'cave-1' } });

    // The first model it can read, skipping the mesh it cannot parse.
    const viewer = await screen.findByTestId('viewer');
    expect(viewer).toHaveAttribute('data-model', 'plot');
    expect(askedForCave).toBe('cave-1');
  });

  it('names the file so the viewer picks the right parser', async () => {
    pinned = model('chosen', 'Grind', 'survex3d');
    show('?model=chosen');

    await waitFor(() =>
      expect(screen.getByTestId('viewer')).toHaveAttribute('data-file', 'Grind.3d'),
    );
  });

  it('offers the shown model a comparison with its cave\'s other surveys, pinned or followed', async () => {
    const offer = (id: string): SurveyCompareOffer => ({
      current: { id, name: 'Shown', fileUrl: `https://x/${id}`, fileName: 'Shown.lox' },
      others: [{ id: 'resurvey', name: 'Resurvey', fileUrl: 'https://x/resurvey', fileName: 'Resurvey.lox' }],
    });

    // Opened on one model: the comparison is asked about that model, and reaches the viewer.
    pinned = model('chosen', 'Chosen plot');
    compareAnswer = offer('chosen');
    const first = show('?model=chosen');

    expect(await screen.findByTestId('viewer')).toHaveAttribute('data-compare-with', 'resurvey');
    expect(comparedModel).toBe('chosen');
    first.unmount();

    // Following the selection: asked about whichever model the window turned out to show.
    listed = [model('plot', 'Grind plot')];
    compareAnswer = offer('plot');
    show();
    busListener?.({ kind: 'selection', selection: { kind: 'cave', caveId: 'cave-1' } });

    expect(await screen.findByTestId('viewer')).toHaveAttribute('data-compare-with', 'resurvey');
    expect(comparedModel).toBe('plot');
  });

  it('says the model is gone rather than telling the reader to pick a cave', async () => {
    // A model whose cave's location is withheld answers 404, which the query layer holds as a
    // failed read carrying the refusal — the way the hook really answers it. Falling through to
    // the follow-the-selection message, or to the general refusal sentence with a Retry, would
    // tell somebody who was sent a link something false: that they did something wrong, or that
    // asking again could help.
    failing.pinned = new ApiError(404, 'survey_model.not_found');
    show('?model=withheld');

    const alert = await screen.findByTestId('panel-viewer3d-load-failed');
    expect(alert).toHaveAttribute('data-refused', 'true');
    expect(alert.textContent).toMatch(/no longer available, or you may not see it/i);
    expect(within(alert).queryByRole('button', { name: 'Retry' })).toBeNull();
    expect(screen.queryByTestId('viewer')).toBeNull();
  });
});

describe('a read of the pop-out that did not land', () => {
  /**
   * A read that failed is not an answer. "Select a cave" or "no longer available" after a dropped
   * connection tells the reader something false; the failure is named with a way to ask again.
   */
  it('says the model could not be read when the one it was opened on failed to load, not that it is gone', async () => {
    failing.pinned = new Error('network');
    show('?model=plot');

    const alert = await screen.findByTestId('panel-viewer3d-load-failed');
    expect(alert.textContent).toContain('could not be read just now');
    expect(screen.queryByText(/no longer available/i)).toBeNull();
    fireEvent.click(within(alert).getByRole('button', { name: 'Retry' }));
    expect(refetch).toHaveBeenCalledTimes(1);
  });

  it('says the selected cave\'s models could not be read instead of telling the reader to select a cave', async () => {
    failing.models = new Error('network');
    show();
    await waitFor(() => expect(busListener).toBeDefined());
    act(() => busListener!({ kind: 'selection', selection: { kind: 'cave', caveId: 'cave-1' } }));

    const alert = await screen.findByTestId('panel-viewer3d-load-failed');
    expect(alert.textContent).toContain('could not be read just now');
    expect(screen.queryByText(/Select a cave with a 3D model/i)).toBeNull();
  });

  it('names a failed read of the cave registry instead of showing an empty table', async () => {
    failing.caves = new Error('network');
    render(
      <MemoryRouter initialEntries={['/panel/registry']}>
        <Routes>
          <Route path="/panel/:panelId" element={<PanelPage />} />
        </Routes>
      </MemoryRouter>,
    );
    const alert = await screen.findByTestId('panel-registry-load-failed');
    expect(alert.textContent).toContain('The cave list could not be read just now');
    expect(screen.queryByText(/No data/)).toBeNull();
    fireEvent.click(within(alert).getByRole('button', { name: 'Retry' }));
    expect(refetch).toHaveBeenCalledTimes(1);
  });
});
