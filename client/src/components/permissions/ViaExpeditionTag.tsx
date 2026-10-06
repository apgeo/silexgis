// SPDX-License-Identifier: AGPL-3.0-or-later
import { Tag } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { useExpedition } from '../../api/hooks.ts';

/**
 * The mark on a rule a camp wrote: which camp, as a link to it, so whoever is reading a trip's
 * permissions and wants this rule changed can go where it is changed.
 *
 * A camp the reader may not open answers as one that does not exist — the server spells both the
 * same way, on purpose — and a link to a page that says "no such camp" would send them nowhere
 * useful. So the mark then says only that a camp wrote the rule, which is the whole of what the
 * reader may be told.
 */
export default function ViaExpeditionTag({ expeditionId }: { expeditionId: string }) {
  const { t } = useTranslation();
  const { data: camp } = useExpedition(expeditionId);

  return (
    <Tag color="blue" style={{ marginInlineStart: 8 }} data-testid="permissions-via-expedition">
      {camp ? (
        <Link to={`/expeditions/${camp.id}`}>
          {t('permissions.viaNamedExpedition', { name: camp.name })}
        </Link>
      ) : (
        t('permissions.viaExpedition')
      )}
    </Tag>
  );
}
