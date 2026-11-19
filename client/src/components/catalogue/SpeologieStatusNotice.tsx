// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert } from 'antd';
import { useTranslation } from 'react-i18next';
import ReadFailedAlert from '../ReadFailedAlert.tsx';

/**
 * Why the catalogue screens cannot ask the catalogue anything, when they cannot: the installation
 * has no key for it, or whether it has one could not be read. Without this a screen waits on reads
 * that are never sent — a search button that stays off, an import that spins — and says nothing.
 */
export default function SpeologieStatusNotice({
  status,
}: {
  status: { data?: { configured: boolean }; isError: boolean; error: unknown; refetch: () => unknown };
}) {
  const { t } = useTranslation();
  if (status.data && !status.data.configured) {
    return (
      <Alert
        type="warning"
        showIcon
        title={t('speologie.notConfigured.title')}
        description={t('speologie.notConfigured.body')}
        data-testid="speologie-not-configured"
      />
    );
  }
  return (
    <ReadFailedAlert
      isError={status.isError && status.data === undefined}
      error={status.error}
      held={false}
      onRetry={status.refetch}
      title={t('speologie.statusLoadFailed')}
      testId="speologie-status-failed"
      style={{ marginBottom: 0 }}
    />
  );
}
