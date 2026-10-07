// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, EditOutlined, PlusOutlined } from '@ant-design/icons';
import { Alert, App, Button, Empty, Flex, Popconfirm, Spin, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  useDeleteExpeditionRosterEntry,
  useExpeditionRoster,
  useExpeditionRosterRoles,
  type ExpeditionRosterEntry,
} from '../../api/hooks.ts';
import List from '../../components/List.tsx';
import { expeditionRosterRoleLabel } from '../../components/expeditions/rosterRoles.ts';
import { formatStayDates } from '../../components/expeditions/stayDates.ts';
import ExpeditionStayModal from './ExpeditionStayModal.tsx';

/**
 * The refusal this tab is built around: the camp is readable, and the caller may not read people.
 * The server answers that with a code of its own rather than with an empty roster, because a camp
 * whose roster came back blank would be indistinguishable from a camp nobody has been recorded at.
 */
const PEOPLE_UNREADABLE = 'expedition_roster.people_unreadable';

interface ExpeditionRosterTabProps {
  expeditionId: string;
  /**
   * Given only for a caller who may write the camp, which is the whole of what recording,
   * correcting and removing a stay take: a stay has no governance of its own, and the server
   * asks the camp. The controls below are drawn when this is given and on nothing else — and it
   * only decides what is offered, never what is allowed, which the server settles again on
   * every write.
   *
   * It carries the camp's own days, because they are what a new stay starts out as; asking for
   * them here is what stops the controls being switched on by a page that cannot fill the
   * dialog in. Left out, the roster is read and nothing more — which is how the camp's write-up
   * draws it, a page that is for circulating and offers no way to change what it shows.
   */
  editable?: {
    /** The camp's first day. */
    startDate: string;
    /** Its last, where it ran on past the first. */
    endDate?: string | null;
  };
}

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
 *
 * Somebody who may write the camp also keeps the roster from here: a stay is added, corrected and
 * removed in place. The withheld answer offers none of that, whoever is looking. Its reader has
 * been refused the list, and a control for writing into a list they are not shown would be a way
 * of working blind — the names it wrote could not be read back to be checked.
 */
