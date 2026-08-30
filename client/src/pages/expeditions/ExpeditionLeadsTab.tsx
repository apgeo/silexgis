// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Card, Empty, Flex, Spin, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import type { ExpeditionLeadGroup } from '../../api/hooks.ts';
import { useExpeditionLeads } from '../../api/hooks.ts';
import List from '../../components/List.tsx';
import { isSeededContinuationStateCode } from '../../components/expeditions/continuationStates.ts';

const stateLabel = (state: string | null | undefined, t: (key: string) => string): string => {
  if (!state) {
    return t('expeditions.leadStateUnrecorded');
  }

  return isSeededContinuationStateCode(state) ? t(`expeditions.leadStates.${state}`) : state;
};

/**
 * What the camp left open: every way on its trips found, gathered by whether it is still going.
 *
 * This is a reading of the places themselves and not a list of its own — a lead is here because a
 * member trip named a place of that kind, so it is recorded in one place and shows up in both.
 * Nothing is grouped, counted or filtered here: the server decides, per lead, whether this caller
 * may be told about it at all, and a lead they may not place exactly never arrives. Which is why
 * the board says in words that it is the board *this reader* gets — two people opening the same
 * camp see different ones, and both are right.
 *
 * No position is shown, and that is not a gap to be filled later. A list of undefended ways into
 * caves ordered by how promising they look is the same disclosure as any single coordinate and a
 * more inviting one; the place is what is being withheld, not its position, so a lead that cannot
 * be shown is absent rather than blurred.
 */
export default function ExpeditionLeadsTab({ expeditionId }: { expeditionId: string }) {
  const { t } = useTranslation();
  const { data, isPending, error } = useExpeditionLeads(expeditionId);

  if (error) {
    return <Alert type="warning" showIcon message={t('expeditions.leadsUnavailable')} />;
  }

  if (isPending || !data) {
    return <Spin />;
  }

  if (data.leads === 0) {
    return (
      <div data-testid="expedition-leads-tab">
        {/* Said deliberately rather than left as a blank pane: a camp with nothing open is a
            camp whose trips answered every question they found, which is a result, and a camp
            whose leads this reader may not be told about looks exactly the same from here. */}
        <Empty description={t('expeditions.leadsEmpty')} data-testid="expedition-leads-empty" />
        {/* The caveat belongs here more than anywhere: an empty board is the one reading where
            the reader cannot tell their own answer from everybody's, so the sentence that says
            it is theirs has to be on the screen in this branch too. */}
        <Typography.Paragraph type="secondary">{t('expeditions.leadsAsVisible')}</Typography.Paragraph>
      </div>
    );
  }

  return (
    <div data-testid="expedition-leads-tab">
      <Typography.Paragraph type="secondary" data-testid="expedition-leads-count">
        {t('expeditions.leadsCount', { count: data.leads })}
      </Typography.Paragraph>
      <Typography.Paragraph type="secondary">{t('expeditions.leadsAsVisible')}</Typography.Paragraph>
      {data.truncated && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid="expedition-leads-truncated"
          message={t('expeditions.leadsTruncated')}
        />
      )}
      <Flex vertical gap={12}>
        {data.groups.map((group: ExpeditionLeadGroup) => (
          <Card
            key={group.state ?? '(unrecorded)'}
            size="small"
            data-testid={`expedition-leads-group-${group.state ?? 'unrecorded'}`}
            title={
              <Flex align="center" gap={8}>
                <span>{stateLabel(group.state, t)}</span>
                <Tag>{group.leads.length}</Tag>
              </Flex>
            }
          >
            <List
              size="small"
              dataSource={group.leads}
              renderItem={(lead) => (
                <List.Item>
                  <List.Item.Meta
                    title={
                      <Flex align="center" gap={8}>
                        <Link to={`/features/${lead.id}`}>{lead.name || t('expeditions.leadUnnamed')}</Link>
                        {lead.grade && (
                          <Tag data-testid={`expedition-lead-grade-${lead.id}`}>
                            {t('expeditions.leadGrade', { grade: lead.grade })}
                          </Tag>
                        )}
                      </Flex>
                    }
                    description={lead.note}
                  />
                </List.Item>
              )}
            />
          </Card>
        ))}
      </Flex>
    </div>
  );
}
