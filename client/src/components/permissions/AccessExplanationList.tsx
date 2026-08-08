// SPDX-License-Identifier: AGPL-3.0-or-later
import { Tag, Typography } from 'antd';
import List from '../List.tsx';
import { useTranslation } from 'react-i18next';
import { explanationReason } from './accessDisplay.ts';

export interface ExplanationRow {
  action: string;
  allowed: boolean;
  source: string;
  level?: string | null;
  ruleName?: string | null;
  redacted: boolean;
}

/**
 * One verdict per action with the server's reason rendered verbatim. Redacted reasons
 * come through `explanationReason` as "a rule you cannot see decides this" — presented
 * as a normal answer, because the withholding is deliberate, not a failure.
 */
export default function AccessExplanationList({ explanations }: { explanations: ExplanationRow[] }) {
  const { t } = useTranslation();
  return (
    <List
      size="small"
      dataSource={explanations}
      renderItem={(explanation) => (
        <List.Item data-testid={`explain-${explanation.action}`}>
          <Typography.Text style={{ width: 130 }}>{t(`access.actions.${explanation.action}`)}</Typography.Text>
          <Tag color={explanation.allowed ? 'green' : 'red'}>
            {t(explanation.allowed ? 'access.allowed' : 'access.denied')}
          </Tag>
          <Typography.Text type="secondary" style={{ flex: 1 }} italic={explanation.redacted}>
            {explanationReason(t, explanation)}
          </Typography.Text>
        </List.Item>
      )}
    />
  );
}
