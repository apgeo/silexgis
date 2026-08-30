// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Empty, Spin, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import { useExpeditionRoster, useExpeditionRosterRoles } from '../../api/hooks.ts';
import List from '../../components/List.tsx';
import { expeditionRosterRoleLabel } from '../../components/expeditions/rosterRoles.ts';
import { formatTripDates } from '../../components/trips/tripDates.ts';

/**
 * The refusal this tab is built around: the camp is readable, and the caller may not read people.
 * The server answers that with a code of its own rather than with an empty roster, because a camp
 * whose roster came back blank would be indistinguishable from a camp nobody has been recorded at.
 */
const PEOPLE_UNREADABLE = 'expedition_roster.people_unreadable';

/**
 * Who was at the camp, and for which days.
 *
 * Three answers, all of them designed:
 *
 * - the stays, with the head count the server worked out. That count is **not** the number of rows:
 *   the roster holds one row per person per role, so somebody who cooked and drove is two rows and
 *   one person, and counting rows reports a camp bigger than it was without raising anything.
 * - a refusal, when the caller may read this camp but may not read people. That is an answer, not a
 *   failure, and it is drawn as a state of the page — an installation that has taken the read over
 *   people away has asked for exactly this.
 * - nothing recorded yet, which is drawn as such so that an empty camp looks deliberate rather than
 *   broken.
 */
export default function ExpeditionRosterTab({ expeditionId }: { expeditionId: string }) {
  const { t, i18n } = useTranslation();
  const { data, isPending, error } = useExpeditionRoster(expeditionId);
  const { data: roles } = useExpeditionRosterRoles();

  if (error instanceof ApiError && error.code === PEOPLE_UNREADABLE) {
    return (
      <Alert
        type="info"
        showIcon
        data-testid="expedition-roster-withheld"
        message={t('expeditions.rosterWithheld')}
        description={t('expeditions.rosterWithheldDetail')}
      />
    );
  }

  // Any other refusal is one this tab has no words of its own for — including the camp going away
  // between the page loading and this tab being opened. It says the roster could not be read, and
  // deliberately does not guess which of the several reasons it was.
  if (error) {
    return <Alert type="warning" showIcon message={t('expeditions.rosterUnavailable')} />;
  }

  if (isPending || !data) {
    return <Spin />;
  }

  if (data.entries.length === 0) {
    return (
      <div data-testid="expedition-roster-tab">
        <Empty description={t('expeditions.rosterEmpty')} />
      </div>
    );
  }

  const roleName = (roleId: number): string => {
    const role = roles?.find((r) => r.id === roleId);
    return role ? expeditionRosterRoleLabel(role, t) : '';
  };

  return (
    <div data-testid="expedition-roster-tab">
      <Typography.Paragraph type="secondary" data-testid="expedition-roster-people">
        {t('expeditions.rosterPeople', { count: data.people })}
      </Typography.Paragraph>
      <List
        size="small"
        dataSource={data.entries}
        renderItem={(entry) => (
          <List.Item>
            <List.Item.Meta
              title={
                <>
                  {entry.caverName}
                  {roleName(entry.roleId) && <Tag style={{ marginLeft: 8 }}>{roleName(entry.roleId)}</Tag>}
                </>
              }
              // An absent end is "they did not stay on past the day they arrived", not "we do not
              // know when they left" — so a one-day stay reads as that day and never as a range of
              // itself.
              description={formatTripDates(entry.fromDate, entry.toDate, i18n.resolvedLanguage)}
            />
            {entry.note && <Typography.Text type="secondary">{entry.note}</Typography.Text>}
          </List.Item>
        )}
      />
    </div>
  );
}