export default function ExpeditionRosterTab({ expeditionId, editable }: ExpeditionRosterTabProps) {
  const canEdit = editable !== undefined;
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const { data, isPending, error } = useExpeditionRoster(expeditionId);
  const { data: roles } = useExpeditionRosterRoles();
  const removeStay = useDeleteExpeditionRosterEntry(expeditionId);
  // The stay the dialog is about — one being corrected, or null for a new one — and, apart from
  // it, whether the dialog is up. Kept apart so that closing leaves the dialog about what it was
  // about: cleared together, a dialog closing on a correction would retitle itself as a new stay
  // for the length of its own fade.
  const [subject, setSubject] = useState<ExpeditionRosterEntry | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const openOn = (entry: ExpeditionRosterEntry | null) => {
    setSubject(entry);
    setDialogOpen(true);
  };
  // Which stay the "remove it?" question is open on, by the stay's own identity. The list draws
  // its rows by position, so a question left to remember for itself that it is open stays with
  // the position: read the roster again a row shorter while it is up, and it is now asking
  // about whichever stay moved into that row — and removes that one. Held here, it follows the
  // stay it was asked about, and closes if that stay is no longer on the list. The rows are
  // keyed by their stay for the other half of the same reason: a row that comes to draw a
  // different stay is made again, so nothing the old one had open is left standing on the new.
  const [removing, setRemoving] = useState<number | null>(null);

  if (error instanceof ApiError && error.code === PEOPLE_UNREADABLE) {
    return (
      <Alert
        type="info"
        showIcon
        data-testid="expedition-roster-withheld"
        title={t('expeditions.rosterWithheld')}
        description={t('expeditions.rosterWithheldDetail')}
      />
    );
  }

  // Any other refusal is one this tab has no words of its own for — including the camp going away
  // between the page loading and this tab being opened. It says the roster could not be read, and
  // deliberately does not guess which of the several reasons it was.
  if (error) {
    return <Alert type="warning" showIcon title={t('expeditions.rosterUnavailable')} />;
  }

  if (isPending || !data) {
    return <Spin />;
  }

  const onRemove = async (entry: ExpeditionRosterEntry) => {
    try {
      await removeStay.mutateAsync(entry.id);
      message.success(t('common.deleted'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const roleName = (roleId: number): string => {
    const role = roles?.find((r) => r.id === roleId);
    return role ? expeditionRosterRoleLabel(role, t) : '';
  };

  const addButton = canEdit && (
    <Button
      icon={<PlusOutlined />}
      onClick={() => openOn(null)}
      data-testid="expedition-stay-add"
    >
      {t('expeditions.stay.add')}
    </Button>
  );

  const stays =
    data.entries.length === 0 ? (
      <Empty description={t('expeditions.rosterEmpty')}>{addButton}</Empty>
    ) : (
      <>
        <Flex justify="space-between" align="start" gap={12} wrap>
          <Typography.Paragraph type="secondary" data-testid="expedition-roster-people">
            {t('expeditions.rosterPeople', { count: data.people })}
          </Typography.Paragraph>
          {addButton}
        </Flex>
        <List
          size="small"
          dataSource={data.entries}
          renderItem={(entry) => (
            <List.Item
              key={entry.id}
              data-testid={`expedition-stay-${entry.id}`}
              actions={
                canEdit
                  ? [
                      <Button
                        key="edit"
                        size="small"
                        icon={<EditOutlined />}
                        title={t('expeditions.stay.edit')}
                        aria-label={t('expeditions.stay.edit')}
                        onClick={() => openOn(entry)}
                        data-testid={`expedition-stay-edit-${entry.id}`}
                      />,
                      // Confirmed first, in words that say whose stay it is and what goes with
                      // it: the stay, never the person, who is in the directory for other
                      // reasons too.
                      <Popconfirm
                        key="remove"
                        title={t('expeditions.stay.removeConfirm', { name: entry.caverName })}
                        open={removing === entry.id}
                        onOpenChange={(open) => setRemoving(open ? entry.id : null)}
                        onConfirm={() => void onRemove(entry)}
                      >
                        <Button
                          size="small"
                          danger
                          icon={<DeleteOutlined />}
                          title={t('expeditions.stay.remove')}
                          aria-label={t('expeditions.stay.remove')}
                          data-testid={`expedition-stay-remove-${entry.id}`}
                        />
                      </Popconfirm>,
                    ]
                  : undefined
              }
            >
              <List.Item.Meta
                title={
                  <>
                    {entry.caverName}
                    {roleName(entry.roleId) && (
                      <Tag style={{ marginLeft: 8 }}>{roleName(entry.roleId)}</Tag>
                    )}
                  </>
                }
                // No last day is somebody who has not left, and is said in words; one day is
                // a last day equal to the first and reads as that day, never as a range of it.
                description={formatStayDates(
                  entry.fromDate,
                  entry.toDate,
                  i18n.resolvedLanguage,
                  t,
                )}
              />
              {entry.note && <Typography.Text type="secondary">{entry.note}</Typography.Text>}
            </List.Item>
          )}
        />
      </>
    );

  return (
    <div data-testid="expedition-roster-tab">
      {stays}
      {/* One dialog, in one place under both answers above. The first stay turns "nobody yet"
          into a list while the dialog that recorded it is still closing, and a dialog drawn
          inside either answer would be thrown away and made again in the middle of that.
          Mounted only for somebody the controls were drawn for, so a reader's page carries no
          dialog at all rather than a closed one. */}
      {editable && (
        <ExpeditionStayModal
          open={dialogOpen}
          expeditionId={expeditionId}
          campStart={editable.startDate}
          campEnd={editable.endDate}
          entry={subject}
          onClose={() => setDialogOpen(false)}
        />
      )}
    </div>
  );
}
