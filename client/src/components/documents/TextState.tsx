// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import {
  CheckCircleOutlined,
  ExclamationCircleOutlined,
  FileImageOutlined,
  MinusCircleOutlined,
  QuestionCircleOutlined,
  SyncOutlined,
} from '@ant-design/icons';
import { Flex, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { TextExtractionState } from '../../api/hooks.ts';

/**
 * How each state of the reading is drawn, beside the sentence that names it.
 *
 * Keyed by the state union rather than by string, so a state added on the server fails to
 * compile here instead of rendering as a blank badge.
 */
const textStates: Record<
  TextExtractionState,
  { icon: ReactNode; tone: 'success' | 'secondary' | 'warning' | 'danger' }
> = {
  extracted: { icon: <CheckCircleOutlined />, tone: 'success' },
  pending: { icon: <SyncOutlined spin />, tone: 'secondary' },
  noText: { icon: <FileImageOutlined />, tone: 'warning' },
  notApplicable: { icon: <MinusCircleOutlined />, tone: 'secondary' },
  unsupported: { icon: <QuestionCircleOutlined />, tone: 'secondary' },
  failed: { icon: <ExclamationCircleOutlined />, tone: 'danger' },
};

/**
 * Says what happened when the document's text was read, in a sentence rather than a badge.
 *
 * This is stated for every document, including the ones with nothing in them, because the
 * two silences are indistinguishable otherwise: a document nobody has read yet and a
 * scanned one that will never have words both show as a document with no text. Leaving
 * that unsaid is how someone waits for a search result that is never coming.
 *
 * It lives on its own so the document's page and the properties panel say the same thing
 * in the same words — a second vocabulary for the same six answers would drift.
 */
export default function TextState({
  state,
  fontSize = 12,
}: {
  state: TextExtractionState;
  fontSize?: number;
}) {
  const { t } = useTranslation();
  const { icon, tone } = textStates[state];
  return (
    <Flex gap={6} align="baseline">
      <Typography.Text type={tone} style={{ fontSize }}>
        {icon}
      </Typography.Text>
      <Typography.Text type={tone} style={{ fontSize }}>
        {t(`documents.textStates.${state}`)}
      </Typography.Text>
    </Flex>
  );
}
