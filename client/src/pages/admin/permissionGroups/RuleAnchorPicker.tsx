// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Input, Select } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCavingGroups,
  useFeatures,
  usePermissionGroups,
  type AccessCatalog,
  type AccessDomainName,
  type AccessScopeKind,
} from '../../../api/hooks.ts';
import { useDebouncedValue } from '../../../hooks/useDebouncedValue.ts';

interface RuleAnchorPickerProps {
  domain: AccessDomainName;
  scopeKind: AccessScopeKind;
  catalog: AccessCatalog;
  value: string | null;
  /** Server-resolved display name; null for a fresh row or an anchor the caller may not read. */
  label: string | null;
  onChange: (id: string | null, label: string | null) => void;
}

/**
 * Picks what a rule is anchored on, per scope: a feature (subtree/object in the feature
 * world), a caving group, a feature set, a permission group — or, for the object scopes
 * no picker exists for (a single trip log, file…), the object's id pasted directly.
 * Which scopes need an anchor at all comes from the catalog, never from here.
 */
export default function RuleAnchorPicker({
  domain, scopeKind, catalog, value, label, onChange,
}: RuleAnchorPickerProps) {
  const { t } = useTranslation();
  const [featureQuery, setFeatureQuery] = useState('');
  const debouncedQuery = useDebouncedValue(featureQuery);

  const anchorsOnFeature = domain === 'features' && (scopeKind === 'subtree' || scopeKind === 'object');
  const { data: featurePage } = useFeatures(
    { search: debouncedQuery || undefined, pageSize: 20 },
    anchorsOnFeature && debouncedQuery.trim().length >= 2,
  );
  const { data: cavingGroups } = useCavingGroups();
  const { data: permissionGroups } = usePermissionGroups(
    scopeKind === 'object' && domain === 'permissionGroups',
  );

  if (scopeKind === 'cavingGroup') {
    return (
      <Select
        size="small"
        style={{ minWidth: 180 }}
        showSearch
        optionFilterProp="label"
        placeholder={t('permissionGroups.pickCavingGroupAnchor')}
        value={value ?? undefined}
        onChange={(id) => onChange(id, cavingGroups?.find((g) => g.id === id)?.name ?? null)}
        options={cavingGroups?.map((group) => ({ value: group.id, label: group.name }))}
      />
    );
  }

  if (scopeKind === 'featureSet') {
    return (
      <Select
        size="small"
        style={{ minWidth: 180 }}
        showSearch
        optionFilterProp="label"
        placeholder={t('permissionGroups.pickFeatureSetAnchor')}
        value={value ?? undefined}
        onChange={(id) => onChange(id, catalog.featureSets.find((s) => s.id === id)?.name ?? null)}
        options={catalog.featureSets.map((set) => ({ value: set.id, label: set.name }))}
      />
    );
  }

  if (anchorsOnFeature) {
    // Server-side search; the current value may not be among the results, so it is
    // carried as its own option under whatever label the server resolved for it.
    const options = (featurePage?.items ?? []).map((feature) => ({
      value: feature.id,
      label: feature.name ?? `${t(`featureKinds.${feature.kind}`)} ${feature.id.slice(0, 8)}`,
    }));
    if (value && !options.some((o) => o.value === value)) {
      options.unshift({ value, label: label ?? value });
    }
    return (
      <Select
        size="small"
        style={{ minWidth: 200 }}
        showSearch
        filterOption={false}
        placeholder={t('permissionGroups.pickFeatureAnchor')}
        value={value ?? undefined}
        onSearch={setFeatureQuery}
        onChange={(id) => onChange(id, options.find((o) => o.value === id)?.label ?? null)}
        options={options}
        notFoundContent={null}
      />
    );
  }

  if (scopeKind === 'object' && domain === 'permissionGroups') {
    return (
      <Select
        size="small"
        style={{ minWidth: 180 }}
        showSearch
        optionFilterProp="label"
        placeholder={t('permissionGroups.pickPermissionGroupAnchor')}
        value={value ?? undefined}
        onChange={(id) => onChange(id, permissionGroups?.find((g) => g.id === id)?.name ?? null)}
        options={permissionGroups?.map((group) => ({ value: group.id, label: group.name }))}
      />
    );
  }

  // Object scopes with no directory to pick from (one trip log, one file…): the id is
  // pasted from the object's own page. Rare, admin-grade, and validated server-side.
  return (
    <Input
      size="small"
      style={{ minWidth: 200 }}
      placeholder={t('permissionGroups.anchorIdPlaceholder')}
      value={value ?? ''}
      onChange={(e) => onChange(e.target.value.trim() || null, null)}
    />
  );
}
