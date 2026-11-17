// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Flex, Typography } from 'antd';
import type { CSSProperties } from 'react';
import { useTranslation } from 'react-i18next';
import { isSettledRefusal } from '../api/client.ts';
import Button from './Button.tsx';

/**
 * The inline counterpart of the whole-page read failure: what a list, a section or a panel draws
 * when its own read failed, while the rest of the screen stays usable.
 *
 * <p>A read that did not land is not an empty answer. Saying "nothing here" after a dropped
 * connection or a 5xx tells the reader something false, and on a page that creates things it
 * invites a duplicate. So the failure is named, with a way to ask again. When an earlier answer
 * is still on screen — TanStack Query keeps it when a later re-read fails — that answer stays and
 * the notice only says it may be out of date.</p>
 *
 * <p>A settled refusal is told apart, exactly as the whole-page variant does: it is the server's
 * answer, not a blip, and the query layer has already stopped asking again. Offering Retry there
 * would repeat the same refusal, and "could not be read just now" would contradict it. So it is
 * worded as a refusal — the caller's own words when it has them — and offers no Retry.</p>
 *
 * <p>Renders nothing while the read has not failed, so a screen can place it unconditionally.</p>
 */
export default function ReadFailedAlert({
  isError,
  error,
  held,
  onRetry,
  title,
  refusedTitle,
  testId = 'read-failed-inline',
  style,
}: {
  isError: boolean;
  /** The failure itself, so a settled refusal is not offered as something to try again. */
  error: unknown;
  /** Whether an earlier answer is still on screen. */
  held: boolean;
  onRetry: () => unknown;
  /** What could not be read, when nothing is held; defaults to a general sentence. */
  title?: string;
  /** What to say when the server refused the read; defaults to a general sentence. */
  refusedTitle?: string;
  testId?: string;
  style?: CSSProperties;
}) {
  const { t } = useTranslation();
  if (!isError) {
    return null;
  }
  if (isSettledRefusal(error)) {
    return (
      <Alert
        type="error"
        showIcon
        data-testid={testId}
        data-refused="true"
        style={{ marginBottom: 12, ...style }}
        title={refusedTitle ?? (held ? t('common.readRefusedHeld') : t('common.readRefused'))}
      />
    );
  }
  return (
    <Alert
      type="warning"
      showIcon
      data-testid={testId}
      style={{ marginBottom: 12, ...style }}
      title={held ? t('common.refreshFailed') : (title ?? t('common.loadFailed'))}
      action={
        <Button size="small" onClick={() => void onRetry()}>
          {t('common.retry')}
        </Button>
      }
    />
  );
}

/**
 * The same failure in a document meant to be printed: a plain line in the document's own words,
 * because a printed write-up that says "none" after a read that never landed is a false record.
 * The Retry beside it is for the screen and is left off the paper.
 */
export function ReadFailedLine({
  error,
  onRetry,
  text,
  testId,
}: {
  error: unknown;
  onRetry: () => unknown;
  /** What the document says in place of what could not be read. */
  text: string;
  testId: string;
}) {
  const { t } = useTranslation();
  return (
    <Flex align="center" gap={8} wrap data-testid={testId}>
      <Typography.Text type="warning">{text}</Typography.Text>
      {!isSettledRefusal(error) && (
        <Button size="small" className="trip-report-noprint" onClick={() => void onRetry()}>
          {t('common.retry')}
        </Button>
      )}
    </Flex>
  );
}
