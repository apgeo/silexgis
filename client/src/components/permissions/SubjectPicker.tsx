// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Select } from 'antd';
import { useTranslation } from 'react-i18next';
import { useCavingGroups, useUserSearch, type AccessSubjectKind } from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

interface SubjectPickerProps {
  kind: AccessSubjectKind;
  /** The picked account or caving group, or undefined while nobody is picked. */
  subjectId: string | undefined;
  /** A new kind always clears the picked subject: an account id names no caving group. */
  onKindChange: (kind: AccessSubjectKind) => void;
  /** The picked subject's id and the name it was offered under (null when none was drawn). */
  onSubjectChange: (subjectId: string | undefined, name: string | null) => void;
  kindWidth?: number;
  subjectMinWidth?: number;
}

/**
 * Who a rule, a membership or a preview is about: the kind (an account or a caving group) and
 * the one picked.
 *
 * Caving groups arrive in full and are filtered here as typed; accounts are searched on the
 * server, debounced, because the directory can be large. An account is offered as its name
 * with its address beside it only when the person shares the address. Both pickers carry their
 * own accessible names, since a placeholder disappears once something is chosen and a screen
 * reader would then announce a bare value with nothing saying what it is.
 */
export default function SubjectPicker({
  kind,
  subjectId,
  onKindChange,
  onSubjectChange,
  kindWidth = 140,
  subjectMinWidth = 200,
}: SubjectPickerProps) {
  const { t } = useTranslation();
  const { data: cavingGroups } = useCavingGroups();
  const [userQuery, setUserQuery] = useState('');
  const debouncedUserQuery = useDebouncedValue(userQuery);
  const { data: users } = useUserSearch(debouncedUserQuery);

  const userOptions = users?.map((user) => ({
    value: user.id,
    label: user.email ? `${user.label} (${user.email})` : user.label,
  }));
  const groupOptions = cavingGroups?.map((group) => ({ value: group.id, label: group.name }));

  return (
    <>
      <Select
        aria-label={t('permissions.subjectKindLabel')}
        value={kind}
        style={{ width: kindWidth }}
        onChange={(next: AccessSubjectKind) => {
          setUserQuery('');
          onKindChange(next);
        }}
        options={[
          { value: 'user', label: t('permissions.user') },
          { value: 'cavingGroup', label: t('permissions.cavingGroup') },
        ]}
      />
      {kind === 'cavingGroup' ? (
        <Select
          aria-label={t('permissions.cavingGroupLabel')}
          style={{ flex: 1, minWidth: subjectMinWidth }}
          // The option values are ids, so filtering on them would match nothing a person types.
          showSearch
          optionFilterProp="label"
          placeholder={t('permissions.pickCavingGroup')}
          value={subjectId}
          onChange={(id: string | undefined) =>
            onSubjectChange(id, cavingGroups?.find((g) => g.id === id)?.name ?? null)}
          options={groupOptions}
        />
      ) : (
        <Select
          aria-label={t('permissions.userLabel')}
          style={{ flex: 1, minWidth: subjectMinWidth }}
          showSearch
          filterOption={false}
          placeholder={t('permissions.pickUser')}
          value={subjectId}
          onSearch={setUserQuery}
          onChange={(id: string | undefined) =>
            onSubjectChange(id, users?.find((u) => u.id === id)?.label ?? null)}
          options={userOptions}
          notFoundContent={null}
        />
      )}
    </>
  );
}
