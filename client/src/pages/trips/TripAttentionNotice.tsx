// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, App, Tag } from 'antd';
import Button from '../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import { useClearTripAttention, type TripAttention } from '../../api/hooks/recordings.ts';

interface Props {
  tripId: string;
  attention: TripAttention;
  /** Whether the reader may change the trip: only they may say it has been looked at. */
  canEdit: boolean;
}

/** The reasons the conversion can raise, each with the words it is shown under. */
const KNOWN_REASONS = new Set([
  'place_unknown',
  'unresolved_station',
  'unmapped_person',
  'recorded_in_future',
  'recording_changed_after_conversion',
]);

/**
 * Says, in plain words, why a trip a phone recording was made into needs somebody to look at it,
 * and lets somebody who may change the trip say they have.
 *
 * Drawn for everybody who reads the trip, because the reasons are codes and counts that place
 * nothing, and the button only for those who may change it. Once cleared the reasons stay on
 * screen as a quiet note: a trip somebody looked at still reads as one that once needed it.
 */
export default function TripAttentionNotice({ tripId, attention, canEdit }: Props) {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const clear = useClearTripAttention();

  const markResolved = async () => {
    try {
      await clear.mutateAsync(tripId);
      message.success(t('tripAttention.resolved'));
    } catch (error) {
      const code = error instanceof ApiError ? error.code : undefined;
      if (code === 'trip_attention.changed') {
        message.error(t('tripAttention.errors.changed'));
      } else if (error instanceof ApiError && error.status === 403) {
        message.error(t('tripAttention.errors.forbidden'));
      } else {
        message.error(t('tripAttention.errors.failed'));
      }
    }
  };

  const raised = attention.raised;
  return (
    <Alert
      type={raised ? 'warning' : 'info'}
      showIcon
      style={{ marginBottom: 12 }}
      data-testid={raised ? 'trip-attention-raised' : 'trip-attention-cleared'}
      title={t(raised ? 'tripAttention.title' : 'tripAttention.titleCleared')}
      description={
        <>
          <p style={{ marginBottom: 8 }}>
            {raised
              ? t('tripAttention.intro')
              : t('tripAttention.clearedAt', {
                  when: attention.clearedAt
                    ? new Date(attention.clearedAt).toLocaleString(i18n.resolvedLanguage)
                    : '',
                })}
          </p>
          <ul style={{ margin: 0, paddingInlineStart: 0, listStyle: 'none' }}>
            {attention.reasons.map((reason) => (
              <li key={reason.code} style={{ paddingBlock: 2 }} data-testid={`trip-attention-${reason.code}`}>
                <Tag>{reason.count}</Tag>
                {KNOWN_REASONS.has(reason.code)
                  ? t(`tripAttention.reasons.${reason.code}`)
                  : t('tripAttention.unknownReason')}
              </li>
            ))}
          </ul>
        </>
      }
      action={
        raised && canEdit ? (
          <Button
            size="small"
            loading={clear.isPending}
            onClick={() => void markResolved()}
            data-testid="trip-attention-clear"
          >
            {t('tripAttention.markResolved')}
          </Button>
        ) : undefined
      }
    />
  );
}
