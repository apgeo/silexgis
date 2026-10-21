// SPDX-License-Identifier: AGPL-3.0-or-later
import { Result, Typography } from 'antd';
import Button from '../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate } from 'react-router-dom';

/**
 * An address this application does not answer: a mistyped link, a bookmark to a page that has
 * moved, a link from an older version. Shown inside the application's own frame, so the
 * navigation is still there and the way back is one click rather than a retyped address.
 */
export default function NotFoundPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { pathname } = useLocation();
  return (
    <Result
      status="404"
      title={t('notFoundPage.title')}
      subTitle={t('notFoundPage.description')}
      extra={
        <Button type="primary" onClick={() => void navigate('/')}>
          {t('notFoundPage.home')}
        </Button>
      }
    >
      {/* The address as it arrived, so somebody reporting a broken link can say which one. */}
      <Typography.Paragraph type="secondary" style={{ marginBottom: 0, textAlign: 'center' }}>
        <Typography.Text code>{pathname}</Typography.Text>
      </Typography.Paragraph>
    </Result>
  );
}
