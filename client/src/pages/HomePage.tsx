// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Descriptions, Spin, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useMe } from '../api/hooks.ts';

export default function HomePage() {
  const { t } = useTranslation();
  const { data: me, isPending } = useMe();

  return (
    <Card>
      <Typography.Title level={3}>{t('home.welcome')}</Typography.Title>
      <Typography.Paragraph type="secondary">{t('home.placeholder')}</Typography.Paragraph>
      {isPending || !me ? (
        <Spin />
      ) : (
        <Descriptions column={1}>
          <Descriptions.Item label={t('home.signedInAs')}>
            {me.displayName ?? me.email} ({me.email})
          </Descriptions.Item>
          <Descriptions.Item label={t('home.roles')}>
            {me.roles.map((role) => (
              <Tag key={role}>{role}</Tag>
            ))}
          </Descriptions.Item>
        </Descriptions>
      )}
    </Card>
  );
}
