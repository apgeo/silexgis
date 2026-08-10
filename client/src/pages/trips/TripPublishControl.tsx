// SPDX-License-Identifier: AGPL-3.0-or-later
import { NotificationOutlined, RollbackOutlined } from '@ant-design/icons';
import { App, Button, Flex, Popconfirm } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  usePublishTripLog,
  useUnpublishTripLog,
  type ActivityState,
} from '../../api/hooks.ts';

interface Props {
  tripId: string;
  state: ActivityState;
  /** The trip's own write permission, already decided by the page. */
  canEdit: boolean;
}

/**
 * Announcing a trip, and taking it back.
 *
 * Publishing is confirmed before it happens because it is not only a change of label: it is the
 * moment the people named on the trip are told about it, and that cannot be recalled by putting
 * the trip back into draft. Taking it back needs no confirmation for the same reason — it sends
 * nothing and loses nothing.
 *
 * Which moves are offered follows what a reader would expect of the state shown; the server holds
 * the rules and refuses anything else, so the two never have to be kept in step by hand.
 */
export default function TripPublishControl({ tripId, state, canEdit }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const publish = usePublishTripLog();
  const unpublish = useUnpublishTripLog();

  if (!canEdit) {
    return null;
  }

  const mayPublish = state === 'draft' || state === 'done';
  const mayReturnToDraft = state === 'published' || state === 'done' || state === 'cancelled';
  if (!mayPublish && !mayReturnToDraft) {
    return null;
  }

  const run = async (what: 'publish' | 'unpublish') => {
    try {
      await (what === 'publish' ? publish : unpublish).mutateAsync(tripId);
      message.success(t(what === 'publish' ? 'trips.publishSuccess' : 'trips.unpublishSuccess'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Flex gap={8}>
      {mayPublish && (
        <Popconfirm title={t('trips.publishConfirm')} onConfirm={() => void run('publish')}>
          <Button type="primary" icon={<NotificationOutlined />} loading={publish.isPending}>
            {t('trips.publish')}
          </Button>
        </Popconfirm>
      )}
      {mayReturnToDraft && (
        <Button
          icon={<RollbackOutlined />}
          loading={unpublish.isPending}
          onClick={() => void run('unpublish')}
        >
          {t('trips.unpublish')}
        </Button>
      )}
    </Flex>
  );
}
