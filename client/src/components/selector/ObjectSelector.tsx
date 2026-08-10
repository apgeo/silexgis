// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState, type CSSProperties } from 'react';
import { Button, Checkbox, Empty, Flex, Input, Popover, Space, Tag, theme, Tooltip, Typography } from 'antd';
import {
  ArrowDownOutlined,
  ArrowUpOutlined,
  EnvironmentOutlined,
  SearchOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { choose, pressSort, toggleScope, type SelectorScope } from '../../filters/selection.ts';
import type { FilterHit, SortKey } from '../../filters/types.ts';
import List from '../List.tsx';
import { useObjectSelector } from './useObjectSelector.ts';

/**
 * One control for choosing things, wherever choosing happens.
 *
 * Before this there were about twenty pickers: some a dropdown, some a modal, each with its own
 * idea of how many characters to wait for, whether an empty box lists anything, and what to show
 * for a stored value it could not find — one of which put a raw identifier on screen. They are
 * replaced by this, which asks the one filter route and therefore cannot be more generous than the
 * screen beside it.
 *
 * Everything it decides lives in `src/filters/selection.ts`, which cannot import React. This file
 * is chrome: it draws buttons, rows and a box, and hands what happened to functions that are tested
 * without it.
 */

export interface ObjectSelectorProps {
  /**
   * What this control may offer. A caller narrows it — "only caves", "caves and documents" — and
   * that is an arrangement of the interface, never a boundary: the server decides what any caller
   * may ask about, and would refuse a world they may not regardless of what is passed here.
   */
  scopes: readonly SelectorScope[];

  /** antd's Form.Item injects these two and nothing else, so nothing else may be required. */
  value?: string | string[] | null;
  onChange?: (value: string | string[] | null) => void;

  /** The chosen row itself, for callers that need its name rather than its identifier. */
  onPick?: (hit: FilterHit | null) => void;

  multiple?: boolean;

  /** Ids never offered: the row being edited, or ones already added somewhere else on screen. */
  exclude?: readonly string[];

  /**
   * Where this control's last arrangement is remembered — which buttons were on, how rows were
   * ordered. Two controls given the same key share one memory; omitting it remembers nothing,
   * which is right inside a form, where the picker is part of filling something in rather than a
   * place somebody works.
   *
   * Never the query. A stored search term would be a record of what a person was looking for,
   * sitting in a browser they may share.
   */
  rememberAs?: string;

  /** `inline` draws the panel; `dropdown` puts it behind the box it belongs to. */
  presentation?: 'inline' | 'dropdown';

  /** Rows before the list scrolls. Fifteen, because the ask was to see fifteen. */
  pageSize?: number;

  minChars?: number;

  /**
   * Whether an empty box lists anything. Off by default: a picker that answers an empty query
   * hands back whichever rows sort first, which is how one becomes a way to enumerate a domain.
   */
  browseOnEmpty?: boolean;

  sorts?: readonly SortKey[];
  placeholder?: string;
  disabled?: boolean;
  autoFocus?: boolean;
  size?: 'small' | 'middle' | 'large';
  'aria-label'?: string;
  'data-testid'?: string;
}

export default function ObjectSelector({
  scopes,
  value,
  onChange,
  onPick,
  multiple = false,
  exclude = [],
  rememberAs,
  presentation = 'inline',
  pageSize = 15,
  minChars = 2,
  browseOnEmpty = false,
  sorts,
  placeholder,
  disabled = false,
  autoFocus = false,
  size = 'middle',
  'aria-label': ariaLabel,
  'data-testid': testId,
}: ObjectSelectorProps) {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const [open, setOpen] = useState(false);

  const chosenIds = useMemo(
    () => (value === null || value === undefined ? [] : Array.isArray(value) ? value : [value]),
    [value],
  );

  const selector = useObjectSelector({
    scopes,
    multiple,
    minChars,
    pageSize,
    browseOnEmpty,
    exclude,
    sorts,
    rememberAs,
    value: chosenIds,
  });

  const emit = (ids: string[], picked: FilterHit | null) => {
    onChange?.(multiple ? ids : (ids[0] ?? null));
    onPick?.(picked);
    if (!multiple) {
      setOpen(false);
    }
  };

  const onRow = (hit: FilterHit) => {
    const next = choose({ ...selector.state, chosen: chosenIds }, hit.id, multiple);
    emit(next.chosen, multiple ? hit : (next.chosen.length > 0 ? hit : null));
  };

  const panel = (
    <Flex vertical gap={8} style={{ width: presentation === 'dropdown' ? 380 : '100%' }}>
      <Input
        allowClear
        autoFocus={autoFocus}
        size={size}
        prefix={<SearchOutlined />}
        placeholder={placeholder ?? t('filters.selector.placeholder')}
        aria-label={ariaLabel ?? t('filters.selector.placeholder')}
        value={selector.state.query}
        disabled={disabled}
        onChange={(event) =>
          selector.setState((s) => ({ ...s, query: event.target.value }))}
        data-testid={testId ? `${testId}-input` : undefined}
      />

      {scopes.length > 1 && (
        <Space size={4} wrap>
          {scopes.map((scope) => {
            const active = selector.state.activeScopeIds.includes(scope.id);
            return (
              <Button
                key={scope.id}
                size="small"
                type={active ? 'primary' : 'default'}
                aria-pressed={active}
                onClick={() => selector.setState((s) => toggleScope(s, scope.id))}
                data-testid={testId ? `${testId}-scope-${scope.id}` : undefined}
              >
                {scope.prefix ? `${scope.prefix} ` : ''}
                {t(scope.labelKey)}
              </Button>
            );
          })}
        </Space>
      )}

      {selector.availableSorts.length > 0 && (
        <Space size={4} wrap>
          {selector.availableSorts.map((sort) => {
            const active = selector.state.sort === sort;
            return (
              <Button
                key={sort}
                size="small"
                type={active ? 'primary' : 'text'}
                aria-pressed={active}
                icon={
                  active
                    ? (selector.state.descending ? <ArrowDownOutlined /> : <ArrowUpOutlined />)
                    : undefined
                }
                onClick={() => selector.setState((s) => pressSort(s, sort))}
                data-testid={testId ? `${testId}-sort-${sort}` : undefined}
              >
                {t(`filters.sorts.${sort}`)}
              </Button>
            );
          })}
        </Space>
      )}

      <div
        style={{ maxHeight: 15 * 34, overflowY: 'auto' }}
        data-testid={testId ? `${testId}-rows` : undefined}
      >
        <List
          size="small"
          loading={selector.loading}
          dataSource={selector.rows}
          locale={{
            emptyText: (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description={
                  selector.asked
                    ? t('filters.selector.noMatches')
                    : t('filters.selector.typeToSearch', { count: minChars })
                }
              />
            ),
          }}
          renderItem={(hit: FilterHit) => {
            const picked = chosenIds.includes(hit.id);
            return (
              <List.Item
                onClick={() => onRow(hit)}
                style={{ cursor: 'pointer' }}
                data-testid={testId ? `${testId}-row` : undefined}
                data-picked={picked ? 'true' : 'false'}
                actions={
                  // A permission, not a position: the only thing it may drive is whether an
                  // offer to show this on a map is worth making.
                  hit.placeable
                    ? [
                        <Tooltip key="p" title={t('filters.selector.placeable')}>
                          <EnvironmentOutlined style={{ color: token.colorTextDescription }} />
                        </Tooltip>,
                      ]
                    : undefined
                }
              >
                <Flex align="center" gap={8} style={{ minWidth: 0 }}>
                  {multiple && <Checkbox checked={picked} />}
                  <List.Item.Meta
                    title={<Typography.Text strong={picked}>{hit.title}</Typography.Text>}
                    description={hit.subtitle ?? undefined}
                  />
                </Flex>
              </List.Item>
            );
          }}
        />
      </div>

      {selector.restricted.length > 0 && (
        // Said once, without saying which. An id that resolves to nothing is one this caller may
        // not see or one that is gone, and the two are indistinguishable on purpose — naming the
        // identifier here would put a raw uuid on screen where a name belongs.
        <Typography.Text type="secondary" data-testid={testId ? `${testId}-restricted` : undefined}>
          {t('filters.selector.restricted', { count: selector.restricted.length })}
        </Typography.Text>
      )}
    </Flex>
  );

  if (presentation === 'inline') {
    return <div data-testid={testId}>{panel}</div>;
  }

  const summary = chosenIds.length === 0
    ? t('filters.selector.nothingChosen')
    : selector.describedChoices.map((h) => h.title).join(', ')
      || t('filters.selector.restricted', { count: chosenIds.length });

  return (
    <Popover
      open={open && !disabled}
      onOpenChange={setOpen}
      trigger="click"
      placement="bottomLeft"
      content={panel}
      styles={{ content: { padding: 12 } as CSSProperties }}
    >
      <Button
        block
        size={size}
        disabled={disabled}
        aria-label={ariaLabel ?? t('filters.selector.placeholder')}
        data-testid={testId}
        style={{ textAlign: 'left' }}
      >
        {chosenIds.length > 1
          ? <Tag>{t('filters.selector.chosen', { count: chosenIds.length })}</Tag>
          : null}
        {summary}
      </Button>
    </Popover>
  );
}
