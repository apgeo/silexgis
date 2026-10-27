// SPDX-License-Identifier: AGPL-3.0-or-later
import { Flex, Tag } from 'antd';
import { useTranslation } from 'react-i18next';
import type { HeldBatch } from '../../api/hooks.ts';
import { SECTION_ORDER } from './reviewModel.ts';

/** A batch's counts by kind, as tags. */
export function SectionTags({ sections }: { sections: HeldBatch['sections'] }) {
  const { t } = useTranslation();
  const names = Object.keys(sections).sort((a, b) => {
    const rank = (n: string) => {
      const i = SECTION_ORDER.indexOf(n as (typeof SECTION_ORDER)[number]);
      return i < 0 ? SECTION_ORDER.length : i;
    };
    return rank(a) - rank(b) || a.localeCompare(b);
  });
  return (
    <Flex gap={4} wrap>
      {names.map((n) => (
        <Tag key={n}>
          {t(`review.section.${n || 'none'}`, { defaultValue: n || t('review.section.none') })}: {sections[n]}
        </Tag>
      ))}
    </Flex>
  );
}
