// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { Alert, Checkbox, Flex, Select, Space, Tooltip, Typography } from 'antd';
import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import Button from '../Button.tsx';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import { useFilterQuery, useFilterVocabulary, type FilterQueryBody } from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import { whereOf, withSort, withWhere } from '../../filters/document.ts';
import { withAnchor } from '../../filters/spatial.ts';
import {
  appendChild,
  editableOps,
  isEditableKind,
  nodeAt,
  pathKey,
  placeAnchorRefusal,
  placeRefusal,
  prune,
  removeAt,
  replaceAt,
  requirementMet,
  startCondition,
  toggleNot,
  valuesForOp,
  withinDepth,
  type NodePath,
} from '../../filters/editorTree.ts';
import { preflight } from '../../filters/preflight.ts';
import type {
  FilterDocument,
  FilterField,
  FilterLimits,
  FilterNode,
  FilterOp,
  SortKey,
  WorldVocabulary,
} from '../../filters/types.ts';
import SortControl, { type SortState } from './SortControl.tsx';
import ValueInput from './ValueInput.tsx';

/**
 * Writing one world's conditions as rows, grouped and nested.
 *
 * The editor edits a draft and hands back a document only when it is applied, so a half-written row
 * never reaches a list, a link or a saved filter. While the draft is being written it is counted —
 * the same filter route the list uses, with the unfinished rows left out — so the person sees what
 * applying would give before they apply it, and a refusal from that route is shown on the row it is
 * about.
 *
 * What may be asked comes from the server's vocabulary for this caller and nowhere else. A field the
 * caller may not ask about is not offered; a field already in the document that the vocabulary no
 * longer holds is shown as unavailable rather than by its key.
 */

export interface FilterEditorProps {
  /** The world whose conditions are edited. The document's other worlds are carried unchanged. */
  world: string;
  document: FilterDocument;
  onApply: (document: FilterDocument) => void;
  onCancel?: () => void;
  /**
   * A condition the page always holds and the editor does not offer as a row — the cave registry's
   * "caves". It is part of what is checked and counted, so the count is the count of what the page
   * will show; it is never part of the document handed back.
   */
  fixed?: FilterNode;
  /**
   * Whether the editor also chooses how results are ordered — nearest first among them, with what
   * it is measured from. Only for a page that reads the order back out of the applied document.
   */
  sortable?: boolean;
  /**
   * The orders offered, when the page serves fewer than the world declares — the cave registry
   * keeps its own column sorts and takes only nearest first from here. Every world's declared
   * sorts otherwise.
   */
  sorts?: readonly SortKey[];
}

const ANCHOR_REFUSALS: Record<string, string> = {
  'filter.anchor_not_found': 'filters.editor.spatial.refused.notFound',
  'filter.study_area_not_an_outline': 'filters.editor.spatial.refused.notAnArea',
  'filter.anchor_nested': 'filters.editor.spatial.refused.nested',
  'filter.anchor_too_large': 'filters.editor.spatial.refused.tooLarge',
};

function anchorRefusalKey(error: unknown): string | null {
  return error instanceof ApiError && error.code !== undefined ? (ANCHOR_REFUSALS[error.code] ?? null) : null;
}

/** A row with nothing chosen yet. It is left out of everything sent until it has a field and a value. */
const BLANK_ROW: FilterNode = { node: 'condition', field: '', op: 'equals', values: [] };

function isGroup(node: FilterNode): node is Extract<FilterNode, { node: 'allOf' | 'anyOf' }> {
  return node.node === 'allOf' || node.node === 'anyOf';
}

function testKey(path: NodePath): string {
  return path.length === 0 ? 'root' : pathKey(path);
}

interface EditorContext {
  world: string;
  root: FilterNode;
  fields: FilterField[];
  limits: FilterLimits;
  problems: Map<string, string[]>;
  /** The phrase a refused row shows: which of the anchor refusals it was, or the general one. */
  rowRefusalKey: string;
  update: (root: FilterNode) => void;
}

