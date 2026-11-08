// SPDX-License-Identifier: AGPL-3.0-or-later
import { Flex, Radio, Select } from 'antd';
import { useTranslation } from 'react-i18next';
import { useCan, useCavingGroups, type ProcessingProfileScope } from '../../api/hooks.ts';
import { SCOPE_LABEL_KEYS } from './scopeLabels.ts';
import type { ProfileScopeChoice } from './profileRules.ts';

/**
 * Where a new profile is kept: for oneself, which anybody may; or for a caving group or the whole
 * installation, which needs the scan-profiles create right. That right is held over the domain as
 * a whole, not group by group, so whoever holds it may pick any group; somebody without it is
 * offered only their own.
 */
export default function ProfileScopePicker({
  value,
  onChange,
}: {
  value: ProfileScopeChoice;
  onChange: (value: ProfileScopeChoice) => void;
}) {
  const { t } = useTranslation();
  const mayShare = useCan('scanProfiles', 'create');
  const groups = useCavingGroups();
  const scopes: ProcessingProfileScope[] = mayShare ? ['personal', 'cavingGroup', 'installation'] : ['personal'];

  return (
    <Flex vertical gap={8} data-testid="profile-scope">
      <Radio.Group
        value={value.scope}
        onChange={(event) => {
          const scope = event.target.value as ProcessingProfileScope;
          onChange({ scope, cavingGroupId: scope === 'cavingGroup' ? value.cavingGroupId : null });
        }}
        options={scopes.map((scope) => ({ value: scope, label: t(SCOPE_LABEL_KEYS[scope]) }))}
      />
      {value.scope === 'cavingGroup' && (
        <Select
          aria-label={t('settings.scanProfiles.scope.group')}
          placeholder={t('settings.scanProfiles.scope.pickGroup')}
          value={value.cavingGroupId ?? undefined}
          loading={groups.isPending}
          options={(groups.data ?? []).map((group) => ({ value: group.id, label: group.name }))}
          onChange={(cavingGroupId: string) => onChange({ scope: 'cavingGroup', cavingGroupId })}
          showSearch
          optionFilterProp="label"
          style={{ maxWidth: 320 }}
        />
      )}
    </Flex>
  );
}

