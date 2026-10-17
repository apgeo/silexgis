// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { SpeleolocImportOptions } from '../../api/hooks.ts';

const { queryCalls, resolveCalls, modelsFor } = vi.hoisted(() => ({
  queryCalls: [] as { body: unknown; enabled: boolean }[],
  resolveCalls: [] as { world: string; ids: readonly string[] }[],
  modelsFor: vi.fn(),
}));

interface AskedBody {
  document: { scope: { world: string; where: unknown }[]; sort: string; descending: boolean };
}

const hit = (world: string, id: string, title: string, subtitle: string | null) =>
  ({ world, id, title, subtitle, symbol: null, placeable: false });

const TRIPS = [hit('tripLog', 'trip-1', 'Tura 1', '2026-09-02'), hit('tripLog', 'trip-2', 'Tura 2', '2026-08-30')];
const CAVES = [hit('feature', 'cave-1', 'Peștera 1', 'Cave'), hit('feature', 'cave-2', 'Peștera 2', 'Cave')];

vi.mock('../../api/hooks.ts', () => ({
  useCavingGroups: () => ({ data: [], isLoading: false }),
  // A stored model names its cave; nothing else is asked of the model here.
  useSurveyModel: (id?: string) => ({ data: id === 'model-1' ? { id, caveId: 'cave-2' } : undefined }),
  useSurveyModels: (caveId?: string) => {
    modelsFor(caveId);
    return { data: caveId ? [{ id: 'model-1', name: 'Model 1' }] : [], isFetching: false };
  },
  useFilterVocabulary: () => ({
    data: {
      worlds: [
        { world: 'tripLog', labelKey: 'x', fields: [], sorts: ['occurred', 'updated', 'title'] },
        { world: 'feature', labelKey: 'x', fields: [], sorts: ['updated', 'title'] },
      ],
    },
  }),
  useFilterQuery: (body: AskedBody, enabled: boolean) => {
    queryCalls.push({ body, enabled });
    if (!enabled) {
      return { data: undefined, isFetching: false };
    }
    const world = body.document.scope[0]?.world;
    return {
      data: {
        worlds: [{ world, hits: world === 'tripLog' ? TRIPS : CAVES, total: null }],
        page: 1, pageSize: 10, counted: false,
      },
      isFetching: false,
    };
  },
  useFilterResolve: (world: string, ids: readonly string[]) => {
    resolveCalls.push({ world, ids });
    const known = [...TRIPS, ...CAVES].filter((h) => h.world === world && ids.includes(h.id));
    return { data: known };
  },
}));

const { default: SpeleolocImportOptionsPanel } = await import('./SpeleolocImportOptionsPanel.tsx');

function renderPanel(options: SpeleolocImportOptions) {
  const onChange = vi.fn();
  render(
    <SpeleolocImportOptionsPanel options={options} onChange={onChange} recordings={[]} recordingsLoading={false} />,
  );
  return onChange;
}

/** The last document a world was asked with, enabled. */
function lastAsked(world: string) {
  const call = [...queryCalls].reverse().find(
    (c) => c.enabled && (c.body as AskedBody).document.scope[0]?.world === world,
  );
  return (call?.body as AskedBody | undefined)?.document;
}

afterEach(cleanup);
beforeEach(() => {
  queryCalls.length = 0;
  resolveCalls.length = 0;
  modelsFor.mockClear();
});

describe('SpeleolocImportOptionsPanel — the trip', () => {
  it('lists the most recent trips on opening, each with its date, and stores the one picked', async () => {
    const onChange = renderPanel({});

    fireEvent.click(screen.getByTestId('speleoloc-import-trip'));
    // Nothing typed and still asked: the trip a recording belongs to is nearly always one just
    // walked, so the newest are what the box opens on.
    const asked = lastAsked('tripLog');
    expect(asked).toBeDefined();
    expect(asked).toMatchObject({ sort: 'occurred', descending: true });
    expect(screen.getByText('2026-09-02')).toBeTruthy();

    fireEvent.click(await screen.findByRole('button', { name: /Tura 2/ }, { timeout: 3000 }));
    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ tripLogId: 'trip-2' }));
  });

  it('names a stored destination through resolve rather than as an identifier', () => {
    renderPanel({ tripLogId: 'trip-1' });
    expect(resolveCalls.some((c) => c.world === 'tripLog' && c.ids.includes('trip-1'))).toBe(true);
    expect(screen.getByTestId('speleoloc-import-trip').textContent).toContain('Tura 1');
    expect(screen.getByTestId('speleoloc-import-trip').textContent).not.toContain('trip-1');
  });

  it('can withdraw the destination', () => {
    const onChange = renderPanel({ tripLogId: 'trip-1' });
    fireEvent.click(screen.getByTestId('speleoloc-import-trip'));
    fireEvent.click(screen.getByTestId('speleoloc-import-trip-clear'));
    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ tripLogId: null }));
  });
});

describe('SpeleolocImportOptionsPanel — the cave', () => {
  it('searches caves only, and does not list anything on an empty box', () => {
    renderPanel({});
    fireEvent.click(screen.getByTestId('speleoloc-import-cave'));
    expect(lastAsked('feature')).toBeUndefined();

    fireEvent.change(screen.getByTestId('speleoloc-import-cave-input'), { target: { value: 'pe' } });
    // Debounced, so wait for the query to be asked rather than for a timer.
    return vi.waitFor(() => {
      const asked = lastAsked('feature');
      expect(asked).toBeDefined();
      expect(JSON.stringify(asked)).toContain('"kind"');
      expect(JSON.stringify(asked)).toContain('"cave"');
    }, { timeout: 3000 });
  });

  it('clears the model when another cave is picked, and lists that cave’s models', async () => {
    const onChange = renderPanel({ surveyModelId: 'model-1' });
    // The resumed review's cave comes from its model, and is named without being searched for.
    expect(screen.getByTestId('speleoloc-import-cave').textContent).toContain('Peștera 2');
    expect(modelsFor).toHaveBeenLastCalledWith('cave-2');

    fireEvent.click(screen.getByTestId('speleoloc-import-cave'));
    fireEvent.change(screen.getByTestId('speleoloc-import-cave-input'), { target: { value: 'pe' } });
    // Debounced; a loaded machine can take longer than the default second to answer.
    fireEvent.click(await screen.findByRole('button', { name: /Peștera 1/ }, { timeout: 3000 }));

    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ surveyModelId: null }));
    expect(modelsFor).toHaveBeenLastCalledWith('cave-1');
  });
});
