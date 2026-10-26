// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert } from 'antd';
import { useTranslation } from 'react-i18next';
import { formatSize } from '../attachments/fileFormat.ts';
import type { SurveyUpload } from '../../api/hooks.ts';

/**
 * Says that a survey's older versions take a lot of room, how much, and which installation setting
 * keeps fewer of them.
 *
 * Every version keeps what its run produced unless the installation says otherwise, and nothing is
 * thrown away without being asked — so the size is only shown past the size the installation
 * warns at, and it names the setting rather than offering a button: the choice is the
 * administrator's, made in the server's environment.
 */
export default function OlderVersionsWarning({
  olderVersions,
  surveyTitle,
}: {
  olderVersions: SurveyUpload['olderVersions'];
  /** Names the survey, where the warning stands among several (the cave's list of surveys). */
  surveyTitle?: string;
}) {
  const { t } = useTranslation();
  if (!olderVersions.warn) {
    return null;
  }
  const keep =
    olderVersions.keepVersions === 0
      ? t('surveyUploads.page.olderVersionsKeepAll')
      : t('surveyUploads.page.olderVersionsKeepNewest', { count: olderVersions.keepVersions });
  return (
    <Alert
      type="warning"
      showIcon
      data-testid="survey-upload-older-versions-warning"
      title={t(surveyTitle ? 'surveyUploads.page.olderVersionsTitleNamed' : 'surveyUploads.page.olderVersionsTitle', {
        size: formatSize(olderVersions.sizeBytes),
        count: olderVersions.count,
        survey: surveyTitle,
      })}
      description={t('surveyUploads.page.olderVersionsHint', {
        keep,
        setting: 'SILEXGIS__SurveyUploads__KeepArtefactVersions',
      })}
    />
  );
}