export default function FilterEditor({
  world, document, onApply, onCancel, fixed, sortable = false, sorts,
}: FilterEditorProps) {
  const { t } = useTranslation();
  const vocabularyQuery = useFilterVocabulary();
  const worlds = (vocabularyQuery.data?.worlds ?? []) as unknown as WorldVocabulary[];
  const vocabulary = worlds.find((w) => w.world === world);
  const limits = vocabularyQuery.data?.limits as FilterLimits | undefined;

  // A root that is not a group is shown inside one, so there is always somewhere to add a row;
  // it is unwrapped again on the way out if it still holds just that one node, so opening and
  // applying without a change gives back the document that came in.
  const initial = whereOf(document, world);
  const [wrapped] = useState(initial === null || !isGroup(initial));
  const [root, setRoot] = useState<FilterNode>(() =>
    initial === null ? { node: 'allOf', of: [] } : isGroup(initial) ? initial : { node: 'allOf', of: [initial] });

  const [sort, setSort] = useState<SortState>(() => ({
    sort: document.sort,
    descending: document.descending,
    anchor: document.anchor ?? null,
  }));
  // Nearest first with nothing to measure from is not a question yet; it is held back like an
  // unfinished row rather than sent to be refused.
  const needsAnchor = sortable && sort.sort === 'proximity' && sort.anchor === null;

  const outgoing = useMemo(() => {
    const pruned = prune(root);
    const where = wrapped && pruned !== null && pruned.node === 'allOf' && pruned.of.length === 1
      ? pruned.of[0]
      : pruned;
    const edited = withWhere(document, world, where);
    return sortable
      ? withAnchor(withSort(edited, sort.sort, sort.descending), sort.sort === 'proximity' ? sort.anchor : null)
      : edited;
  }, [root, wrapped, document, world, sortable, sort]);

  // What the page will actually ask once this is applied: the fixed condition beside the edited
  // ones, in the same all-of the page writes, so the limits are checked against that shape.
  const asked = useMemo(() => {
    if (fixed === undefined) {
      return outgoing;
    }
    const where = whereOf(outgoing, world);
    const of = where === null ? [] : where.node === 'allOf' ? where.of : [where];
    return withWhere(outgoing, world, { node: 'allOf', of: [fixed, ...of] });
  }, [outgoing, fixed, world]);

  const problems = useMemo(
    () => (limits && vocabulary ? preflight(asked, limits, [vocabulary]) : []),
    [asked, limits, vocabulary],
  );

  const counted = useDebouncedValue(asked, 400);
  // Asked of the debounced document too: the draft gains its anchor at once while the count still
  // holds the document from before it, which would be sent nearest first with nothing to measure from.
  const countedNeedsAnchor = counted.sort === 'proximity' && (counted.anchor ?? null) === null;
  const countQuery = useFilterQuery(
    {
      document: counted as unknown as FilterQueryBody['document'],
      page: 1,
      pageSize: 1,
      count: true,
    },
    problems.length === 0 && !needsAnchor && !countedNeedsAnchor,
  );
  // An anchor the reader may not use is refused in one sentence that names no row — saying which
  // one was withheld would say it exists — so it lands on every row that names something to look up.
  const refusal = !(countQuery.error instanceof ApiError)
    ? null
    : countQuery.error.code === 'filter.invalid'
      ? placeRefusal(countQuery.error.detail ?? '', root)
      : anchorRefusalKey(countQuery.error) !== null
        ? placeAnchorRefusal(countQuery.error.detail ?? '', root)
        : null;
  const total = countQuery.data?.counted
    ? countQuery.data.worlds.find((w) => w.world === world)?.total ?? null
    : null;

  if (vocabulary === undefined || limits === undefined) {
    return <Typography.Text type="secondary">{t('common.loading')}</Typography.Text>;
  }

  const offeredSorts = sorts === undefined
    ? vocabulary.sorts
    : vocabulary.sorts.filter((s) => sorts.includes(s));

  const context: EditorContext = {
    world,
    root,
    fields: vocabulary.fields.filter((f) => isEditableKind(f.kind)),
    limits,
    problems: refusal?.byPath ?? new Map(),
    rowRefusalKey: anchorRefusalKey(countQuery.error) ?? 'filters.editor.rowRefused',
    update: setRoot,
  };

  return (
    <Flex vertical gap={12} data-testid="filter-editor">
      {sortable && offeredSorts.length > 0 && (
        <SortControl world={world} sorts={offeredSorts} value={sort} onChange={setSort} />
      )}
      {needsAnchor && (
        <Alert type="info" showIcon title={t('filters.editor.spatial.needsAnchor')} data-testid="filter-editor-needs-anchor" />
      )}
      <GroupBlock context={context} path={[]} />
      {problems.map((p) => (
        <Alert key={p.messageKey} type="warning" showIcon title={t(p.messageKey, p.values)}
          data-testid="filter-editor-problem" />
      ))}
      {/* The server explains a refusal in English and in its own field keys, so the reader is told
          in their language which rows it was about rather than shown its sentences. */}
      {refusal !== null && refusal.unplaced.length > 0 && (
        <Alert type="error" showIcon title={t('filters.editor.refused')} data-testid="filter-editor-refusal" />
      )}
      <Flex justify="space-between" align="center" wrap gap={8}>
        <Typography.Text data-testid="filter-editor-count" aria-live="polite">
          {total === null ? '' : t('filters.editor.count', { count: total })}
        </Typography.Text>
        <Space>
          {onCancel && <Button onClick={onCancel} data-testid="filter-editor-cancel">{t('common.cancel')}</Button>}
          <Button
            type="primary"
            disabled={problems.length > 0 || refusal !== null || needsAnchor}
            onClick={() => onApply(outgoing)}
            data-testid="filter-editor-apply"
          >
            {t('filters.editor.apply')}
          </Button>
        </Space>
      </Flex>
    </Flex>
  );
}

