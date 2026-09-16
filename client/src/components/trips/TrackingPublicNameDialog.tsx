// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { App, Button, Input, Modal, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useSetTrackingParticipantLabel, type TrackingParticipant } from '../../api/hooks.ts';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import { publicNamingOf } from './trackingPublicName.ts';
import { trackingProblemMessage } from './trackingProblems.ts';

/** The longest caption the server will store. Said here so a field cannot offer more than lands. */
const MAX_LABEL_LENGTH = 200;

interface Props {
  open: boolean;
  tripLogId: string;
  /** The person being named. The caption already stored for them rides on this row. */
  participant: TrackingParticipant;
  /** What every signed-in surface calls them, which is what this tab's table shows. */
  caverName: string;
  /** Whether this installation publishes the roster's names — the server's answer, passed down. */
  publishesRealNames: boolean;
  onClose(): void;
}

/**
 * Naming one person as a follower of the published page sees them, or taking the name back off.
 *
 * <b>This is the only mechanism by which a named person stays off a public page, so the dialog's
 * first job is to say what it does rather than to be small.</b> The installation publishes real
 * names; everybody on a followed trip is therefore named by the name the roster holds for them; and
 * a caver who does not want that has exactly one thing that can be done about it. Somebody opening
 * this on behalf of a person who asked must be able to read, on this surface, that a caption
 * replaces the name on the *public* page only, that it beats the installation's setting in both
 * directions, and that nothing else in the application starts calling anybody by it.
 *
 * <b>Clearing is saving an empty field, and that is stated rather than implied.</b> There is no
 * separate route that clears, because "call them nothing in particular" is a value this field holds
 * — so an empty caption is a write like any other. The button is offered as well, for whoever does
 * not think to empty a field and press Save, and both go the same way.
 *
 * <b>What the page will say is shown before and after.</b> A caption is typed by one person about
 * another, usually from a request made in a car park; the sentence that matters is "this page will
 * call them X", and the dialog answers it as it is typed rather than leaving it to be discovered
 * once a link has been handed out and cannot be taken back out of anybody's messages.
 */
export default function TrackingPublicNameDialog({
  open,
  tripLogId,
  participant,
  caverName,
  publishesRealNames,
  onClose,
}: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  // Typed one-handed at a cave entrance as often as at a desk, so what is pressed is sized on the
  // pointer rather than on how much room there is across.
  const coarse = useCoarsePointer();
  const save = useSetTrackingParticipantLabel();
  const [draft, setDraft] = useState(participant.label ?? '');

  const controlSize: 'large' | 'middle' = coarse ? 'large' : 'middle';

  /** How the published page would end up naming this person under whatever is in the field now. */
  const kind = publicNamingOf({ label: draft }, publishesRealNames);

  /** What the published page would call them, in the sentence the whole dialog exists to answer. */
  const naming =
    kind === 'caption'
      ? t('trips.tracking.publicName.asCaption', { caption: draft.trim() })
      : kind === 'realName'
        ? t('trips.tracking.publicName.asRealName', { name: caverName })
        : t('trips.tracking.publicName.asPlaceInParty');

  const write = async (label: string | null) => {
    try {
      await save.mutateAsync({ tripLogId, caverId: participant.caverId, label });
      message.success(
        label === null || label.trim().length === 0
          ? t('trips.tracking.publicName.cleared')
          : t('trips.tracking.publicName.saved'),
      );
      onClose();
    } catch (error) {
      message.error(trackingProblemMessage(error, t));
    }
  };

  return (
    <Modal
      open={open}
      title={t('trips.tracking.publicName.title', { name: caverName })}
      onCancel={onClose}
      // Built afresh each time it opens: a caption half-typed for one person, left standing in the
      // field when the dialog is opened on the next, is how the wrong person gets somebody's name.
      destroyOnHidden
      data-testid="trip-tracking-public-name-dialog"
      footer={[
        // Offered only where there is something to take off. A clear on a person who has no
        // caption would be a control whose whole effect is a request that changes nothing.
        participant.label !== null ? (
          <Button
            key="clear"
            size={controlSize}
            loading={save.isPending}
            onClick={() => void write(null)}
            data-testid="trip-tracking-public-name-clear"
          >
            {t('trips.tracking.publicName.clear')}
          </Button>
        ) : null,
        <Button key="cancel" size={controlSize} onClick={onClose}>
          {t('common.cancel')}
        </Button>,
        <Button
          key="save"
          type="primary"
          size={controlSize}
          loading={save.isPending}
          onClick={() => void write(draft)}
          data-testid="trip-tracking-public-name-save"
        >
          {t('common.save')}
        </Button>,
      ]}
    >
      <Typography.Paragraph>{t('trips.tracking.publicName.explain')}</Typography.Paragraph>
      {/* Both directions, said as two sentences rather than one: an administrator reaching for this
          to keep somebody off a page and an administrator reaching for it to name the one person a
          follower can ring are doing the same thing, and a dialog that only described the first
          would leave the second looking like a misuse of it. */}
      <Typography.Paragraph type="secondary">
        {t('trips.tracking.publicName.outranks')}
      </Typography.Paragraph>

      <Typography.Text type="secondary">{t('trips.tracking.publicName.fieldLabel')}</Typography.Text>
      <Input
        autoFocus
        allowClear
        size={controlSize}
        value={draft}
        maxLength={MAX_LABEL_LENGTH}
        onChange={(event) => setDraft(event.target.value)}
        placeholder={t('trips.tracking.publicName.placeholder')}
        style={{ marginTop: 4 }}
        data-testid="trip-tracking-public-name-input"
      />
      <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
        {t('trips.tracking.publicName.emptyClears')}
      </Typography.Paragraph>

      {/* The sentence the whole dialog is for, answered against what is in the field right now. */}
      <Typography.Paragraph style={{ marginTop: 12, marginBottom: 0 }}>
        <Typography.Text strong data-testid="trip-tracking-public-name-preview">
          {naming}
        </Typography.Text>
      </Typography.Paragraph>

      {/* <b>The one qualification on that sentence, and only where it applies.</b> A caption is
          printed exactly as it is typed, so under one the answer above is a fact. Without one, the
          name offered is the name *this application* calls somebody — their account's display name
          where they have set one — while the published page prints the name on the club's roster.
          They begin identical and part company the moment a member chooses a display name, so the
          answer above is a good guess and must not be read as the string. Somebody who needs it
          exact has the field right there, which is what this says to do. */}
      {kind === 'realName' && (
        <Typography.Paragraph
          type="secondary"
          style={{ marginTop: 8, marginBottom: 0 }}
          data-testid="trip-tracking-public-name-roster"
        >
          {t('trips.tracking.publicName.rosterName')}
        </Typography.Paragraph>
      )}
    </Modal>
  );
}
