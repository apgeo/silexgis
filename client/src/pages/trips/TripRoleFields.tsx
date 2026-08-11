// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { PlusOutlined } from '@ant-design/icons';
import { Button, Card, Dropdown, Flex } from 'antd';
import { useTranslation } from 'react-i18next';
import { useResLinksForTarget } from '../../api/hooks.ts';
import RoleField from '../../components/reslinks/RoleField.tsx';
import { TRIP_ROLE_CODES, tripRoleLabelKey } from '../../components/reslinks/relations.ts';

/**
 * The two roles a trip report is expected to state, so their fields stand whether or not
 * anything is recorded in them yet. Every other role appears once it holds something —
 * ten empty boxes is the difference between a report written and a report not written.
 */
const ALWAYS_PRESENT: readonly string[] = ['trip-work-area', 'trip-objective'];

/**
 * Enough links to decide which roles this trip has recorded anything in. Deliberately the
 * same ask the trip's general links list already makes, so the two share one answer rather
 * than each fetching the page.
 */
const MAX_ROWS = 200;

/**
 * What this trip did, as one labelled field per role.
 *
 * Every field is a filtered view of the same links list — nothing here is a store of its
 * own — so a role is whatever links carry its relation, merged, and a link recorded from
 * anywhere else shows up in its field without being told to.
 *
 * The fields are written through the link routes rather than through the trip, which is
 * why they live on the page and not in the trip form: the form submits one trip write, and
 * a trip has to exist before anything can link to it. It is the same split the tags and the
 * attachments on this page already follow.
 */
export default function TripRoleFields({
  tripId,
  tripTitle,
  canEdit,
}: {
  tripId: string;
  tripTitle?: string | null;
  canEdit: boolean;
}) {
  const { t } = useTranslation();
  const [revealed, setRevealed] = useState<readonly string[]>([]);

  // One unfiltered read decides which roles have content. Each field still reads its own
  // relation — that is what it draws from — but this is what keeps a trip with two roles
  // recorded from mounting ten fields and asking ten times.
  const { data } = useResLinksForTarget('tripLog', tripId, { pageSize: MAX_ROWS });
  const recorded = new Set(
    (data?.items ?? []).map((link) => link.relationType?.code).filter((code) => Boolean(code)),
  );

  // The always-present two and anything revealed are invitations to write, so they stand
  // only for somebody who can accept the invitation; a reader sees the roles that hold
  // something, and a trip that recorded none of them shows no section at all.
  const shown = TRIP_ROLE_CODES.filter(
    (code) =>
      recorded.has(code) ||
      (canEdit && (ALWAYS_PRESENT.includes(code) || revealed.includes(code))),
  );
  const hidden = TRIP_ROLE_CODES.filter((code) => !shown.includes(code));

  if (shown.length === 0) {
    return null;
  }

  return (
    <Card size="small" title={t('trips.rolesTitle')} style={{ marginTop: 16 }}>
      <Flex vertical gap={8}>
        {shown.map((code) => (
          <RoleField
            key={code}
            entityType="tripLog"
            entityId={tripId}
            entityTitle={tripTitle}
            relationCode={code}
            label={t(tripRoleLabelKey(code))}
            canAdd={canEdit}
            // Where the trip worked is a passage or a sector, and those names repeat
            // across the installation — a "Galeria Mare" in every second cave — so this
            // is the one field where the name alone does not say which one. The path is
            // derived from the hierarchy at read time, never recorded on the link: a
            // stored path is one that lies the first time a passage is re-parented.
            showPath={code === 'trip-work-area'}
            // A lead is a continuation somebody will come back for, and what was left is
            // the whole of what the field says — a cave's name alone would not tell the
            // next party where to go. The other roles name a thing and mean it.
            showNotes={code === 'trip-lead'}
          />
        ))}
        {canEdit && hidden.length > 0 && (
          <Dropdown
            trigger={['click']}
            menu={{
              items: hidden.map((code) => ({ key: code, label: t(tripRoleLabelKey(code)) })),
              onClick: ({ key }) => setRevealed((codes) => [...codes, key]),
            }}
          >
            <Button type="link" size="small" icon={<PlusOutlined />} style={{ alignSelf: 'start' }}>
              {t('trips.roleOther')}
            </Button>
          </Dropdown>
        )}
      </Flex>
    </Card>
  );
}
