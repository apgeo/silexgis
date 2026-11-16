// SPDX-License-Identifier: AGPL-3.0-or-later
import { Flex, Result } from 'antd';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { isSettledRefusal } from '../api/client.ts';
import Button from './Button.tsx';

/**
 * What a detail page draws when its own read failed and there is nothing to show instead.
 *
 * <p>Two different things reach here and they are told apart. A settled refusal is the server's
 * answer — for these pages it is the same 404 for an object that does not exist and for one the
 * caller may not read, by design — and the page's own not-found words say so. Anything else (a
 * dropped connection, a timeout, a 5xx while the server restarts) says nothing about the object,
 * so it is worded as a read that did not land, with a way to ask again. Calling that "not found"
 * would send somebody away from something that is there.</p>
 */
export default function ReadFailedResult({
  error,
  onRetry,
  notFoundTitle,
  notFoundDetail,
  actions,
  refusedActions,
}: {
  error: unknown;
  onRetry: () => void;
  notFoundTitle: string;
  notFoundDetail: string;
  /** The ways out offered in both cases, such as a Back button. */
  actions: ReactNode;
  /** Ways out that only make sense after the server's answer, such as where a deleted one would be. */
  refusedActions?: ReactNode;
}) {
  const { t } = useTranslation();

  if (!isSettledRefusal(error)) {
    return (
      <Result
        status="warning"
        data-testid="read-failed"
        title={t('common.readFailedTitle')}
        subTitle={t('common.readFailedDetail')}
        extra={
          <Flex gap={8} justify="center" wrap>
            <Button data-testid="read-failed-retry" onClick={onRetry}>
              {t('common.retry')}
            </Button>
            {actions}
          </Flex>
        }
      />
    );
  }

  return (
    <Result
      status="404"
      title={notFoundTitle}
      subTitle={notFoundDetail}
      extra={
        <Flex gap={8} justify="center" wrap>
          {actions}
          {refusedActions}
        </Flex>
      }
    />
  );
}
