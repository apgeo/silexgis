// SPDX-License-Identifier: AGPL-3.0-or-later
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useCan, useCreateDoor } from '../../api/hooks.ts';
import Button from '../../components/Button.tsx';
import ReadFailedResult from '../../components/ReadFailedResult.tsx';

/** What the trip page and the trip's write-up draw when the trip's own read failed. */
export default function TripReadFailed({
  error,
  onRetry,
}: {
  error: unknown;
  onRetry: () => void;
}) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  // Whoever could have deleted a trip is who a deleted one might be waiting for: an account that
  // may delete trips, and one that may create them, since an author may delete their own.
  // Creating is asked of the door rather than of the domain-level right, as the trip list asks
  // it: somebody who records trips only for their caving group owns those trips all the same.
  const { canCreate: mayCreate } = useCreateDoor('tripLogs');
  const mayDelete = useCan('tripLogs', 'delete');
  const canRestore = mayCreate || mayDelete;

  return (
    <ReadFailedResult
      error={error}
      onRetry={onRetry}
      notFoundTitle={t('trips.notFound')}
      // A deleted trip answers exactly as one that was never there, so this page cannot know
      // which it is looking at either. What it can do is say where a deleted one would be, to
      // somebody who might have deleted it.
      notFoundDetail={
        canRestore
          ? `${t('trips.notFoundDetail')} ${t('trips.deleted.notFoundHint')}`
          : t('trips.notFoundDetail')
      }
      actions={
        <Button type="primary" onClick={() => void navigate('/trip-logs')}>
          {t('common.back')}
        </Button>
      }
      refusedActions={
        canRestore && (
          <Button
            data-testid="trip-not-found-deleted"
            onClick={() => void navigate('/trip-logs/deleted')}
          >
            {t('trips.deleted.link')}
          </Button>
        )
      }
    />
  );
}
