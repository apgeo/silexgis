// SPDX-License-Identifier: AGPL-3.0-or-later
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import Button from '../../components/Button.tsx';
import ReadFailedResult from '../../components/ReadFailedResult.tsx';

/**
 * What the camp page and the camp's write-up draw when the camp's own read failed. The server's
 * refusal is the same for a camp that is not there and for one the reader is not admitted to, so
 * the not-found words never claim to know which of the two it is.
 */
export default function ExpeditionReadFailed({
  error,
  onRetry,
}: {
  error: unknown;
  onRetry: () => void;
}) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  return (
    <ReadFailedResult
      error={error}
      onRetry={onRetry}
      notFoundTitle={t('expeditions.notFound')}
      notFoundDetail={t('expeditions.notFoundDetail')}
      actions={
        <Button type="primary" onClick={() => void navigate('/expeditions')}>
          {t('common.back')}
        </Button>
      }
    />
  );
}
