// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined } from '@ant-design/icons';
import { Alert, App, Button, Flex, Modal, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { useDeleteCaver } from '../../api/hooks.ts';
import { RemoveReportsOfDialog } from '../trips/RemoveReportsOfDialog.tsx';
import { parseTripDay } from '../trips/tripDates.ts';
import {
  CAVER_HELD_BY_CAMP_CODE,
  afterReportsRemoved,
  caverHeldByTrips,
  type CaverHeldByTrip,
  type CaverHeldByTrips,
} from './caverHeldByTrips.ts';
import { ApiError } from '../../api/client.ts';

/**
 * What still holds a person whose delete was refused, and what can be done about each thing
 * from here.
 *
 * Opened by the refusal itself, which names the trips this reader may open. Each is a link — a
 * name on a roster comes off by editing the trip — and where this reader may write the trip's
 * tracking log, the person's reports of it can be removed for good on the spot. Anything the
 * refusal could not name is one line saying so. Merging stays the other way out, for an entry
 * that is a duplicate rather than a person who asked to go.
 *
 * <b>Nothing here deletes the person without being pressed.</b> Once the list is empty the
 * delete is offered again as a button of its own; trying it silently after the last removal
 * would turn "remove these reports" into "and the person" behind the reader's back.
 */
export function CaverHeldByTripsModal({
  caver,
  held: initial,
  onClose,
  onMerge,
}: {
  caver: { id: string; name: string };
  held: CaverHeldByTrips;
  onClose: () => void;
  /** Opens the merge for this person; left out where the reader may not merge. */
  onMerge?: () => void;
}) {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const [held, setHeld] = useState(initial);
  const [removing, setRemoving] = useState<CaverHeldByTrip | null>(null);
  const deleteCaver = useDeleteCaver();

  const nothingShownIsLeft = held.trips.length === 0;

  const tryDelete = async () => {
    try {
      await deleteCaver.mutateAsync(caver.id);
      message.success(t('cavers.heldByTrips.deleted', { name: caver.name }));
      onClose();
    } catch (error) {
      // Refused again: the answer is the list as it now stands, which may name something that
      // came to hold the person meanwhile.
      const again = caverHeldByTrips(error);
      if (again) {
        setHeld(again);
        message.warning(t('cavers.heldByTrips.stillHeld'));
      } else if (error instanceof ApiError && error.code === CAVER_HELD_BY_CAMP_CODE) {
        message.error(t('cavers.deleteRefusedCamp'));
      } else {
        message.error(t('cavers.deleteRefused'));
      }
    }
  };

  return (
    <Modal
      title={t('cavers.heldByTrips.title', { name: caver.name })}
      open
      onCancel={onClose}
      footer={
        <Flex gap={8} justify="flex-end" wrap>
          {onMerge && (
            <Button onClick={onMerge} data-testid="caver-held-merge">
              {t('cavers.heldByTrips.merge')}
            </Button>
          )}
          {nothingShownIsLeft && !held.heldElsewhere && (
            <Button
              danger
              type="primary"
              loading={deleteCaver.isPending}
              onClick={() => void tryDelete()}
              data-testid="caver-held-delete"
            >
              {t('cavers.heldByTrips.deleteNow')}
            </Button>
          )}
          <Button onClick={onClose}>{t('common.close')}</Button>
        </Flex>
      }
      destroyOnHidden
    >
      <Flex vertical gap={12} data-testid="caver-held-by-trips">
        <Typography.Paragraph style={{ marginBottom: 0 }}>
          {t('cavers.heldByTrips.intro')}
        </Typography.Paragraph>

        {held.trips.map((trip) => (
          <Flex
            key={trip.id}
            vertical
            gap={6}
            data-testid={`caver-held-trip-${trip.id}`}
            style={{ borderTop: '1px solid var(--ant-color-border-secondary)', paddingTop: 8 }}
          >
            <Flex gap={8} align="center" wrap>
              <Link to={`/trip-logs/${trip.id}`}>{trip.title}</Link>
              <Typography.Text type="secondary">
                {parseTripDay(trip.tripDate).toLocaleDateString(i18n.language)}
              </Typography.Text>
            </Flex>
            <Flex gap={8} align="center" wrap>
              {trip.onRoster && (
                <Tag data-testid={`caver-held-roster-${trip.id}`}>
                  {t('cavers.heldByTrips.onRoster')}
                </Tag>
              )}
              {trip.reports > 0 && (
                <Tag data-testid={`caver-held-reports-${trip.id}`}>
                  {t('cavers.heldByTrips.reports', { count: trip.reports })}
                </Tag>
              )}
            </Flex>
            {trip.onRoster && (
              <Typography.Text type="secondary">{t('cavers.heldByTrips.onRosterHelp')}</Typography.Text>
            )}
            {trip.reports > 0 &&
              (trip.reportsRemovable ? (
                <div>
                  <Button
                    danger
                    size="small"
                    icon={<DeleteOutlined />}
                    onClick={() => setRemoving(trip)}
                    data-testid={`caver-held-remove-${trip.id}`}
                  >
                    {t('trips.tracking.removeReportsOf.actionCounted', { count: trip.reports })}
                  </Button>
                </div>
              ) : (
                <Typography.Text type="secondary" data-testid={`caver-held-not-removable-${trip.id}`}>
                  {t('cavers.heldByTrips.notRemovable')}
                </Typography.Text>
              ))}
          </Flex>
        ))}

        {held.heldElsewhere && (
          <Alert
            type="warning"
            showIcon
            title={t('cavers.heldByTrips.elsewhere')}
            data-testid="caver-held-elsewhere"
          />
        )}
        {nothingShownIsLeft && !held.heldElsewhere && (
          <Alert
            type="success"
            showIcon
            title={t('cavers.heldByTrips.nothingLeft')}
            data-testid="caver-held-nothing-left"
          />
        )}
        <Typography.Text type="secondary">{t('cavers.heldByTrips.mergeHint')}</Typography.Text>
      </Flex>

      {removing && (
        <RemoveReportsOfDialog
          tripLogId={removing.id}
          caverId={caver.id}
          name={caver.name}
          count={removing.reports}
          onClose={() => setRemoving(null)}
          onRemoved={() => setHeld((current) => afterReportsRemoved(current, removing.id))}
        />
      )}
    </Modal>
  );
}
