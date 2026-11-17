// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert } from 'antd';
import { useTranslation } from 'react-i18next';
import Button from '../Button.tsx';

interface ReviewSaveFailedProps {
  /** Whether the last save of the review was refused or never arrived. */
  failed: boolean;
  /** Whether a save is on its way right now. */
  saving: boolean;
  /** Sends the last review again. */
  onRetry: () => void;
}

/**
 * The line an import review shows while its last save has failed.
 *
 * The review is saved as the reviewer works, so a failure would otherwise be invisible: the page
 * looks the same, the stored review falls behind it, and the next visit brings back the older one.
 * The page holds its confirmation while this shows, since the counts on screen are then no longer
 * the decisions the server holds. The line clears itself on the next save that succeeds.
 */
export default function ReviewSaveFailed({ failed, saving, onRetry }: ReviewSaveFailedProps) {
  const { t } = useTranslation();
  if (!failed) {
    return null;
  }
  return (
    <Alert
      type="error"
      showIcon
      style={{ marginBottom: 16 }}
      data-testid="review-save-failed"
      title={t('importReview.saveFailed')}
      description={t('importReview.saveFailedHint')}
      action={
        <Button size="small" loading={saving} onClick={onRetry}>
          {t('common.retry')}
        </Button>
      }
    />
  );
}
