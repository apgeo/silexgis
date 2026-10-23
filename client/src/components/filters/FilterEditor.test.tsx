// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import en from '../../i18n/locales/en.json';
import ro from '../../i18n/locales/ro.json';
import { ApiError } from '../../api/client.ts';
import type { FilterDocument, FilterField, FilterNode, FilterOp, FilterValue } from '../../filters/types.ts';
import FilterEditor from './FilterEditor.tsx';
import { CLOSED_SETS } from './closedSets.ts';

/**
 * The editor's chrome, with the server stood in for.
 *
 * What the tree operations decide — paths, depth, what is pruned, where a type is fixed — is proved
 * against values in `src/filters/editorTree.test.ts`. What is left here is what only exists once it
 * is drawn: that a document comes back out as it went in, that an edit lands where it was made, that
 * the depth limit is a control that cannot be pressed, that a stored id the reader cannot resolve is
 * never printed, and that the count and a refusal are shown where they belong.
 */

const ALL_OPS: FilterOp[] = [
  'equals', 'in', 'contains', 'startsWith', 'lessThan', 'lessOrEqual', 'greaterThan',
  'greaterOrEqual', 'between', 'isEmpty', 'isNotEmpty', 'within', 'inside',
];

const field = (key: string, kind: FilterField['kind'], ops: FilterOp[], extra: Partial<FilterField> = {}): FilterField => ({
  key, labelKey: `filters.fields.${key}`, kind, ops, options: null, label: null, requires: null, ...extra,
});

const FIELDS: FilterField[] = [
  field('name', 'text', ['equals', 'contains', 'startsWith', 'isEmpty', 'isNotEmpty']),
  field('surveyedLength', 'number', ['equals', 'lessThan', 'lessOrEqual', 'greaterThan', 'greaterOrEqual', 'between', 'isEmpty', 'isNotEmpty']),
  field('caveTypeId', 'id', ['equals', 'in', 'isEmpty', 'isNotEmpty'], { options: 'caveTypes' }),
  field('typeId', 'id', ['equals', 'in', 'isEmpty', 'isNotEmpty'], { options: 'featureTypes' }),
  field('surveyedAt', 'instant', ['lessThan', 'lessOrEqual', 'greaterThan', 'greaterOrEqual', 'between', 'isEmpty', 'isNotEmpty']),
  field('property:sinkhole:depth_m', 'number', ['lessThan', 'greaterOrEqual'], {
    labelKey: 'filters.fields.property', label: 'Sinkhole depth (m)', requires: { field: 'typeId', value: '7' },
  }),
  field('position', 'spatial', ['within', 'inside']),
];

let sorts: string[] = ['updated'];

// The object pickers are the ordinary selector, proved on its own; a box handing back an id is
// enough to prove what the editor does with one.
vi.mock('../selector/ObjectSelector.tsx', () => ({
  default: (props: { value: string | null; onChange: (id: string | null) => void; 'data-testid': string }) => (
    <input
      data-testid={props['data-testid']}
      value={props.value ?? ''}
      onChange={(e) => props.onChange(e.target.value === '' ? null : e.target.value)}
    />
  ),
}));

let limits = { maxNodes: 200, maxDepth: 6, maxValuesPerCondition: 200, maxWorlds: 12, maxTextValueLength: 200, maxPageSize: 500, maxRadiusMeters: 200_000 };
let queryAnswer: { data?: unknown; error?: unknown } = {};
const queriesAsked: { body: { count: boolean; pageSize: number }; enabled: boolean }[] = [];

vi.mock('../../api/hooks.ts', () => ({
  useFilterVocabulary: () => ({
    data: { worlds: [{ world: 'feature', labelKey: 'x', fields: FIELDS, sorts }], limits },
  }),
  useFilterQuery: (body: { count: boolean; pageSize: number }, enabled: boolean) => {
    queriesAsked.push({ body, enabled });
    return queryAnswer;
  },
  useCaveTypes: () => ({ data: [{ id: 3, name: 'Karst cave' }], isLoading: false }),
  useFeatureTypes: () => ({ data: [{ id: 7, name: 'Sinkhole' }], isLoading: false }),
  useRockTypes: () => ({ data: [], isLoading: false }),
  useDocumentTypes: () => ({ data: [], isLoading: false }),
  useTripTypes: () => ({ data: [], isLoading: false }),
  useTags: () => ({ data: [], isLoading: false }),
  useTagsBySlug: () => ({ data: [], isLoading: false }),
  useCavingGroups: () => ({ data: [], isLoading: false }),
  useUserSearch: () => ({ data: [], isFetching: false }),
  useMember: () => ({ data: undefined, isError: true }),
  useSavedFilters: () => ({ data: [], isLoading: false }),
  documentOf: () => null,
}));

