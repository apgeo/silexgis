// SPDX-License-Identifier: AGPL-3.0-or-later
import { SettingOutlined } from '@ant-design/icons';
import { Button, Dropdown } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';

/**
 * The way from a page to the vocabularies that decide what it can say.
 *
 * Those pages moved into a configuration group in the rail, which is right — they are opened
 * twice a season and were taking up a third of a list somebody reads every day — but it puts them
 * two clicks and one guess away from the page they actually govern. Somebody looking at a trip
 * list wondering why a purpose is missing is not going to think "administration"; they are
 * looking at the thing the purpose belongs to. So the rail is where these pages are *found*, and
 * this is where they are *reached from*.
 *
 * Renders nothing at all when the caller may open none of them, rather than an empty menu. The
 * gating is the caller's, deliberately: each page already holds the capability answers it needs
 * for its own buttons, and resolving rights a second time here would be a second place for the
 * offer and the server's refusal to disagree.
 */
export interface ConfigureLinkItem {
  /** The route, less the leading slash — the same key the rail uses. */
  key: string;
  label: string;
}

export default function ConfigureLink({ items }: { items: ConfigureLinkItem[] }) {
  const { t } = useTranslation();
  const navigate = useNavigate();

  if (items.length === 0) {
    return null;
  }

  return (
    <Dropdown
      menu={{
        items: items.map((item) => ({
          key: item.key,
          label: item.label,
          onClick: () => navigate(`/${item.key}`),
        })),
      }}
    >
      <Button icon={<SettingOutlined />}>{t('common.configure')}</Button>
    </Dropdown>
  );
}
