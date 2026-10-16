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
  field('property:sinkhole:depth_m', 'number', ['lessThan', 'greaterOrEqual'], {
    labelKey: 'filters.fields.property', label: 'Sinkhole depth (m)', requires: { field: 'typeId', value: '7' },
  }),
];

let limits = { maxNodes: 200, maxDepth: 6, maxValuesPerCondition: 200, maxWorlds: 12, maxTextValueLength: 200, maxPageSize: 500 };
let queryAnswer: { data?: unknown; error?: unknown } = {};
const queriesAsked: { body: { count: boolean; pageSize: number }; enabled: boolean }[] = [];

vi.mock('../../api/hooks.ts', () => ({
  useFilterVocabulary: () => ({
    data: { worlds: [{ world: 'feature', labelKey: 'x', fields: FIELDS, sorts: ['updated'] }], limits },
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

  it('puts the server refusal on the row it names, and will not apply it', () => {
    queryAnswer = {
      error: new ApiError(400, 'filter.invalid',
        "'surveyedLength' was given a number it cannot be compared against. A filter holds at most 200 conditions and groups."),
    };
    show(REGISTRY);

    expect(screen.getByTestId('filter-editor-row-error-1')).toHaveTextContent(en.filters.editor.rowRefused);
    expect(screen.queryByTestId('filter-editor-row-error-0')).not.toBeInTheDocument();
    expect(screen.getByTestId('filter-editor-refusal')).toHaveTextContent(en.filters.editor.refused);
    expect(screen.getByTestId('filter-editor-apply')).toBeDisabled();
  });

  it("tells a Romanian reader in Romanian, and never shows the server's English or its field keys", async () => {
    await i18n.changeLanguage('ro');
    queryAnswer = {
      error: new ApiError(400, 'filter.invalid',
        "'surveyedLength' was given a range that ends before it starts. A filter holds at most 200 conditions and groups."),
    };
    show(REGISTRY);

    expect(screen.getByTestId('filter-editor-row-error-1')).toHaveTextContent(ro.filters.editor.rowRefused);
    expect(screen.getByTestId('filter-editor-refusal')).toHaveTextContent(ro.filters.editor.refused);
    const editor = screen.getByTestId('filter-editor');
    expect(editor).not.toHaveTextContent('surveyedLength');
    expect(editor).not.toHaveTextContent('ends before it starts');
    expect(editor).not.toHaveTextContent('at most 200 conditions');
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
