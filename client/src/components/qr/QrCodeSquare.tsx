// SPDX-License-Identifier: AGPL-3.0-or-later
import { CopyOutlined } from '@ant-design/icons';
import { Alert, App, Button, Flex, Input, QRCode, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { isPrintableCode } from './printedCode.ts';

interface QrCodeSquareProps {
  /** The code printed on the label — a place code, or the value derived from one. */
  code: string;
}

/**
 * The address a printed code resolves to. Deliberately built from the browser's own origin
 * rather than from configuration: the square is only useful on a label at a cave, and it has to
 * carry the address of the installation somebody is actually looking at.
 */
function qrLandingUrl(code: string): string {
  return `${window.location.origin}/q/${encodeURIComponent(code)}`;
}

/**
 * The square that goes on a label, plus the address it carries in a form somebody can copy into
 * a label designer.
 *
 * The code is shown as text under the square on purpose. A camera fails in a wet cave, in the
 * dark, behind a scratched laminate — and a person who can read the characters can type them into
 * the same address by hand, which is the whole reason these codes are short enough to transcribe.
 */
export default function QrCodeSquare({ code }: QrCodeSquareProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const url = qrLandingUrl(code);
  // Said on screen rather than left to a label already on a wall: a browser opens the escaped
  // address happily, and the scanner this square exists for does not.
  const printable = isPrintableCode(code);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(url);
      message.success(t('qr.urlCopied'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Flex vertical align="center" gap={8} data-testid="qr-code-square">
      {!printable && (
        <Alert
          type="warning"
          showIcon
          style={{ width: '100%' }}
          data-testid="qr-code-unscannable"
          title={t('qr.codeNotScannableTitle')}
          description={t('qr.codeNotScannableBody')}
        />
      )}
      <QRCode value={url} size={160} bordered={false} />
      <Typography.Text code copyable={{ text: code }}>
        {code}
      </Typography.Text>
      <Flex gap={8} style={{ width: '100%' }}>
        <Input readOnly value={url} onFocus={(e) => e.target.select()} />
        <Button icon={<CopyOutlined />} onClick={() => void copy()}>
          {t('qr.copyUrl')}
        </Button>
      </Flex>
    </Flex>
  );
}
