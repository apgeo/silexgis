// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { SelectorScope } from '../../filters/selection.ts';
import ObjectSelector from './ObjectSelector.tsx';

/**
 * The chrome only.
 *
 * What the control decides is proved in `src/filters/selection.test.ts` against values, with
 * nothing mounted. What is left for here is the part that only exists once something is drawn: that
 * a stored value the server will not describe is reported as restricted rather than as an
 * identifier, that choosing a row reports the row and not only its id, and that the request the
 * control makes never asks for a total.
 *
 * The hooks are stood in for rather than the network, which is how the rest of this codebase's
 * component tests are written — and it lets the request itself be asserted on directly.
 */

const queryCalls: unknown[] = [];
const resolveResult = { data: [] as unknown[] };
const queryResult = {
  data: {
    worlds: [{
      world: 'feature',
      hits: [{
        world: 'feature', id: 'f1', title: 'Peștera Urșilor',
        subtitle: 'Cave', symbol: 'cave', placeable: true,
      }],
      total: null,
    }],
    page: 1,
    pageSize: 15,
    counted: false,
  },
  isFetching: false,
};

vi.mock('../../api/hooks.ts', () => ({
  useFilterVocabulary: () => ({
    data: {
      worlds: [
        { world: 'feature', labelKey: 'x', fields: [], sorts: ['created', 'updated', 'title'] },
        { world: 'tripLog', labelKey: 'x', fields: [], sorts: ['created', 'updated'] },
      ],
      limits: {
        maxNodes: 200, maxDepth: 6, maxValuesPerCondition: 200,
        maxWorlds: 12, maxTextValueLength: 200, maxPageSize: 500,
      },
    },
  }),
  useFilterQuery: (body: unknown, enabled: boolean) => {
    queryCalls.push({ body, enabled });
    return enabled ? queryResult : { data: undefined, isFetching: false };
  },
  useFilterResolve: () => resolveResult,
}));

const SCOPES: SelectorScope[] = [
  { id: 'caves', labelKey: 'Caves', world: 'feature', where: { field: 'kind', value: 'cave' } },
  { id: 'trips', labelKey: 'Trips', world: 'tripLog', where: null },
];

function renderControl(props: Partial<React.ComponentProps<typeof ObjectSelector>> = {}) {
  return render(<ObjectSelector scopes={SCOPES} data-testid="picker" {...props} />);
}

function type(text: string) {
  fireEvent.change(screen.getByTestId('picker-input'), { target: { value: text } });
}

/** The last request the control actually asked for. */
function lastAsked() {
  return queryCalls.filter((c) => (c as { enabled: boolean }).enabled).at(-1) as
    { body: { count: boolean; pageSize: number }; enabled: boolean } | undefined;
}

beforeEach(() => {
  cleanup();
  queryCalls.length = 0;
  resolveResult.data = [];
});

describe('the object selector', () => {
  it('asks nothing until enough has been typed', async () => {
    // A picker that queried on an empty box would hand back whichever rows sort first, which is
    // how one becomes a way to enumerate a domain.
    renderControl();

    await waitFor(() => expect(queryCalls.length).toBeGreaterThan(0));
    expect(lastAsked()).toBeUndefined();
  });

  it('lists what the server found once it has', async () => {
    renderControl();
    type('urs');

    expect(await screen.findByText('Peștera Urșilor')).toBeInTheDocument();
  });

  it('reports a choice it cannot describe without printing the identifier', async () => {
    // The whole reason resolve returns an unreadable row as missing rather than refused. A control
    // that fell back to showing the value would put a raw uuid where a name belongs — and would
    // confirm to whoever read it that the row exists.
    const secret = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';
    renderControl({ value: secret });

    expect(await screen.findByTestId('picker-restricted')).toBeInTheDocument();
    expect(screen.queryByText(secret)).not.toBeInTheDocument();
  });

  it('reports the row that was chosen, not only its id', async () => {
    const onChange = vi.fn();
    const onPick = vi.fn();
    renderControl({ onChange, onPick });

    type('urs');
    fireEvent.click(await screen.findByText('Peștera Urșilor'));

    expect(onChange).toHaveBeenCalledWith('f1');
    expect(onPick).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'f1', title: 'Peștera Urșilor' }));
  });

  it('hands back a list when it takes several', async () => {
    const onChange = vi.fn();
    renderControl({ multiple: true, onChange });

    type('urs');
    fireEvent.click(await screen.findByText('Peștera Urșilor'));

    expect(onChange).toHaveBeenCalledWith(['f1']);
  });

  it('never asks for a total, and asks for fifteen rows', async () => {
    // A total beside a search box is a population statistic for a predicate somebody just typed,
    // and it costs a whole pass over the composed set on every keystroke.
    renderControl();
    type('urs');

    await waitFor(() => expect(lastAsked()).toBeDefined());
    expect(lastAsked()!.body.count).toBe(false);
    expect(lastAsked()!.body.pageSize).toBe(15);
  });

  it('shows the scope buttons when there is more than one to choose', () => {
    renderControl();

    expect(screen.getByTestId('picker-scope-caves')).toBeInTheDocument();
    expect(screen.getByTestId('picker-scope-trips')).toBeInTheDocument();
  });

  it('offers only the sorts every world in play can answer', () => {
    // Trips cannot sort by title; offering it would leave them in an order nobody chose with
    // nothing on screen to say so.
    renderControl();

    expect(screen.getByTestId('picker-sort-created')).toBeInTheDocument();
    expect(screen.getByTestId('picker-sort-updated')).toBeInTheDocument();
    expect(screen.queryByTestId('picker-sort-title')).not.toBeInTheDocument();
  });
});
