// SPDX-License-Identifier: AGPL-3.0-or-later
import { Flex, Select, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useResLinkRelationTypes, type ResLinkRelationType } from '../../api/hooks.ts';
import { relationPhrase } from './relations.ts';

interface Props {
  value?: number | null;
  /** The chosen relation's id, plus whether it reads differently from each end. */
  onChange?: (value: number | null, directed: boolean) => void;
  /**
   * Name of the member the relation reads from, when one has been marked. A directed
   * relation says different things from each end, so the preview stays out of the way
   * until there is a main member to read it from.
   */
  mainLabel?: string | null;
  disabled?: boolean;
}

/**
 * The relation vocabulary as one list. Rows the installation ships are translated by their
 * code; rows an administrator added show the names they stored, in whatever language they
 * wrote them. Order comes from the server's sort field, never from this file — an
 * administrator reordering the vocabulary is the whole point of it being a table.
 */
export default function RelationSelect({ value, onChange, mainLabel, disabled }: Props) {
  const { t } = useTranslation();
  const { data: relationTypes, isLoading } = useResLinkRelationTypes();

  const options = (relationTypes ?? []).map((relationType) => ({
    value: relationType.id,
    label: relationPhrase(relationType, 'forward', t),
  }));

  const selected: ResLinkRelationType | undefined = (relationTypes ?? []).find(
    (relationType) => relationType.id === value,
  );

  return (
    <Flex vertical gap={4}>
      <Select
        allowClear
        loading={isLoading}
        disabled={disabled}
        value={value ?? undefined}
        onChange={(next) => {
          const picked = (relationTypes ?? []).find((relationType) => relationType.id === next);
          onChange?.(next ?? null, picked?.directed ?? false);
        }}
        options={options}
        placeholder={t('resLinks.relationPlaceholder')}
        aria-label={t('resLinks.relation')}
      />
      {selected?.directed && (
        <Typography.Text type="secondary">
          {mainLabel
            ? t('resLinks.directionPreview', {
                main: mainLabel,
                forward: relationPhrase(selected, 'forward', t),
                inverse: relationPhrase(selected, 'inverse', t),
              })
            : t('resLinks.directionNeedsMain')}
        </Typography.Text>
      )}
    </Flex>
  );
}
