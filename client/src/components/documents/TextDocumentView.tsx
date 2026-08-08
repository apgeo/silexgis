// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Flex, Skeleton, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { maxInlineTextBytes, useFileText, type FileInfo } from '../../api/hooks.ts';

/**
 * A text document shown as what it is.
 *
 * Marked-up text — notes in Markdown, a delimited table — is shown as its source rather than
 * as the thing the markup describes. That is a deliberate limit and not an oversight: turning
 * markup into formatting means shipping something that parses it, and the source of a set of
 * field notes is legible in a way the source of most formats is not. What matters is that the
 * words are on the screen and searchable, which they are.
 */
export default function TextDocumentView({ file }: { file: FileInfo }) {
  const { t } = useTranslation();
  const { data, isPending, isError } = useFileText(file);

  if (isError) {
    return <Alert type="error" showIcon title={t('documents.viewer.textFailed')} />;
  }

  if (isPending || !data) {
    return <Skeleton active paragraph={{ rows: 6 }} />;
  }

  return (
    <Flex vertical gap={8} style={{ width: '100%', minWidth: 0 }}>
      <Typography.Paragraph
        style={{
          whiteSpace: 'pre-wrap',
          // Long unbroken lines — a wide delimited table, a pasted path — must scroll inside
          // this panel rather than widen the page under them.
          overflowWrap: 'anywhere',
          fontFamily: 'monospace',
          fontSize: 13,
          margin: 0,
          maxHeight: '70vh',
          overflow: 'auto',
        }}
      >
        {data.text}
      </Typography.Paragraph>
      {data.truncated && (
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {t('documents.viewer.textTruncated', {
            kilobytes: Math.round(maxInlineTextBytes / 1024),
          })}
        </Typography.Text>
      )}
    </Flex>
  );
}
