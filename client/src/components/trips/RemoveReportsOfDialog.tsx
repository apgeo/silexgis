// SPDX-License-Identifier: AGPL-3.0-or-later
import { App, Modal, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useRemoveTrackingReportsOf, useTrackingReportsHeldOf } from '../../api/hooks.ts';
import { trackingProblemMessage } from './trackingProblems.ts';

/**
 * The confirmation for the act somebody asks for when they ask to be removed: every report of one
 * trip about one person, gone for good — saying exactly that, and what stays.
 *
 * One dialog for both places the act is offered from — the trip's own party table, and the list a
 * refused delete of the person shows — so that the confirmation cannot come to say two different
 * things about one act. A dialog rather than a small confirmation beside the button: what it has
 * to say is a paragraph, and it is read once, on purpose, before something that cannot be undone.
 */
export function RemoveReportsOfDialog({
  tripLogId,
  caverId,
  name,
  count,
  onClose,
  onRemoved,
}: {
  tripLogId: string;
  caverId: string;
  /** The person as this reader knows them. */
  name: string;
  /**
   * How many reports will go, where the caller was told — the refusal that lists the trips counts
   * them. Left out where it was not: the party table reads a log that leaves out the reports
   * already taken off it, and a number that is too small is worse than none — so the dialog then
   * asks for the whole number itself, kept reports included, before it lets anything be confirmed.
   */
  count?: number;
  onClose: () => void;
  onRemoved?: (removed: number) => void;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const remove = useRemoveTrackingReportsOf();
  const asked = useTrackingReportsHeldOf(tripLogId, caverId, count === undefined);
  // The number the title says. Where it had to be asked for and could not be read, the act is
  // still offered — under the title that names no number, never under a guessed one.
  const known = count ?? asked.data;
  const counting = count === undefined && asked.isPending;

  const run = async () => {
    try {
      const { removed } = await remove.mutateAsync({ tripLogId, caverId });
      message.success(t('trips.tracking.removeReportsOf.done', { count: removed }));
      onRemoved?.(removed);
      onClose();
    } catch (error) {
      message.error(trackingProblemMessage(error, t, t('trips.tracking.removeReportsOf.failed')));
    }
  };

  return (
    <Modal
      open
      title={
        known === undefined
          ? t('trips.tracking.removeReportsOf.confirmTitle', { name })
          : t('trips.tracking.removeReportsOf.confirmTitleCounted', { name, count: known })
      }
      okText={t('trips.tracking.removeReportsOf.confirmOk')}
      okButtonProps={{
        danger: true,
        // Not before the number is on screen, and not at all where it is nought: there is
        // nothing to confirm, and the server would answer that there is nothing to remove.
        disabled: counting || known === 0,
        loading: counting,
        'data-testid': 'remove-reports-of-confirm',
      }}
      cancelText={t('common.cancel')}
      confirmLoading={remove.isPending}
      onOk={() => void run()}
      onCancel={onClose}
      destroyOnHidden
    >
      <Typography.Paragraph style={{ marginBottom: 0 }} data-testid="remove-reports-of-body">
        {t('trips.tracking.removeReportsOf.confirmBody')}
      </Typography.Paragraph>
    </Modal>
  );
}
