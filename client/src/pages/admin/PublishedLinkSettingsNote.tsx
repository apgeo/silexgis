// SPDX-License-Identifier: AGPL-3.0-or-later
import { Typography } from 'antd';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import type { PublishedLinks } from '../../api/hooks.ts';
import { settingSpanOf } from './publishedLinkSettings.ts';

/**
 * A period in words. One whole call per unit rather than one over a chosen key: the check that
 * every key the code asks for exists reads them out of the source text.
 */
function spanText(t: TFunction, seconds: number): string {
  const { unit, amount } = settingSpanOf(seconds);
  switch (unit) {
    case 'days':
      return t('publishedTrips.settings.span.days', { amount });
    case 'hours':
      return t('publishedTrips.settings.span.hours', { amount });
    case 'minutes':
      return t('publishedTrips.settings.span.minutes', { amount });
    default:
      return t('publishedTrips.settings.span.seconds', { amount });
  }
}

/**
 * What the statuses in the list rest on in this installation, printed once.
 *
 * <b>Why it is here.</b> The list names a link "just closed", "in the archive" or as running out
 * on a date, and says in each word's explanation that this lasts "a short while" or until "the
 * archive no longer covers its trip". How long that is, is a setting — and the person reading
 * this page is very often not the person who typed the settings. Without the figures the reader
 * cannot tell whether a row will change in an hour or in a year.
 *
 * <b>The figures are the server's, printed as given.</b> Nothing here works a status out from
 * them: a row's status is still the word the server decided. A period the installation has not
 * limited arrives as nothing and is said as "no limit", never as a large number; no grace at all
 * arrives as zero and is said as the status not being used.
 *
 * The archive's period is left out where past trips are switched off, because the line above the
 * list already says that no link is ever in the archive there.
 *
 * <b>The line about an old link is worded from three settings, not one.</b> A link whose own
 * page has ended lists today's parties only while its trip can still be read among the past
 * trips. So its own window is the whole answer only where the archive is on and keeps trips
 * without limit. With past trips switched off such a link lists nobody, whatever the window
 * says; with a period set on the archive the list ends when that period does, window or no
 * window. Printing the window alone told an administrator "no time limit" on an installation
 * where the list ends in a year, or never starts.
 */
function siblingsText(
  t: TFunction,
  settings: PublishedLinks['settings'],
  archiveEnabled: boolean,
): string {
  const window = settings.siblingWindowAfterLapseSeconds;
  if (!archiveEnabled) {
    return t('publishedTrips.settings.siblingsArchiveOff');
  }
  if (window === 0) {
    return t('publishedTrips.settings.siblingsNone');
  }
  const archiveEnds = settings.archiveRetentionSeconds != null;
  if (window == null) {
    return archiveEnds
      ? t('publishedTrips.settings.siblingsWhileArchived')
      : t('publishedTrips.settings.siblingsUnlimited');
  }
  return archiveEnds
    ? t('publishedTrips.settings.siblingsWithinArchive', { span: spanText(t, window) })
    : t('publishedTrips.settings.siblings', { span: spanText(t, window) });
}

export default function PublishedLinkSettingsNote({
  settings,
  archiveEnabled,
}: {
  settings: PublishedLinks['settings'];
  archiveEnabled: boolean;
}) {
  const { t } = useTranslation();

  return (
    <div data-testid="published-trips-settings">
      <Typography.Text strong>{t('publishedTrips.settings.title')}</Typography.Text>
      <Typography.Paragraph type="secondary">
        <ul>
          <li data-testid="published-trips-setting-lifetime">
            {t('publishedTrips.settings.lifetime', {
              span: spanText(t, settings.shareLifetimeSeconds),
            })}
          </li>
          <li data-testid="published-trips-setting-grace">
            {settings.shareGraceAfterCloseSeconds > 0
              ? t('publishedTrips.settings.grace', {
                  span: spanText(t, settings.shareGraceAfterCloseSeconds),
                })
              : t('publishedTrips.settings.graceNone')}
          </li>
          {archiveEnabled && (
            <li data-testid="published-trips-setting-retention">
              {settings.archiveRetentionSeconds == null
                ? t('publishedTrips.settings.retentionUnlimited')
                : t('publishedTrips.settings.retention', {
                    span: spanText(t, settings.archiveRetentionSeconds),
                  })}
            </li>
          )}
          <li data-testid="published-trips-setting-siblings">
            {siblingsText(t, settings, archiveEnabled)}
          </li>
          <li data-testid="published-trips-setting-read-limit">
            {t('publishedTrips.settings.readLimit', { limit: settings.publicReadsPerMinute })}
          </li>
        </ul>
      </Typography.Paragraph>
    </div>
  );
}
