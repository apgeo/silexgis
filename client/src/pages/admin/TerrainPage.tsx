// SPDX-License-Identifier: AGPL-3.0-or-later
import { GlobalOutlined } from '@ant-design/icons';
import { Empty, Flex, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

/**
 * The elevation surface an installation has built for itself. The sidebar offers this
 * destination to whoever holds the terrain right, and a router this application's shape
 * has no fallback route: a path nothing matches does not land on an empty page, it
 * replaces the whole shell with the router's own error screen and leaves no way back
 * except the browser's history. So the destination exists from the moment the entry
 * does; the list of builds fills it in.
 */
export default function TerrainPage() {
  const { t } = useTranslation();

  return (
    <Flex vertical gap={16} style={{ padding: 16 }}>
      <Typography.Title level={4} style={{ margin: 0 }}>
        <GlobalOutlined /> {t('terrain.title')}
      </Typography.Title>
      <Empty description={t('terrain.noBuilds')} />
    </Flex>
  );
}
