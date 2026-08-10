// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { AutoComplete, Flex, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useResLinkTargets } from '../../api/hooks.ts';
import type { SelectorScope } from '../../filters/selection.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import ObjectSelector from '../selector/ObjectSelector.tsx';
import type { ResLinkTargetType } from './registry.ts';

/**
 * Choosing the thing a link points at.
 *
 * Two implementations behind one name, and the split is temporary by construction. Where the kind
 * of target is a world the filter model serves, this is the ordinary selector — which means it
 * inherits the things that were decided once: an id it cannot describe reads as restricted rather
 * than as a raw identifier, nothing is asked until enough has been typed, and a search box never
 * asks the server for a total. Where the kind is not yet a world, it stays the search it always
 * was.
 *
 * When the remaining five kinds become worlds, the second branch goes and this file becomes a
 * thin call to the selector. Until then the branch lives here rather than in the dialog, so the
 * dialog does not have to know there are two.
 */

/** The target kinds the filter model can already answer for. */
const WORLD_OF: Partial<Record<ResLinkTargetType, string>> = {
  feature: 'feature',
  document: 'document',
  tripLog: 'tripLog',
  mapView: 'mapView',
};

const SEARCH_MIN_LENGTH = 2;

/** Short, because this sits inside a dialog that already has a dozen other things in it. */
const PICKER_ROWS = 6;

interface ResLinkTargetPickerProps {
  targetType: ResLinkTargetType;
  /** Cleared to null while somebody is still typing, so a half-typed name cannot be submitted. */
  onChange: (id: string | null, title: string | null) => void;
  value: string | null;
  disabled?: boolean;
}

export default function ResLinkTargetPicker({
  targetType,
  onChange,
  value,
  disabled = false,
}: ResLinkTargetPickerProps) {
  const { t } = useTranslation();
  const world = WORLD_OF[targetType];

  if (world) {
    // One scope, so the control draws no scope buttons: which kind of thing is being linked was
    // already answered by the dropdown above, and asking twice in one dialog would be a second
    // control for one decision.
    const scopes: SelectorScope[] = [
      { id: targetType, labelKey: `resLinks.targetTypes.${targetType}`, world, where: null },
    ];

    return (
      <ObjectSelector
        scopes={scopes}
        value={value}
        pageSize={PICKER_ROWS}
        minChars={SEARCH_MIN_LENGTH}
        // No sort buttons: this is a picker inside a form, not a place anybody works, and the
        // same reason it remembers nothing between openings.
        sorts={[]}
        disabled={disabled}
        placeholder={t('resLinks.searchPlaceholder')}
        aria-label={t('resLinks.target')}
        data-testid="reslink-target-picker"
        onChange={(next) => {
          if (next === null) {
            onChange(null, null);
          }
        }}
        onPick={(hit) => onChange(hit?.id ?? null, hit?.title ?? null)}
      />
    );
  }

  return <LegacyTargetSearch
    targetType={targetType}
    onChange={onChange}
    disabled={disabled}
  />;
}

/**
 * The search as it was, for the kinds that are not worlds yet — cavers, caving groups, cabinets,
 * survey models and geofiles.
 */
function LegacyTargetSearch({
  targetType,
  onChange,
  disabled,
}: Omit<ResLinkTargetPickerProps, 'value'>) {
  const { t } = useTranslation();
  const [query, setQuery] = useState('');
  const debounced = useDebouncedValue(query);
  const searchable = debounced.trim().length >= SEARCH_MIN_LENGTH;
  const { data: hits, isFetching } = useResLinkTargets(targetType, debounced, searchable);

  const options = useMemo(
    () =>
      (hits ?? []).map((hit) => ({
        value: hit.id,
        label: (
          <Flex vertical>
            <span>{hit.title}</span>
            {hit.subtitle && <Typography.Text type="secondary">{hit.subtitle}</Typography.Text>}
          </Flex>
        ),
        title: hit.title,
      })),
    [hits],
  );

  return (
    <Flex vertical gap={4}>
      <AutoComplete
        value={query}
        options={options}
        disabled={disabled}
        // The server does the matching; filtering the answer again here would only hide rows it
        // deliberately returned.
        filterOption={false}
        notFoundContent={null}
        onSearch={(next) => {
          setQuery(next);
          onChange(null, null);
        }}
        onSelect={(selected, option) => {
          setQuery(option.title ?? '');
          onChange(String(selected), option.title ?? null);
        }}
        placeholder={t('resLinks.searchPlaceholder')}
        aria-label={t('resLinks.target')}
      />
      {isFetching && <Typography.Text type="secondary">{t('common.loading')}</Typography.Text>}
    </Flex>
  );
}