const c = (fieldKey: string, op: FilterOp, values: FilterValue[]): FilterNode =>
  ({ node: 'condition', field: fieldKey, op, values });

/** The text box inside a control, wherever the component library put the test id. */
function inputOf(testId: string): HTMLInputElement {
  const el = screen.getByTestId(testId);
  return (el.tagName === 'INPUT' ? el : el.querySelector('input')) as HTMLInputElement;
}

function doc(where: FilterNode | null): FilterDocument {
  return { version: 1, scope: [{ world: 'feature', where }], sort: 'updated', descending: true };
}

function show(where: FilterNode | null) {
  const onApply = vi.fn();
  render(<FilterEditor world="feature" document={doc(where)} onApply={onApply} />);
  return onApply;
}

beforeEach(async () => {
  cleanup();
  await i18n.changeLanguage('en');
  limits = { ...limits, maxDepth: 6 };
  queryAnswer = {};
  queriesAsked.length = 0;
  sorts = ['updated'];
});

const REGISTRY: FilterNode = {
  node: 'allOf',
  of: [
    c('name', 'contains', [{ type: 'text', value: 'urs' }]),
    c('surveyedLength', 'greaterOrEqual', [{ type: 'number', value: 300 }]),
  ],
};

describe('the condition editor', () => {
  it('gives back the document it was opened on when nothing is changed', () => {
    const onApply = show(REGISTRY);

    expect(screen.getByTestId('filter-editor-row-0')).toBeInTheDocument();
    expect(screen.getByTestId('filter-editor-row-1')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('filter-editor-apply'));

    expect(onApply).toHaveBeenCalledWith(doc(REGISTRY));
  });

  it('gives a single condition back unwrapped, as it came in', () => {
    const single = c('name', 'contains', [{ type: 'text', value: 'urs' }]);
    const onApply = show(single);

    fireEvent.click(screen.getByTestId('filter-editor-apply'));

    expect(onApply).toHaveBeenCalledWith(doc(single));
  });

  it('writes an edit into the row it was made on, and leaves an unfinished row out', () => {
    const onApply = show(REGISTRY);

    fireEvent.change(inputOf('filter-editor-value-1'), { target: { value: '500' } });
    fireEvent.click(screen.getByTestId('filter-editor-add-row-root'));
    expect(screen.getByTestId('filter-editor-row-2')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('filter-editor-not-0'));
    fireEvent.click(screen.getByTestId('filter-editor-apply'));

    expect(onApply).toHaveBeenCalledWith(doc({
      node: 'allOf',
      of: [
        { node: 'not', of: c('name', 'contains', [{ type: 'text', value: 'urs' }]) },
        c('surveyedLength', 'greaterOrEqual', [{ type: 'number', value: 500 }]),
      ],
    }));
  });

  it('nests groups to the depth limit and offers no deeper one', () => {
    limits = { ...limits, maxDepth: 3 };
    show(null);

    // Root group (1) → nested group (2) → its row (3): the limit.
    expect(screen.getByTestId('filter-editor-add-group-root')).toBeEnabled();
    fireEvent.click(screen.getByTestId('filter-editor-add-group-root'));
    expect(screen.getByTestId('filter-editor-group-0')).toBeInTheDocument();
    expect(screen.getByTestId('filter-editor-add-group-0')).toBeDisabled();
    // Negating the nested row would be a fourth level as well.
    expect(inputOf('filter-editor-not-0.0')).toBeDisabled();
  });

  it('shows a stored id the reader cannot resolve as not available, never as the id', () => {
    show({ node: 'allOf', of: [c('caveTypeId', 'equals', [{ type: 'id', value: '99' }])] });

    const value = screen.getByTestId('filter-editor-value-0');
    expect(value).toHaveTextContent('not available to you');
    expect(value).not.toHaveTextContent('99');
  });

  it('shows how many rows the filter would find before it is applied', () => {
    queryAnswer = { data: { counted: true, worlds: [{ world: 'feature', hits: [], total: 12 }] } };
    show(REGISTRY);

    expect(screen.getByTestId('filter-editor-count')).toHaveTextContent('Matches: 12');
    const asked = queriesAsked.filter((q) => q.enabled).at(-1)!;
    expect(asked.body.count).toBe(true);
    expect(asked.body.pageSize).toBe(1);
  });

  it('counts with the page’s fixed condition and never hands that condition back', () => {
    const fixed = c('kind', 'equals', [{ type: 'id', value: 'cave' }]);
    const where = c('surveyedLength', 'greaterOrEqual', [{ type: 'number', value: 300 }]);
    const onApply = vi.fn();
    render(<FilterEditor world="feature" document={doc({ node: 'allOf', of: [where] })} fixed={fixed} onApply={onApply} />);

    const asked = queriesAsked.filter((q) => q.enabled).at(-1)!.body as unknown as { document: FilterDocument };
    expect(asked.document.scope[0]!.where).toEqual({ node: 'allOf', of: [fixed, where] });
    expect(screen.queryByText('filters.fields.kind')).toBeNull();

    fireEvent.click(screen.getByTestId('filter-editor-apply'));
    expect((onApply.mock.calls[0]![0] as FilterDocument).scope[0]!.where).toEqual({ node: 'allOf', of: [where] });
  });

  /** A refusal as the server sends it: a code per problem and the place in the sent document. */
  function refusal(problems: { code: string; path: string; field?: string; limit?: number }[]): ApiError {
    return new ApiError(400, 'filter.invalid', 'An English sentence the reader is never shown.', {
      code: 'filter.invalid',
      problems,
    });
  }

  it('puts each problem on the row its path points at, the rest above the rows, and will not apply', () => {
    queryAnswer = {
      error: refusal([
        { code: 'filter.number_out_of_range', path: '/scope/0/where/of/1', field: 'surveyedLength' },
        { code: 'filter.too_many_nodes', path: '/scope/0/where', limit: 200 },
      ]),
    };
    show(REGISTRY);

    expect(screen.getByTestId('filter-editor-row-error-1')).toHaveTextContent(en.filters.editor.problems.number_out_of_range);
    expect(screen.queryByTestId('filter-editor-row-error-0')).not.toBeInTheDocument();
    const whole = screen.getByTestId('filter-editor-refusal');
    expect(whole).toHaveTextContent(en.filters.editor.refused);
    expect(whole).toHaveTextContent('A filter holds at most 200 conditions and groups.');
    expect(screen.getByTestId('filter-editor-apply')).toBeDisabled();
    expect(screen.getByTestId('filter-editor')).not.toHaveTextContent('never shown');
  });

  it("tells a Romanian reader in Romanian, and never shows the server's English or its field keys", async () => {
    await i18n.changeLanguage('ro');
    queryAnswer = {
      error: refusal([
        { code: 'filter.range_reversed', path: '/scope/0/where/of/1', field: 'surveyedLength' },
        { code: 'filter.too_many_nodes', path: '/scope/0/where', limit: 200 },
      ]),
    };
    show(REGISTRY);

    expect(screen.getByTestId('filter-editor-row-error-1')).toHaveTextContent(ro.filters.editor.problems.range_reversed);
    expect(screen.getByTestId('filter-editor-refusal')).toHaveTextContent(ro.filters.editor.refused);
    expect(screen.getByTestId('filter-editor-refusal')).toHaveTextContent('Un filtru conține cel mult 200 condiții și grupuri.');
    const editor = screen.getByTestId('filter-editor');
    expect(editor).not.toHaveTextContent('surveyedLength');
    expect(editor).not.toHaveTextContent('English sentence');
  });

  it("reads a path past the page's fixed condition, which is on no row of its own", () => {
    const fixed = c('kind', 'equals', [{ type: 'id', value: 'cave' }]);
    const where = c('surveyedLength', 'greaterOrEqual', [{ type: 'number', value: 300 }]);
    queryAnswer = {
      error: refusal([
        { code: 'filter.value_kind', path: '/scope/0/where/of/1', field: 'surveyedLength' },
        { code: 'filter.unknown_field', path: '/scope/0/where/of/0', field: 'kind' },
      ]),
    };
    render(<FilterEditor world="feature" document={doc({ node: 'allOf', of: [where] })} fixed={fixed} onApply={vi.fn()} />);

    expect(screen.getByTestId('filter-editor-row-error-0')).toHaveTextContent(en.filters.editor.problems.value_kind);
    expect(screen.getByTestId('filter-editor-refusal')).toHaveTextContent(en.filters.editor.problems.unknown_field);
  });

  it('words a code it does not know in the general sentence rather than leaving the row unmarked', () => {
    queryAnswer = { error: refusal([{ code: 'filter.something_new', path: '/scope/0/where/of/0' }]) };
    show(REGISTRY);

    expect(screen.getByTestId('filter-editor-row-error-0')).toHaveTextContent(en.filters.editor.rowRefused);
  });

  it("offers a type's own property only beside a condition fixing that type", async () => {
    show({ node: 'allOf', of: [c('typeId', 'equals', [{ type: 'id', value: '7' }])] });

    fireEvent.click(screen.getByTestId('filter-editor-add-row-root'));
    fireEvent.mouseDown(within(screen.getByTestId('filter-editor-field-1')).getByRole('combobox'));
    expect(await screen.findByText('Property: Sinkhole depth (m)')).toBeInTheDocument();

    cleanup();
    show({ node: 'allOf', of: [c('typeId', 'equals', [{ type: 'id', value: '8' }])] });
    fireEvent.click(screen.getByTestId('filter-editor-add-row-root'));
    fireEvent.mouseDown(within(screen.getByTestId('filter-editor-field-1')).getByRole('combobox'));
    expect(await screen.findByText('Surveyed length (m)')).toBeInTheDocument();
    expect(screen.queryByText('Property: Sinkhole depth (m)')).not.toBeInTheDocument();
  });
});

