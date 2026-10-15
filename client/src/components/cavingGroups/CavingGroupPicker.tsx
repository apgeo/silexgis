// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { PlusOutlined } from '@ant-design/icons';
import { App, Select } from 'antd';
import { useTranslation } from 'react-i18next';
import { useCan, useCavingGroups, useCreateCavingGroup, type CavingGroupInfo } from '../../api/hooks.ts';
import { cavingGroupKey, foldName } from './cavingGroupNames.ts';
import { cavingGroupCreateProblem } from './cavingGroupProblems.ts';

/** The value of the one option that is not a group but an offer to make one. */
const CREATE = '__create__';

/** The longest name the server accepts for a caving group. */
const MAX_NAME = 120;

interface CavingGroupPickerProps {
  /** The chosen group's id, as a form field holds it. */
  value?: string | null;
  onChange?: (value: string | undefined) => void;
  id?: string;
  placeholder?: string;
  'data-testid'?: string;
}

/**
 * Picks a caving group from the directory, or makes the one that is missing.
 *
 * Typing narrows the directory, accent-blind. When what was typed names no group already there —
 * compared the way the server compares names for duplicates, so "Speo Club" and "speo-club" are
 * one — the list ends with an offer to create it, and choosing that makes the group and selects
 * it in one step. A club somebody belongs to is a link to the directory, never free text, so the
 * way to name a club that is not listed yet is to list it.
 *
 * The offer is made only to somebody who may create caving groups, which every account may unless
 * an installation has taken that away; the server re-checks either way, and its refusals — a name
 * somebody created a moment ago, a right taken away — are said in words rather than as a failure.
 */
export default function CavingGroupPicker({ value, onChange, id, placeholder, ...rest }: CavingGroupPickerProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: groups, isLoading } = useCavingGroups();
  const create = useCreateCavingGroup();
  const canCreate = useCan('cavingGroups', 'create');
  const [search, setSearch] = useState('');
  // A group made here, kept until the directory read again includes it, so the field shows its
  // name rather than its id in between.
  const [made, setMade] = useState<CavingGroupInfo | null>(null);

  const known = useMemo(() => {
    const list = groups ?? [];
    return made && !list.some((group) => group.id === made.id) ? [...list, made] : list;
  }, [groups, made]);

  const typed = search.trim();
  const typedKey = cavingGroupKey(typed);
  const offerCreate =
    canCreate &&
    typedKey.length > 0 &&
    typed.length <= MAX_NAME &&
    !known.some((group) => group.slug === typedKey);

  const folded = foldName(typed);
  const options = [
    ...known
      // By the letters as typed, or by the key duplicates are judged on — so a name that is
      // refused as a copy is never hidden from the list at the same moment.
      .filter(
        (group) =>
          folded.length === 0 ||
          foldName(group.name).includes(folded) ||
          (typedKey.length > 0 && group.slug.includes(typedKey)),
      )
      .map((group) => ({ value: group.id, label: group.name })),
    ...(offerCreate
      ? [
          {
            value: CREATE,
            label: (
              <span>
                <PlusOutlined /> {t('cavingGroupPicker.create', { name: typed })}
              </span>
            ),
          },
        ]
      : []),
  ];

  const choose = async (chosen: string | undefined) => {
    if (chosen !== CREATE) {
      setSearch('');
      onChange?.(chosen);
      return;
    }

    try {
      const group = await create.mutateAsync({ name: typed, type: 'cavingClub', description: null, website: null });
      setMade(group);
      setSearch('');
      onChange?.(group.id);
      message.success(t('cavingGroupPicker.created', { name: group.name }));
    } catch (error) {
      message.error(cavingGroupCreateProblem(error, t));
    }
  };

  return (
    <Select
      id={id}
      data-testid={rest['data-testid']}
      allowClear
      showSearch
      filterOption={false}
      searchValue={search}
      onSearch={setSearch}
      placeholder={placeholder}
      loading={isLoading || create.isPending}
      disabled={create.isPending}
      value={value ?? undefined}
      options={options}
      onChange={(chosen) => void choose(chosen as string | undefined)}
      onClear={() => setSearch('')}
      notFoundContent={typed.length > 0 ? t('cavingGroupPicker.noMatch') : null}
    />
  );
}
