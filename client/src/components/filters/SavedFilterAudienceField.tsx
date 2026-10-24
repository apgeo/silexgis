// SPDX-License-Identifier: AGPL-3.0-or-later
import { useId } from 'react';
import { SettingOutlined } from '@ant-design/icons';
import { Flex, Select, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import {
  SAVED_FILTER_AUDIENCE_SETTINGS_PATH,
  audienceFromKey,
  audienceKey,
  type SavedFilterAudience,
} from '../../filters/savedFilterAudience.ts';
import type { AudienceGroup } from './useSavedFilterAudienceChoice.ts';

interface Props {
  /** Set by the surrounding form item; the id ties its label to the choice. */
  id?: string;
  value?: SavedFilterAudience;
  onChange?: (audience: SavedFilterAudience) => void;
  /** The groups to offer; a group the value names that is not among them is still offered. */
  groups: readonly AudienceGroup[] | undefined;
  /**
   * The installation's other clubs, offered after the member's own under a heading that says the
   * server may refuse them.
   */
  otherGroups?: readonly AudienceGroup[];
  /** Names a group the offered list does not hold (an administrator editing another's filter). */
  groupName?: (id: string) => string | undefined;
  /** Offers the way to the member's default audience in Settings — where a new filter is saved. */
  showDefaultLink?: boolean;
  testId: string;
}

/**
 * Who a saved filter is shared with, chosen where the filter is saved, with what that choice means
 * said right under it: only the author, the members of one caving group, or everyone signed in to
 * this installation — and that whoever runs it is shown only what they could already see, because
 * a filter carries the question and never any access.
 *
 * The explanation is tied to the choice for screen readers, and the "change your default" link is
 * an ordinary link, so both are reachable from the keyboard.
 */
export default function SavedFilterAudienceField({
  id,
  value,
  onChange,
  groups,
  otherGroups,
  groupName,
  showDefaultLink = false,
  testId,
}: Props) {
  const { t } = useTranslation();
  const explanationId = useId();
  const audience = value ?? { visibility: 'private' };

  const offered = [...(groups ?? []), ...(otherGroups ?? [])];
  const nameOf = (id: string) =>
    offered.find((group) => group.id === id)?.name ?? groupName?.(id) ?? t('savedFilters.someGroup');
  const groupOption = (group: AudienceGroup) => ({
    value: audienceKey({ visibility: 'cavingGroup', cavingGroupId: group.id }),
    label: t('savedFilters.sharedWith', { name: group.name }),
  });

  const options = [
    { value: audienceKey({ visibility: 'private' }), label: t('savedFilters.private') },
    ...(groups ?? []).map(groupOption),
    // A group the lists do not hold — still loading, or one this reader cannot see — stays on
    // offer, so the control never shows a raw key and a rename never moves the filter.
    ...(audience.visibility === 'cavingGroup' && !offered.some((group) => group.id === audience.cavingGroupId)
      ? [{
          value: audienceKey(audience),
          label: groupName?.(audience.cavingGroupId) === undefined
            ? t('savedFilters.someGroup')
            : t('savedFilters.sharedWith', { name: nameOf(audience.cavingGroupId) }),
        }]
      : []),
    { value: audienceKey({ visibility: 'authenticated' }), label: t('savedFilters.authenticated') },
    ...(otherGroups && otherGroups.length > 0
      ? [{ label: t('savedFilters.otherClubs'), title: t('savedFilters.otherClubs'), options: otherGroups.map(groupOption) }]
      : []),
  ];

  const explanation =
    audience.visibility === 'cavingGroup'
      ? t('savedFilters.explain.cavingGroup', { name: nameOf(audience.cavingGroupId) })
      : t(`savedFilters.explain.${audience.visibility}`);

  return (
    <Flex vertical gap={4}>
      <Select
        id={id}
        value={audienceKey(audience)}
        onChange={(key: string) => onChange?.(audienceFromKey(key))}
        aria-label={t('savedFilters.audience')}
        aria-describedby={explanationId}
        data-testid={testId}
        options={options}
      />
      <Flex gap={8} align="baseline" wrap justify="space-between">
        <Typography.Text type="secondary" id={explanationId} data-testid={`${testId}-explanation`}>
          {explanation}
        </Typography.Text>
        {showDefaultLink && (
          <Link to={SAVED_FILTER_AUDIENCE_SETTINGS_PATH} data-testid={`${testId}-default-link`}>
            <SettingOutlined aria-hidden /> {t('savedFilters.changeDefault')}
          </Link>
        )}
      </Flex>
    </Flex>
  );
}
