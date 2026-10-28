// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert } from 'antd';
import { useTranslation } from 'react-i18next';

/**
 * Said wherever the right to review uploads is given. A reviewer is shown every change waiting
 * for review whole — exact positions of protected caves, and pictures of caves they may not
 * otherwise read — because they cannot judge what they are not shown; so the right belongs only
 * with people trusted with all caves.
 */
export default function ReviewRightWarning() {
  const { t } = useTranslation();
  return (
    <Alert
      type="warning"
      showIcon
      data-testid="review-right-warning"
      title={t('permissions.reviewRightWarning.title')}
      description={t('permissions.reviewRightWarning.body')}
    />
  );
}
