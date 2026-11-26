// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { Flex, Spin, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

/** How long the scene may take before the loader says why it might be slow. */
export const SLOW_AFTER_MS = 15_000;

/**
 * The loader over a 3D scene being built: what is loading, and — once it has been a while — why.
 *
 * A computer without graphics acceleration draws the scene in software and can take most of a
 * minute; a bare spinner over a dark box for that long reads as a scene that will never come. A
 * browser that cannot draw 3D at all does not reach this: building the scene fails, and the view
 * says so with its own message.
 */
export default function Scene3DLoading() {
  const { t } = useTranslation();
  const [slow, setSlow] = useState(false);
  useEffect(() => {
    const timer = window.setTimeout(() => setSlow(true), SLOW_AFTER_MS);
    return () => window.clearTimeout(timer);
  }, []);

  return (
    <Flex
      vertical
      align="center"
      justify="center"
      gap="small"
      className="scene3d-loading"
      role="status"
      data-testid="scene3d-loading"
    >
      <Spin />
      <Typography.Text>{t('scene3d.loading')}</Typography.Text>
      {slow && (
        <Typography.Text type="secondary" className="scene3d-loading-slow" data-testid="scene3d-loading-slow">
          {t('scene3d.loadingSlow')}
        </Typography.Text>
      )}
    </Flex>
  );
}
