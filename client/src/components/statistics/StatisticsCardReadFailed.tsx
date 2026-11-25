// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card } from 'antd';
import { useTranslation } from 'react-i18next';
import { isSettledRefusal } from '../../api/client.ts';
import ReadFailedAlert from '../ReadFailedAlert.tsx';

/**
 * What a figures card draws in place of itself when its read failed.
 *
 * <p>Two failures look alike to the query layer and must not to the reader. A settled refusal is
 * the server's answer: the caller may not read the subject, may not place the cave exactly, or the
 * figures do not apply. A card of blanks there would be a claim about the cave — and for a cave
 * whose location is withheld it would announce that something is there — so nothing is drawn, as
 * before. Any other failure (a dropped connection, a server error) says nothing about the cave, so
 * the card says that its figures could not be read and offers to ask again, rather than vanishing
 * and letting the reader take the page for complete.</p>
 */
export default function StatisticsCardReadFailed({
  title,
  error,
  onRetry,
  testId,
}: {
  /** The card's own title, so the reader knows which figures are missing. */
  title: string;
  error: unknown;
  onRetry: () => unknown;
  testId: string;
}) {
  const { t } = useTranslation();
  if (isSettledRefusal(error)) {
    return null;
  }
  return (
    <Card size="small" title={title} style={{ marginBottom: 16 }} data-testid={testId}>
      <ReadFailedAlert
        isError
        error={error}
        held={false}
        onRetry={onRetry}
        title={t('statistics.cardReadFailed')}
        style={{ marginBottom: 0 }}
      />
    </Card>
  );
}
