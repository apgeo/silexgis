// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState, type ComponentType, type ReactNode } from 'react';
import { Input, Select } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCaveTypes,
  useCavingGroups,
  useDocumentTypes,
  useFeatureTypes,
  useMember,
  useRockTypes,
  useTags,
  useTagsBySlug,
  useTripTypes,
  useUserSearch,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import { CLOSED_SETS } from './closedSets.ts';

/**
 * Choosing identity values for a condition, one control per kind of list the server names.
 *
 * Every list comes from the route that already decides what this caller may see — the taxonomies,
 * the caving groups, the tags, the user search — so the picker can offer nothing the reader could
 * not have found elsewhere. A value already in the filter that the list does not hold (a group the
 * reader is not in, a type since removed) is shown as "not available to you", never as its raw
 * identifier: a filter handed on by somebody else must not become a way to read the ids it names.
 */

export interface IdPickerProps {
  /** The values chosen, as the condition holds them. */
  value: string[];
  multiple: boolean;
  onChange: (value: string[]) => void;
  ariaLabel: string;
  testId: string;
}

interface ListOption {
  value: string;
  label: ReactNode;
}

function ListSelect({
  options,
  loading,
  ...props
}: IdPickerProps & { options: ListOption[] | undefined; loading: boolean }) {
  const { t } = useTranslation();
  const known = new Set((options ?? []).map((o) => o.value));
  // Only once the list has arrived: while it loads, a stored value is not yet unavailable.
  const missing = loading ? [] : props.value.filter((v) => !known.has(v));
  const all = [
    ...(options ?? []),
    ...missing.map((v) => ({ value: v, label: t('savedFilters.notAvailable') })),
  ];
  return (
    <Select
      aria-label={props.ariaLabel}
      data-testid={props.testId}
      mode={props.multiple ? 'multiple' : undefined}
      value={props.multiple ? props.value : props.value[0]}
      onChange={(next: string | string[] | undefined) =>
        props.onChange(next === undefined ? [] : Array.isArray(next) ? next : [next])}
      options={loading && props.value.length > 0 ? props.value.map((v) => ({ value: v, label: t('common.loading') })) : all}
      loading={loading}
      showSearch
      optionFilterProp="label"
      allowClear
      style={{ minWidth: 200 }}
    />
  );
}

function closedSet(key: string): ComponentType<IdPickerProps> {
  return function ClosedSetPicker(props) {
    const { t } = useTranslation();
    const options = CLOSED_SETS[key].map((v) => ({ value: v, label: t(`filters.editor.values.${key}.${v}`) }));
    return <ListSelect {...props} options={options} loading={false} />;
  };
}

function taxonomy(useList: () => { data?: { id: number; name: string }[]; isLoading: boolean }) {
  return function TaxonomyPicker(props: IdPickerProps) {
    const { data, isLoading } = useList();
    const options = data?.map((x) => ({ value: String(x.id), label: x.name }));
    return <ListSelect {...props} options={options} loading={isLoading} />;
  };
}

/**
 * A list answered by the server as the reader types, with the values already chosen named apart
 * from it — so a chosen value is never mistaken for an unavailable one only because the search in
 * hand did not happen to return it.
 */
function SearchedSelect({
  options,
  loading,
  onSearch,
  ...props
}: IdPickerProps & { options: ListOption[]; loading: boolean; onSearch: (search: string) => void }) {
  return (
    <Select
      aria-label={props.ariaLabel}
      data-testid={props.testId}
      mode={props.multiple ? 'multiple' : undefined}
      value={props.multiple ? props.value : props.value[0]}
      onChange={(next: string | string[] | undefined) =>
        props.onChange(next === undefined ? [] : Array.isArray(next) ? next : [next])}
      options={options}
      loading={loading}
      showSearch
      filterOption={false}
      onSearch={onSearch}
      allowClear
      style={{ minWidth: 200 }}
    />
  );
}

