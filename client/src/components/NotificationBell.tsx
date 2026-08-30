// SPDX-License-Identifier: AGPL-3.0-or-later
import { BellOutlined } from '@ant-design/icons';
import { Badge, Button, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useUnreadNotificationCount } from '../api/hooks.ts';

/**
 * How many notifications are waiting, and the way to them.
 *
 * A count, not a prompt. It says how many there are and offers no opinion about what to do with
 * them: nothing here nags, opens by itself, or grows more insistent as the number climbs. Somebody
 * who reads everything as it arrives by mail will have a bell that is never zero, and that is
 * expected rather than a state to be fixed — marking everything read, on the page this opens, is
 * the answer to it.
 *
 * Zero shows no badge at all: antd draws none without `showZero`, which is the wanted behaviour —
 * an empty inbox should look like a plain bell, not like a bell reporting nothing.
 */
export default function NotificationBell() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { token } = theme.useToken();
  const { data } = useUnreadNotificationCount();

  return (
    <Badge count={data?.unread ?? 0} size="small" overflowCount={99}>
      <Button
        type="text"
        shape="circle"
        aria-label={t('notifications.bell')}
        // The header paints itself dark, and an icon button inherits the body's text colour
        // rather than the header's, so it has to be told which one it is standing on.
        icon={<BellOutlined style={{ color: token.colorTextLightSolid, fontSize: 18 }} />}
        onClick={() => navigate('/notifications')}
      />
    </Badge>
  );
}
