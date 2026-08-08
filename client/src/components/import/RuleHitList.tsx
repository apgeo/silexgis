// SPDX-License-Identifier: AGPL-3.0-or-later
import { Badge, Card, Flex, Typography } from 'antd';
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
              background: activeRule === hit.ruleId ? 'rgba(20, 98, 98, 0.12)' : undefined,
            }}
          >
            <Typography.Text type={hit.count === 0 ? 'secondary' : undefined}>{hit.ruleName}</Typography.Text>
            <Badge
              count={hit.count}
              showZero
              color={hit.count === 0 ? '#d9d9d9' : '#146262'}
              style={{ color: hit.count === 0 ? '#595959' : undefined }}
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
            borderTop: '1px solid rgba(0,0,0,0.06)',
            background: activeRule === 'none' ? 'rgba(20, 98, 98, 0.12)' : undefined,
          }}
        >
          <Typography.Text type="secondary">{t('vectorImport.noRule')}</Typography.Text>
          <Badge count={unmatched} showZero color="#d9d9d9" style={{ color: '#595959' }} />
        </Flex>
      </Flex>
    </Card>
  );
}