describe('a comparison typed with signs', () => {
  const length = (op: FilterOp, value: number) => c('surveyedLength', op, [{ type: 'number', value }]);

  function type(testId: string, text: string) {
    fireEvent.change(inputOf(testId), { target: { value: text } });
  }

  it('becomes the comparison it names, says what it understood, and is applied as that', () => {
    const onApply = show(length('greaterOrEqual', 300));

    type('filter-editor-value-0', '>300 m');
    expect(screen.getByTestId('filter-editor-value-0-understood')).toHaveTextContent('Read as: more than 300 m');
    expect(screen.getByTestId('filter-editor-op-0')).toHaveTextContent('more than');
    fireEvent.click(screen.getByTestId('filter-editor-apply'));

    expect(onApply).toHaveBeenCalledWith(doc(length('greaterThan', 300)));
  });

  it('keeps the chosen comparison for a bare number, and reads a range as between', () => {
    const onApply = show({ node: 'allOf', of: [length('lessOrEqual', 1), length('equals', 1)] });

    type('filter-editor-value-0', '2,5');
    type('filter-editor-value-1', '300-500');
    expect(screen.getByTestId('filter-editor-value-1-understood')).toHaveTextContent('between 300 and 500');
    fireEvent.click(screen.getByTestId('filter-editor-apply'));

    expect(onApply).toHaveBeenCalledWith(doc({
      node: 'allOf',
      of: [
        length('lessOrEqual', 2.5),
        c('surveyedLength', 'between', [{ type: 'number', value: 300 }, { type: 'number', value: 500 }]),
      ],
    }));
  });

  it('writes “not equal to” as a negated equals, ticks the row’s not, and undoes it when the sign goes', () => {
    const onApply = show(length('greaterOrEqual', 300));

    type('filter-editor-value-0', '!=300');
    expect(inputOf('filter-editor-not-0')).toBeChecked();
    expect(screen.getByTestId('filter-editor-value-0-understood')).toHaveTextContent('not equal to 300');
    fireEvent.click(screen.getByTestId('filter-editor-apply'));
    expect(onApply).toHaveBeenLastCalledWith(doc({ node: 'not', of: length('equals', 300) }));

    type('filter-editor-value-0', '>300');
    expect(inputOf('filter-editor-not-0')).not.toBeChecked();
    fireEvent.click(screen.getByTestId('filter-editor-apply'));
    expect(onApply).toHaveBeenLastCalledWith(doc(length('greaterThan', 300)));
  });

  it('says on the row when it cannot read the text, and applies nothing for it', () => {
    const onApply = show({ node: 'allOf', of: [length('greaterOrEqual', 300), c('name', 'contains', [{ type: 'text', value: 'urs' }])] });

    type('filter-editor-value-0', '300 km');
    expect(screen.getByTestId('filter-editor-value-0-unreadable')).toHaveTextContent(en.filters.typed.unreadable);
    fireEvent.click(screen.getByTestId('filter-editor-apply'));

    expect(onApply).toHaveBeenCalledWith(doc({ node: 'allOf', of: [c('name', 'contains', [{ type: 'text', value: 'urs' }])] }));
  });

  it('refuses a comparison the field does not offer', () => {
    show({ node: 'allOf', of: [c('property:sinkhole:depth_m', 'greaterOrEqual', [{ type: 'number', value: 1 }]), c('typeId', 'equals', [{ type: 'id', value: '7' }])] });

    type('filter-editor-value-0', '>5');
    expect(screen.getByTestId('filter-editor-value-0-unreadable')).toHaveTextContent(en.filters.typed.notOffered);
  });

  it('tells a Romanian reader in Romanian', async () => {
    await i18n.changeLanguage('ro');
    show({ node: 'allOf', of: [length('greaterOrEqual', 300)] });

    type('filter-editor-value-0', '≤30');
    expect(screen.getByTestId('filter-editor-value-0-understood')).toHaveTextContent('Înțeles ca: cel mult 30');
    type('filter-editor-value-0', 'abc');
    expect(screen.getByTestId('filter-editor-value-0-unreadable')).toHaveTextContent(ro.filters.typed.unreadable);
  });

  it('writes the row back into the box when its comparison is chosen from the list', () => {
    show({ node: 'allOf', of: [length('greaterOrEqual', 300)] });

    type('filter-editor-value-0', '>300');
    fireEvent.click(screen.getByTestId('filter-editor-not-0'));
    fireEvent.click(screen.getByTestId('filter-editor-not-0'));
    expect(inputOf('filter-editor-value-0')).toHaveValue('300');
  });

  it('reads a typed day as the whole day it names, beside the picker', () => {
    const onApply = show(c('surveyedAt', 'greaterThan', []));

    type('filter-editor-value-0-typed', '=2024-05-01');
    expect(screen.getByTestId('filter-editor-value-0-typed-understood')).toHaveTextContent('on 2024-05-01');
    fireEvent.click(screen.getByTestId('filter-editor-apply'));

    const where = (onApply.mock.calls[0]![0] as FilterDocument).scope[0]!.where as Extract<FilterNode, { node: 'condition' }>;
    expect(where.op).toBe('between');
    expect(where.values.map((v) => String('value' in v ? v.value : '').slice(0, 10))).toEqual(['2024-05-01', '2024-05-01']);

    type('filter-editor-value-0-typed', '2024-05-01-2024-06-01');
    expect(screen.getByTestId('filter-editor-value-0-typed-unreadable')).toHaveTextContent(en.filters.typed.unreadableDay);
  });
});