function GroupBlock({ context, path }: { context: EditorContext; path: NodePath }) {
  const { t } = useTranslation();
  const group = nodeAt(context.root, path);
  if (group === null || !isGroup(group)) {
    return null;
  }
  const key = testKey(path);
  const nested: FilterNode = { node: group.node === 'allOf' ? 'anyOf' : 'allOf', of: [BLANK_ROW] };
  const canNest = withinDepth(appendChild(context.root, path, nested), context.limits.maxDepth);
  const canAddRow = withinDepth(appendChild(context.root, path, BLANK_ROW), context.limits.maxDepth);

  return (
    <Flex
      vertical
      gap={8}
      role="group"
      aria-label={t(group.node === 'allOf' ? 'filters.editor.allOf' : 'filters.editor.anyOf')}
      data-testid={`filter-editor-group-${key}`}
      style={path.length > 0 ? { borderInlineStart: '2px solid', borderColor: 'var(--ant-color-border)', paddingInlineStart: 12 } : undefined}
    >
      <Select
        aria-label={t('filters.editor.combinator')}
        data-testid={`filter-editor-combinator-${key}`}
        value={group.node}
        onChange={(node: 'allOf' | 'anyOf') => context.update(replaceAt(context.root, path, { node, of: group.of }))}
        options={[
          { value: 'allOf', label: t('filters.editor.allOf') },
          { value: 'anyOf', label: t('filters.editor.anyOf') },
        ]}
        style={{ width: 220 }}
      />
      {group.of.map((_, i) => <Item key={i} context={context} path={[...path, i]} />)}
      <Space wrap>
        <Button icon={<PlusOutlined />} disabled={!canAddRow}
          onClick={() => context.update(appendChild(context.root, path, BLANK_ROW))}
          data-testid={`filter-editor-add-row-${key}`}>
          {t('filters.editor.addCondition')}
        </Button>
        <Tooltip title={canNest ? undefined : t('filters.editor.tooDeep', { max: context.limits.maxDepth })}>
          <Button icon={<PlusOutlined />} disabled={!canNest}
            onClick={() => context.update(appendChild(context.root, path, nested))}
            data-testid={`filter-editor-add-group-${key}`}>
            {t('filters.editor.addGroup')}
          </Button>
        </Tooltip>
      </Space>
    </Flex>
  );
}

