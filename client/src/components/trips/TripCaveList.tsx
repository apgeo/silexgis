// SPDX-License-Identifier: AGPL-3.0-or-later
import { EyeInvisibleOutlined } from '@ant-design/icons';
import { Flex, Tag, Tooltip } from 'antd';
import { useTranslation } from 'react-i18next';
import TripCaveLink from './TripCaveLink.tsx';

/**
 * The caves a trip names, and — as a number, never as an identity — how many of them this reader
 * was not given.
 *
 * The list a trip hands over is short of every cave its reader may not open and every cave they
 * may not place, so a surface that simply drew what it was given would show a trip that went to
 * fewer places than it did, and two people's screens would differ with nothing to say why. The
 * count is what makes the shortfall a state of this surface rather than a gap in it: it says the
 * list is incomplete without saying anything at all about what is missing, which is the whole
 * point of having withheld it — an identifier is enough to go and ask for the cave by it.
 *
 * Which is also why this draws even when the list is empty. A reader given none of the caves must
 * still be told the trip named some, or a withholding is indistinguishable from a trip that never
 * recorded a cave.
 *
 * Shared between the trip page and its report so the two cannot come to say different things
 * about the same list.
 */
export default function TripCaveList({
  caveIds,
  withheld,
}: {
  caveIds: readonly string[];
  withheld: number;
}) {
  const { t } = useTranslation();
  if (caveIds.length === 0 && withheld <= 0) {
    return null;
  }
  return (
    <Flex gap={8} wrap>
      {caveIds.map((caveId) => (
        <TripCaveLink key={caveId} caveId={caveId} />
      ))}
      {withheld > 0 && (
        <Tooltip title={t('trips.cavesWithheldDetail')}>
          <Tag icon={<EyeInvisibleOutlined />} data-testid="trip-caves-withheld">
            {t('trips.cavesWithheld', { count: withheld })}
          </Tag>
        </Tooltip>
      )}
    </Flex>
  );
}
