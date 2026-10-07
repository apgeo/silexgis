// SPDX-License-Identifier: AGPL-3.0-or-later
import { CopyOutlined } from '@ant-design/icons';
import { App, Button, Flex, Input, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { publicTripPath } from '../../pages/public/publicTripEmbed.ts';

interface Props {
  /** The token of a link that was handed out a moment ago — the only copy there will ever be. */
  token: string;
  size?: 'large' | 'middle' | 'small';
  /** Prefixes the test ids of the field and its button, so two surfaces can each find their own. */
  testId: string;
}

/**
 * The address of a follow link that has just been handed out, with a button that copies it.
 *
 * Its own component because the address can be built in one place only: where a published trip
 * lives under this installation is the public page's own knowledge, and a surface outside the
 * trip's pages — the administrators' list replaces links too — has to show exactly the address
 * the trip's own panel would have shown for the same token.
 */
export default function FreshFollowLink({ token, size = 'middle', testId }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const followUrl = `${window.location.origin}${publicTripPath(token)}`;

  // The clipboard call is refused outside a secure context, which is what an installation
  // reached over plain HTTP on a club's own network is. A button that silently did nothing would
  // leave somebody believing they hold an address they do not, and this one is shown once.
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(followUrl);
      message.success(t('trips.tracking.publish.linkCopied'));
    } catch {
      message.warning(t('trips.tracking.publish.copyFailed'));
    }
  };

  return (
    <div>
      <Typography.Text type="secondary">{t('trips.tracking.publish.linkLabel')}</Typography.Text>
      <Flex gap={8} wrap style={{ marginTop: 4 }}>
        <Input
          readOnly
          value={followUrl}
          size={size}
          onFocus={(event) => event.target.select()}
          style={{ flex: '1 1 220px', minWidth: 0 }}
          data-testid={`${testId}-link`}
        />
        <Button
          size={size}
          icon={<CopyOutlined />}
          onClick={() => void copy()}
          data-testid={`${testId}-copy-link`}
        >
          {t('trips.tracking.publish.copy')}
        </Button>
      </Flex>
    </div>
  );
}