/** A child of a group: a row or a nested group, either of them possibly negated. */
function Item({ context, path }: { context: EditorContext; path: NodePath }) {
  const { t } = useTranslation();
  const node = nodeAt(context.root, path);
  if (node === null) {
    return null;
  }
  const negated = node.node === 'not';
  // A negation is drawn as a box on the thing it negates; a negation of a negation, which the editor
  // never writes but a document may hold, is drawn as a box around a negated item.
  const innerPath = negated && node.of.node !== 'not' ? [...path, 0] : path;
  const inner = nodeAt(context.root, innerPath)!;
  const toggled = toggleNot(context.root, path);
  const canToggle = negated || withinDepth(toggled, context.limits.maxDepth);
  const key = testKey(path);

  const notBox = (
    <Checkbox
      checked={negated}
      disabled={!canToggle}
      onChange={() => context.update(toggled)}
      data-testid={`filter-editor-not-${key}`}
    >
      {t('filters.editor.not')}
    </Checkbox>
  );
  const remove = (
    <Button
      icon={<DeleteOutlined />}
      aria-label={t(inner.node === 'condition' ? 'filters.editor.removeCondition' : 'filters.editor.removeGroup')}
      onClick={() => context.update(removeAt(context.root, path))}
      data-testid={`filter-editor-remove-${key}`}
    />
  );

  if (inner.node === 'condition') {
    return (
      <Flex vertical gap={4} data-testid={`filter-editor-row-${key}`}>
        <Flex gap={8} wrap align="center">
          {notBox}
          <ConditionFields context={context} path={innerPath} rowKey={key} />
          {remove}
        </Flex>
        {context.problems.has(pathKey(innerPath)) && (
          <Typography.Text type="danger" role="alert" data-testid={`filter-editor-row-error-${key}`}>
            {t(context.rowRefusalKey)}
          </Typography.Text>
        )}
      </Flex>
    );
  }

  return (
    <Flex vertical gap={4}>
      <Flex gap={8} align="center">{notBox}{remove}</Flex>
      {inner.node === 'not'
        ? <Item context={context} path={innerPath} />
        : <GroupBlock context={context} path={innerPath} />}
    </Flex>
  );
}

function ConditionFields({ context, path, rowKey }: { context: EditorContext; path: NodePath; rowKey: string }) {
  const { t } = useTranslation();
  const condition = nodeAt(context.root, path) as Extract<FilterNode, { node: 'condition' }>;
  const field = context.fields.find((f) => f.key === condition.field);
  const offered = context.fields.filter((f) => f.key === condition.field || requirementMet(context.root, path, f));
  const set = (next: FilterNode) => context.update(replaceAt(context.root, path, next));
  // A type's own property is named by its schema, which may well reuse the name of a field every
  // feature has ("Depth (m)"), so it is marked as a property where the two would sit side by side.
  const labelOf = (f: FilterField) =>
    f.label === null ? t(f.labelKey) : f.requires === null ? f.label : t('filters.editor.propertyField', { label: f.label });

  const fieldOptions = offered.map((f) => ({ value: f.key, label: labelOf(f) }));
  if (condition.field !== '' && field === undefined) {
    fieldOptions.push({ value: condition.field, label: t('filters.editor.unavailableField') });
  }

  return (
    <>
      <Select
        aria-label={t('filters.editor.field')}
        data-testid={`filter-editor-field-${rowKey}`}
        placeholder={t('filters.editor.chooseField')}
        value={condition.field === '' ? undefined : condition.field}
        onChange={(key: string) => {
          const next = context.fields.find((f) => f.key === key);
          if (next) {
            set(startCondition(next));
          }
        }}
        options={fieldOptions}
        showSearch
        optionFilterProp="label"
        style={{ width: 220 }}
      />
      {field && (
        <Select
          aria-label={t('filters.editor.operator')}
          data-testid={`filter-editor-op-${rowKey}`}
          value={condition.op}
          onChange={(op: FilterOp) => set({ ...condition, op, values: valuesForOp(op, condition.values) })}
          options={editableOps(field).map((op) => ({ value: op, label: t(`filters.editor.ops.${op}`) }))}
          style={{ width: 160 }}
        />
      )}
      {field && (
        <ValueInput
          world={context.world}
          field={field}
          op={condition.op}
          values={condition.values}
          onChange={(values) => set({ ...condition, values })}
          testId={`filter-editor-value-${rowKey}`}
        />
      )}
      {field && !requirementMet(context.root, path, field) && (
        <Typography.Text type="warning" data-testid={`filter-editor-needs-type-${rowKey}`}>
          {t('filters.editor.needsType')}
        </Typography.Text>
      )}
    </>
  );
}
