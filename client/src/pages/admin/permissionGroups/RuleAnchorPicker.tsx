// SPDX-License-Identifier: AGPL-3.0-or-later
import { Input, Select } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCabinets,
  useCavingGroups,
  usePermissionGroups,
  type AccessCatalog,
  type AccessDomainName,
  type AccessScopeKind,
} from '../../../api/hooks.ts';
import ObjectSelector from '../../../components/selector/ObjectSelector.tsx';
import type { SelectorScope } from '../../../filters/selection.ts';

/** A subtree or object rule may hang on any kind of feature, so the whole feature world is offered. */
const FEATURE_ANCHOR_SCOPES: readonly SelectorScope[] = [
  { id: 'features', labelKey: 'filters.scopes.features', world: 'feature', where: null },
];

interface RuleAnchorPickerProps {
  domain: AccessDomainName;
  scopeKind: AccessScopeKind;
  catalog: AccessCatalog;
  value: string | null;
  onChange: (id: string | null) => void;
}

/**
 * Picks what a rule is anchored on, per scope: a feature (subtree/object in the feature
 * world), a caving group, a feature set, a permission group — or, for the object scopes
 * no picker exists for (a single trip log, file…), the object's id pasted directly.
 * Which scopes need an anchor at all comes from the catalog, never from here.
 */
export default function RuleAnchorPicker({
  domain, scopeKind, catalog, value, onChange,
}: RuleAnchorPickerProps) {
  const { t } = useTranslation();

  const anchorsOnFeature = domain === 'features' && (scopeKind === 'subtree' || scopeKind === 'object');
  const { data: cavingGroups } = useCavingGroups();
  const { data: permissionGroups } = usePermissionGroups(
    scopeKind === 'object' && domain === 'permissionGroups',
  );
  const { data: cabinets } = useCabinets(scopeKind === 'cabinet');

  if (scopeKind === 'cavingGroup') {
    return (
      <Select
        size="small"
        style={{ minWidth: 180 }}
        showSearch
        optionFilterProp="label"
        placeholder={t('permissionGroups.pickCavingGroupAnchor')}
        value={value ?? undefined}
        onChange={(id) => onChange(id)}
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
        onChange={(id) => onChange(id)}
        options={catalog.featureSets.map((set) => ({ value: set.id, label: set.name }))}
      />
    );
  }

  if (scopeKind === 'cabinet') {
    // Shown with its ancestry, because two shelves in different archives may share a
    // name and a rule that names the wrong one is not visibly wrong afterwards. That is why this
    // stays a list of its own rather than a search over the cabinet filter world: a row there
    // names only the cabinet directly above, not the whole path.
    const path = (cabinet: { id: string; name: string; ancestorIds: string[] }) =>
      cabinet.ancestorIds
        .map((id) => cabinets?.find((c) => c.id === id)?.name ?? cabinet.name)
        .join(' / ');
    return (
      <Select
        size="small"
        style={{ minWidth: 220 }}
        showSearch
        optionFilterProp="label"
        placeholder={t('permissionGroups.pickCabinetAnchor')}
        value={value ?? undefined}
        onChange={(id) => onChange(id)}
        options={(cabinets ?? []).map((cabinet) => ({ value: cabinet.id, label: path(cabinet) }))}
      />
    );
  }

  if (anchorsOnFeature) {
    // A stored anchor is named by the filter route under the editor's own visibility; one they
    // may not read shows as "not shown" rather than as its identifier. Searching is the same
    // route, so it offers nothing the editor could not already find on the feature list.
    return (
      <div style={{ minWidth: 200 }}>
        <ObjectSelector
          scopes={FEATURE_ANCHOR_SCOPES}
          presentation="dropdown"
          size="small"
          pageSize={8}
          sorts={[]}
          placeholder={t('permissionGroups.pickFeatureAnchor')}
          aria-label={t('permissionGroups.pickFeatureAnchor')}
          value={value}
          onPick={(hit) => onChange(hit?.id ?? null)}
          data-testid="rule-anchor-feature-picker"
        />
      </div>
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
        onChange={(id) => onChange(id)}
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
      onChange={(e) => onChange(e.target.value.trim() || null)}
    />
  );
}