/**
 * Tags are searched on the server, which answers a hundred at a time, and the chosen ones are named
 * by their slugs in a lookup of their own: a tag the reader can read is named however many tags
 * the installation holds, and only one the server does not return reads as not available.
 */
function TagPicker(props: IdPickerProps) {
  const { t } = useTranslation();
  const [search, setSearch] = useState('');
  const debounced = useDebouncedValue(search);
  const { data: found, isFetching } = useTags(debounced);
  const { data: chosen, isLoading: resolving } = useTagsBySlug(props.value);
  const names = new Map((chosen ?? []).map((x) => [x.slug, x.name]));
  // A tag condition names the tag by its slug, which is what the server compares.
  const options: ListOption[] = [
    ...props.value.map((slug) => ({
      value: slug,
      label: resolving ? t('common.loading') : (names.get(slug) ?? t('savedFilters.notAvailable')),
    })),
    ...(found ?? []).filter((x) => !props.value.includes(x.slug)).map((x) => ({ value: x.slug, label: x.name })),
  ];
  return <SearchedSelect {...props} options={options} loading={isFetching} onSearch={setSearch} />;
}

function CavingGroupPicker(props: IdPickerProps) {
  const { data, isLoading } = useCavingGroups();
  const options = data?.map((x) => ({ value: x.id, label: x.name }));
  return <ListSelect {...props} options={options} loading={isLoading} />;
}

/** A chosen member's name, or "not available to you" when the directory will not describe them. */
function MemberName({ id }: { id: string }) {
  const { t } = useTranslation();
  const { data, isError } = useMember(id);
  if (isError) {
    return <>{t('savedFilters.notAvailable')}</>;
  }
  return <>{data === undefined ? t('common.loading') : (data.displayName ?? data.label)}</>;
}

/**
 * People are searched for, not listed: the directory answers from two characters, and the chosen
 * ones are named one at a time through the member route, which refuses anybody the reader may not
 * see.
 */
function UserPicker(props: IdPickerProps) {
  const [search, setSearch] = useState('');
  const debounced = useDebouncedValue(search);
  const { data, isFetching } = useUserSearch(debounced);
  const found = (data ?? []).filter((u) => !props.value.includes(u.id));
  const options: ListOption[] = [
    ...props.value.map((id) => ({ value: id, label: <MemberName id={id} /> })),
    ...found.map((u) => ({ value: u.id, label: u.label })),
  ];
  return <SearchedSelect {...props} options={options} loading={isFetching} onSearch={setSearch} />;
}

/** A list the client has no picker for: the identifier is typed, as the server would read it. */
function TypedIdPicker(props: IdPickerProps) {
  return (
    <Input
      aria-label={props.ariaLabel}
      data-testid={props.testId}
      value={props.value[0] ?? ''}
      onChange={(e) => props.onChange(e.target.value === '' ? [] : [e.target.value])}
      style={{ width: 200 }}
    />
  );
}

const PICKERS: Record<string, ComponentType<IdPickerProps>> = {
  featureKinds: closedSet('featureKinds'),
  featureCategories: closedSet('featureCategories'),
  visibilities: closedSet('visibilities'),
  activityStates: closedSet('activityStates'),
  mapKinds: closedSet('mapKinds'),
  surveyModelFormats: closedSet('surveyModelFormats'),
  geofileFormats: closedSet('geofileFormats'),
  caveTypes: taxonomy(useCaveTypes),
  rockTypes: taxonomy(useRockTypes),
  featureTypes: taxonomy(useFeatureTypes),
  documentTypes: taxonomy(useDocumentTypes),
  tripTypes: taxonomy(useTripTypes),
  tags: TagPicker,
  cavingGroups: CavingGroupPicker,
  users: UserPicker,
};

export default function IdPicker({ options, ...props }: IdPickerProps & { options: string | null }) {
  const Picker = (options !== null ? PICKERS[options] : undefined) ?? TypedIdPicker;
  return <Picker {...props} />;
}
