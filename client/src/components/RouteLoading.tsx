// SPDX-License-Identifier: AGPL-3.0-or-later
import { Flex, Spin, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import './RouteLoading.css';

/**
 * What a page shows while its code arrives: a spinner with the word for it, centred in the space
 * the page will fill. A signed-in page draws it inside the application's frame, where its content
 * will go; a public page, which has no frame, draws it centred on its own. The word is in the
 * language the page is already in, so a reader of a Romanian link is not shown English first.
 *
 * Announced as a status, so a screen reader says the page is coming rather than nothing.
 */
export default function RouteLoading() {
  const { t } = useTranslation();
  return (
    <Flex vertical align="center" justify="center" gap="small" className="route-loading" role="status">
      <Spin />
      <Typography.Text type="secondary">{t('common.loading')}</Typography.Text>
    </Flex>
  );
}