describe('the editor names every comparison and every closed value, in both languages', () => {
  it.each([['en', en], ['ro', ro]] as const)('%s', (_, locale) => {
    const editor = (locale as unknown as { filters: { editor: { ops: Record<string, string>; values: Record<string, Record<string, string>> } } }).filters.editor;
    expect(Object.keys(editor.ops).sort()).toEqual([...ALL_OPS].sort());
    expect(Object.keys(editor.values).sort()).toEqual(Object.keys(CLOSED_SETS).sort());
    for (const [set, members] of Object.entries(CLOSED_SETS)) {
      expect(Object.keys(editor.values[set]).sort(), set).toEqual([...members].sort());
    }
  });
});

const CAVE = '0b3f6a52-1d7e-4c5a-9a61-3f2d9c4e8b10';

describe('a spatial row', () => {
  const nearCave = c('position', 'within', [{ type: 'number', value: 500 }, { type: 'object', world: 'feature', id: CAVE }]);
  const nearPoint = c('position', 'within', [{ type: 'number', value: 500 }, { type: 'point', longitude: 23.5, latitude: 45.25 }]);

  it('is offered as a field, and a radius with a picked object is applied as written', () => {
    const onApply = show({ node: 'allOf', of: [c('position', 'within', [])] });
    fireEvent.change(inputOf('filter-editor-value-0-radius'), { target: { value: '500' } });
    fireEvent.change(inputOf('filter-editor-value-0-object'), { target: { value: CAVE } });
    fireEvent.click(screen.getByTestId('filter-editor-apply'));

    expect(onApply).toHaveBeenCalledWith(doc({ node: 'allOf', of: [nearCave] }));
  });

  it('leaves a radius with nothing to measure from out of what is counted', () => {
    show({ node: 'allOf', of: [c('position', 'within', [])] });
    fireEvent.change(inputOf('filter-editor-value-0-radius'), { target: { value: '500' } });

    const last = queriesAsked.at(-1)!.body as unknown as { document: FilterDocument };
    expect(last.document.scope[0]!.where).toBeNull();
  });

  it('shows a refused anchor on the row that names it, in the reader language, and will not apply it', async () => {
    queryAnswer = { error: new ApiError(400, 'filter.anchor_not_found', 'What this filter measures from is not available.') };
    show({ node: 'allOf', of: [c('name', 'contains', [{ type: 'text', value: 'urs' }]), nearCave] });

    expect(screen.getByTestId('filter-editor-row-error-1')).toHaveTextContent(en.filters.editor.spatial.refused.notFound);
    expect(screen.queryByTestId('filter-editor-row-error-0')).not.toBeInTheDocument();
    expect(screen.queryByTestId('filter-editor-refusal')).not.toBeInTheDocument();
    expect(screen.getByTestId('filter-editor-apply')).toBeDisabled();

    cleanup();
    await i18n.changeLanguage('ro');
    show({ node: 'allOf', of: [nearCave] });
    expect(screen.getByTestId('filter-editor-row-error-0')).toHaveTextContent(ro.filters.editor.spatial.refused.notFound);
    expect(screen.getByTestId('filter-editor')).not.toHaveTextContent('measures from');
  });

  it('keeps an anchor refusal with the whole filter when no row names something to look up', () => {
    queryAnswer = { error: new ApiError(400, 'filter.anchor_not_found', 'What this filter measures from is not available.') };
    show({ node: 'allOf', of: [nearPoint] });

    expect(screen.queryByTestId('filter-editor-row-error-0')).not.toBeInTheDocument();
    expect(screen.getByTestId('filter-editor-refusal')).toBeInTheDocument();
  });
});

