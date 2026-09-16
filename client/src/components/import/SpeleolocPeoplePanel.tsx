// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Alert, Card, Flex, Select, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useCavers, type SpeleolocImportOptions } from '../../api/hooks.ts';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

interface Props {
  options: SpeleolocImportOptions;
  onChange: (next: SpeleolocImportOptions) => void;
  /** Every device account the chosen recording's scans were made under. */
  deviceUsers: readonly string[];
  /** The ones nothing has been said about yet. The server's own answer, not a count of blanks. */
  unmappedDeviceUsers: readonly string[];
  /**
   * People this mapping names who are not on the chosen trip's roster. Empty when the confirmation
   * is to create the trip, because a created trip is created with exactly these people on it.
   */
  caversNotOnRoster: readonly string[];
}

/** A device account is a 36-character identifier; the head of it is enough to tell two apart. */
function shortAccount(deviceUserId: string) {
  return deviceUserId.slice(0, 8);
}

/**
 * Who each device account is, filled in by hand.
 *
 * Nothing here proposes anybody, and that is the point rather than an omission. A device account
 * is a login on a phone: the archive carries its identifier and, on some builds, an address and a
 * label somebody typed once — none of which is evidence about which caver was underground. So the
 * choices open empty, a mapping is only ever what a person said, and an account left unmapped
 * costs every scan it made rather than being guessed at.
 *
 * Both warnings are stated here, above the work, rather than left to be counted off two hundred
 * rows: one unmapped account and one mapped person who is not on the trip cost exactly the same
 * thing — every scan they made — and both are fixed in one act if the reviewer is told which.
 */
export default function SpeleolocPeoplePanel({
  options,
  onChange,
  deviceUsers,
  unmappedDeviceUsers,
  caversNotOnRoster,
}: Props) {
  const { t } = useTranslation();
  // Sized on the pointer rather than the width, like every other control that decides something
  // here: saying who a device account is, on a phone, must not be a matter of aim.
  const controlSize = useCoarsePointer() ? 'middle' : 'small';
  const [search, setSearch] = useState('');
  const debounced = useDebouncedValue(search);
  const candidates = useCavers(debounced || undefined);
  // The whole roster, for naming people the searched page does not carry — the person a stored
  // mapping already names, and the ones the chosen trip has no place for.
  const roster = useCavers();

  const mapping = options.cavers ?? {};

  const nameOf = (caverId: string) =>
    [...(roster.data ?? []), ...(candidates.data ?? [])].find((caver) => caver.id === caverId)?.name
    ?? t('speleolocImport.unnamedCaver');

  const choose = (deviceUserId: string, caverId: string | undefined) => {
    const next = { ...mapping };
    if (caverId === undefined) {
      delete next[deviceUserId];
    } else {
      next[deviceUserId] = caverId;
    }
    onChange({ ...options, cavers: next });
  };

  const options_ = [
    ...(candidates.data ?? []).map((caver) => ({ value: caver.id, label: caver.name })),
    // Whoever a stored mapping already names, so a resumed review shows a name rather than an
    // identifier when the searched page does not happen to carry that person.
    ...Object.values(mapping)
      .filter((caverId) => !(candidates.data ?? []).some((caver) => caver.id === caverId))
      .map((caverId) => ({ value: caverId, label: nameOf(caverId) })),
  ];

  return (
    <Card size="small" title={t('speleolocImport.peopleTitle')} data-testid="speleoloc-import-people">
      <Typography.Paragraph type="secondary">{t('speleolocImport.peopleHint')}</Typography.Paragraph>

      {unmappedDeviceUsers.length > 0 && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid="speleoloc-import-people-unmapped"
          title={t('speleolocImport.peopleUnmappedTitle', { count: unmappedDeviceUsers.length })}
          description={t('speleolocImport.peopleUnmappedBody')}
        />
      )}

      {caversNotOnRoster.length > 0 && (
        <Alert
          type="error"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid="speleoloc-import-people-off-roster"
          title={t('speleolocImport.peopleOffRosterTitle', { count: caversNotOnRoster.length })}
          description={
            <>
              <Typography.Paragraph style={{ marginBottom: 8 }}>
                {t('speleolocImport.peopleOffRosterBody')}
              </Typography.Paragraph>
              {caversNotOnRoster.map((caverId) => (
                <Tag key={caverId} color="red">
                  {nameOf(caverId)}
                </Tag>
              ))}
            </>
          }
        />
      )}

      {deviceUsers.length === 0 && (
        <Typography.Text type="secondary" data-testid="speleoloc-import-people-none">
          {t('speleolocImport.peopleNone')}
        </Typography.Text>
      )}

      {deviceUsers.map((deviceUserId) => (
        <Flex key={deviceUserId} gap={8} align="center" wrap style={{ marginBottom: 8 }}>
          <Tag color={mapping[deviceUserId] ? 'green' : 'gold'}>
            {t('speleolocImport.deviceAccount', { id: shortAccount(deviceUserId) })}
          </Tag>
          <Select
            allowClear
            showSearch
            // Searched on the server, so the box is not narrowed twice against a page it has
            // already narrowed.
            filterOption={false}
            size={controlSize}
            style={{ minWidth: 220, flex: '1 1 220px' }}
            loading={candidates.isFetching}
            // Empty, always, until somebody says otherwise. A placeholder that named the most
            // likely caver would be read as an answer by everybody who did not stop to check.
            placeholder={t('speleolocImport.chooseCaver')}
            value={mapping[deviceUserId] ?? undefined}
            onSearch={setSearch}
            onChange={(value?: string) => choose(deviceUserId, value)}
            data-testid={`speleoloc-import-map-${shortAccount(deviceUserId)}`}
            options={options_}
          />
        </Flex>
      ))}
    </Card>
  );
}
