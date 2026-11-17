// SPDX-License-Identifier: AGPL-3.0-or-later
import { Badge, Card, Flex, Typography, theme } from 'antd';
import { useTranslation } from 'react-i18next';

interface RuleHit {
  ruleId: string;
  ruleName: string;
  count: number;
}

interface Props {
  hits: RuleHit[];
  unmatched: number;
  activeRule?: string;
  onPick: (ruleId: string) => void;
}

/**
 * The dry run: which rule claimed how many, before anything is created — the way a rule gets
 * debugged.
 *
 * Every rule is listed, including the ones that claimed nothing: a zero beside a rule is the
 * single most useful line here, because it is the rule that is not doing what its author
 * thought. Clicking a line filters the table to what that rule claimed.
 */
export default function RuleHitList({ hits, unmatched, activeRule, onPick }: Props) {
  const { t } = useTranslation();
  const { token } = theme.useToken();

  return (
    <Card size="small" title={t('vectorImport.dryRun')} data-testid="import-rule-hits">
      <Flex vertical gap={4}>
        {hits.map((hit) => (
          <Flex
            key={hit.ruleId}
            justify="space-between"
            align="center"
            role="button"
            tabIndex={0}
            onClick={() => onPick(hit.ruleId)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' || e.key === ' ') {
                onPick(hit.ruleId);
              }
            }}
            style={{
              cursor: 'pointer',
              paddingInline: 6,
              paddingBlock: 2,
              borderRadius: 4,
              background: activeRule === hit.ruleId ? token.colorPrimaryBg : undefined,
            }}
          >
            <Typography.Text type={hit.count === 0 ? 'secondary' : undefined}>{hit.ruleName}</Typography.Text>
            <Badge
              count={hit.count}
              showZero
              color={hit.count === 0 ? token.colorFillSecondary : token.colorPrimary}
              style={{ color: hit.count === 0 ? token.colorTextSecondary : undefined }}
            />
          </Flex>
        ))}
        <Flex
          justify="space-between"
          align="center"
          role="button"
          tabIndex={0}
          onClick={() => onPick('none')}
          onKeyDown={(e) => {
            if (e.key === 'Enter' || e.key === ' ') {
              onPick('none');
            }
          }}
          style={{
            cursor: 'pointer',
            paddingInline: 6,
            paddingBlock: 2,
            borderRadius: 4,
            borderTop: `1px solid ${token.colorSplit}`,
            background: activeRule === 'none' ? token.colorPrimaryBg : undefined,
          }}
        >
          <Typography.Text type="secondary">{t('vectorImport.noRule')}</Typography.Text>
          <Badge count={unmatched} showZero color={token.colorFillSecondary} style={{ color: token.colorTextSecondary }} />
        </Flex>
      </Flex>
    </Card>
  );
}