describe('nearest first', () => {
  function showSortable() {
    const onApply = vi.fn();
    render(<FilterEditor world="feature" document={doc(null)} onApply={onApply} sortable />);
    return onApply;
  }

  it('is not offered where the page orders results its own way', () => {
    sorts = ['updated', 'proximity'];
    show(null);
    expect(screen.queryByTestId('filter-sort')).not.toBeInTheDocument();
  });

  it('needs a place to measure from before it is asked or applied, and then carries it', () => {
    sorts = ['updated', 'proximity'];
    const onApply = showSortable();

    fireEvent.mouseDown(within(screen.getByTestId('filter-sort-key')).getByRole('combobox'));
    fireEvent.click(screen.getByText(en.filters.sorts.proximity));
    expect(screen.getByTestId('filter-editor-needs-anchor')).toBeInTheDocument();
    expect(screen.getByTestId('filter-editor-apply')).toBeDisabled();
    expect(queriesAsked.at(-1)!.enabled).toBe(false);
    // Nearest first has no direction to choose.
    expect(screen.queryByTestId('filter-sort-direction')).not.toBeInTheDocument();

    fireEvent.change(inputOf('filter-sort-anchor-object'), { target: { value: CAVE } });
    fireEvent.click(screen.getByTestId('filter-editor-apply'));

    expect(onApply).toHaveBeenCalledWith({
      ...doc(null), sort: 'proximity', descending: true, anchor: { anchor: 'object', world: 'feature', id: CAVE },
    });
  });

  it('drops the anchor when another order is chosen', () => {
    sorts = ['updated', 'title', 'proximity'];
    const onApply = vi.fn();
    render(<FilterEditor world="feature" sortable onApply={onApply}
      document={{ ...doc(null), sort: 'proximity', anchor: { anchor: 'point', longitude: 23.5, latitude: 45.25 } }} />);

    fireEvent.mouseDown(within(screen.getByTestId('filter-sort-key')).getByRole('combobox'));
    fireEvent.click(screen.getByText(en.filters.sorts.title));
    fireEvent.click(screen.getByTestId('filter-editor-apply'));

    expect(onApply).toHaveBeenCalledWith({ ...doc(null), sort: 'title', descending: false });
  });
});
