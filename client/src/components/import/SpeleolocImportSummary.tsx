// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Card, Flex, Statistic, Typography, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import type { SpeleolocImportPreview } from '../../api/hooks.ts';

/**
 * The things the confirmation demands that the dry run cannot refuse on its own.
 *
 * Each one is a refusal the server answers with at the moment the button is pressed — no survey
 * model is a conflict, no destination is a bad request, a model this account cannot place is a
 * conflict — and each is knowable before the press. They are listed as sentences above the button
 * rather than discovered underneath it, because the whole promise of a dry run is that what it
 * offers is what the confirmation will do.
 */
export type SpeleolocBlocker =
  | 'recording'
  // An archive that holds no recording at all. Its own line rather than the one above, because
  // "choose a recording" in front of an empty list is an instruction nobody can carry out, and a
  // reviewer told to do the impossible concludes the screen is broken.
  | 'archiveEmpty'
  | 'destination'
  | 'model'
  | 'modelUnusable'
  | 'selection';

interface Props {
  preview: SpeleolocImportPreview | undefined;
  takenCount: number;
  blockers: readonly SpeleolocBlocker[];
  /** Whether the confirmation would make the trip, which decides what the note below can promise. */
  createTrip: boolean;
}

/**
 * What confirming would do, and what still stands in its way.
 *
 * Every figure is drawn at zero as well as above it: a count that disappears when it is nothing
 * reads exactly like a count that was never there, and "nothing is unresolved" is a different
 * statement from "nobody looked".
 */
export default function SpeleolocImportSummary({
  preview,
  takenCount,
  blockers,
  createTrip,
}: Props) {
  const { t } = useTranslation();
  const { token } = theme.useToken();

  return (
    <Card size="small" title={t('speleolocImport.summaryTitle')} data-testid="speleoloc-import-summary">
      <Flex gap={24} wrap style={{ marginBottom: 8 }}>
        <Statistic title={t('speleolocImport.scans')} value={preview?.totalItems ?? 0} />
        <Statistic title={t('speleolocImport.proposed')} value={preview?.proposedCount ?? 0} />
        <Statistic
          title={t('speleolocImport.unresolved')}
          value={preview?.unresolvedCount ?? 0}
          styles={
            (preview?.unresolvedCount ?? 0) > 0 ? { content: { color: token.colorError } } : undefined
          }
        />
        <Statistic title={t('speleolocImport.taken')} value={takenCount} />
      </Flex>

      {blockers.length > 0 && (
        <Alert
          type="error"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid="speleoloc-import-blockers"
          title={t('speleolocImport.blockersTitle')}
          description={
            <ul style={{ marginBottom: 0, paddingInlineStart: 20 }}>
              {blockers.map((blocker) => (
                <li key={blocker} data-testid={`speleoloc-import-blocker-${blocker}`}>
                  {t(`speleolocImport.blockers.${blocker}`)}
                </li>
              ))}
            </ul>
          }
        />
      )}

      {(preview?.unresolvedCount ?? 0) > 0 && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid="speleoloc-import-unresolved-notice"
          title={t('speleolocImport.unresolvedTitle', { count: preview?.unresolvedCount ?? 0 })}
          description={t('speleolocImport.unresolvedBody')}
        />
      )}

      <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
        {createTrip ? t('speleolocImport.createTripNote') : t('speleolocImport.existingTripNote')}
      </Typography.Paragraph>
      <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
        {t('speleolocImport.undoHint')}
      </Typography.Paragraph>
    </Card>
  );
}
