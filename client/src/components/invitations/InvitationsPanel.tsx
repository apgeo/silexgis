// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState, type ReactNode } from 'react';
import {
  DeleteOutlined,
  EyeInvisibleOutlined,
  StarFilled,
  StarOutlined,
  UserAddOutlined,
} from '@ant-design/icons';
import {
  AutoComplete,
  Button,
  Card,
  Flex,
  Input,
  Popconfirm,
  Select,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { useCavers, type TripInvitationAnswer } from '../../api/hooks.ts';
import List from '../List.tsx';
import { caverReference } from '../trips/roster.ts';

/** The longest note the server will store against an answer; a longer one is refused. */
const NOTE_MAX = 500;

/** The answers a person can choose. Not-answered is where a row starts and nobody picks it. */
const ANSWERS: readonly TripInvitationAnswer[] = ['yes', 'maybe', 'no'];

const ANSWER_COLOUR: Record<TripInvitationAnswer, string | undefined> = {
  pending: undefined,
  yes: 'success',
  maybe: 'warning',
  no: 'default',
};

/**
 * One person's standing answer, as much of it as this panel draws.
 *
 * Named by the members rather than by either subject's generated type, because the answers about
 * a trip and the answers about a club event are rows of one table read through two shapes that
 * differ only in which subject they name — and the subject is the one thing drawn here that is
 * not on the row.
 */
export interface InvitationRow {
  caverId: string;
  caverName: string;
  response: TripInvitationAnswer;
  note?: string | null;
  selectedAt?: string | null;
  place?: number | null;
  attending: boolean;
  mayAnswer: boolean;
}

/** The list as the server concluded it: the rows, and the counts a limit produced over them. */
export interface InvitationList {
  maxParticipants?: number | null;
  attendingCount: number;
  waitingCount: number;
  invitations: readonly InvitationRow[];
}

/** What one row's controls read while somebody is editing them and before they are sent. */
interface Draft {
  response: TripInvitationAnswer;
  note: string;
}

export interface InvitationsPanelProps {
  data: InvitationList;
  /** Whether this caller may put people on the list, pick them out, and take them off it. */
  canEdit: boolean;
  /**
   * Where this panel's wording lives — `trips.invitations` or `events.responses`. The two groups
   * hold the same leaf names, which is what lets one panel draw both subjects; that they do is
   * asserted where the locales themselves are checked, because a key built from a prefix is
   * invisible to a scan over literal ones.
   */
  keys: string;
  /**
   * What this panel's test hooks are named after — `trip` or `event`. Two lists on two pages that
   * shared their hooks could not be told apart by anything driving the browser.
   */
  idPrefix: string;
  /** True while an invitation is being sent, so the button can say so. */
  inviting?: boolean;
  /**
   * The four acts, each answering whether the server accepted it. Refusals are reported by the
   * caller, in the words of the subject it is about — the codes differ between the two — and this
   * panel only needs to know whether to clear what somebody was typing.
   */
  onInvite: (caverId: string) => Promise<boolean>;
  onAnswer: (caverId: string, response: TripInvitationAnswer, note: string | null) => Promise<boolean>;
  onSelect: (caverId: string, selected: boolean) => Promise<void>;
  onRemove: (caverId: string) => Promise<void>;
  /** Told when somebody was named in the picker but the text no longer names anybody the club knows. */
  onUnknownInvitee: () => void;
  /** Anything the subject offers below the list. A trip offers one act here; an event offers none. */
  footer?: ReactNode;
}

/**
 * Who has been asked about something, what each has said, and who holds one of its places.
 *
 * Almost nothing on this surface is worked out here. The order the rows arrive in *is* the order
 * people answered in; the place beside a row and whether that row holds a place or is waiting are
 * the server's conclusions about a limit and a hand-picked list; and whether this caller may write
 * an answer for a given person is a right over the subject, not something a name can be compared
 * against. Re-deriving any of them would be a second copy of a rule already enforced, free to
 * disagree with it — and disagreeing quietly, because every wrong answer still looks like an
 * answer. So the rows are rendered as received, in the order received.
 *
 * A row here is intent and never attendance. Who said they would come and who was there are
 * different facts, kept in different places on purpose, and nothing that counts who went may count
 * these rows.
 *
 * One panel draws both subjects because there is one answering mechanism behind both, in one
 * table. Two copies of this would be two places for the same rule about how an order and a limit
 * read, free to drift apart while both keep looking like answers.
 */
export default function InvitationsPanel({
  data,
  canEdit,
  keys,
  idPrefix,
  inviting = false,
  onInvite,
  onAnswer,
  onSelect,
  onRemove,
  onUnknownInvitee,
  footer,
}: InvitationsPanelProps) {
  const { t } = useTranslation();

  // The name being typed into the picker, and the person it last named. Held apart because they
  // can come apart: text edited after somebody was chosen no longer names them.
  const [invitee, setInvitee] = useState<{ caverId?: string; loadedName?: string; name: string }>({
    name: '',
  });
  const { data: cavers } = useCavers(invitee.name || undefined);

  // Only rows somebody is part way through editing. A row nobody has touched reads the server's
  // answer directly, so a list that refreshes underneath cannot take back what is being typed.
  const [drafts, setDrafts] = useState<Record<string, Draft>>({});

  const invite = async () => {
    // The one rule that decides who a row still names, called rather than restated. This form can
    // only ask somebody the club already knows, so a row that has stopped naming its person is
    // refused here instead of being sent as a name the list has no way to hold.
    const { caverId } = caverReference(invitee);
    if (!caverId) {
      onUnknownInvitee();
      return;
    }
    if (await onInvite(caverId)) {
      setInvitee({ name: '' });
    }
  };

  const answer = async (row: InvitationRow, draft: Draft) => {
    const accepted = await onAnswer(
      row.caverId,
      draft.response,
      // Explicitly nothing rather than left out: an answer given without words is an answer
      // without words, not one still wearing the words of the answer before it.
      draft.note.trim() === '' ? null : draft.note.trim(),
    );
    if (accepted) {
      setDrafts((current) => {
        const { [row.caverId]: _done, ...rest } = current;
        return rest;
      });
    }
  };

  // A name the caller may not be given arrives as no name at all — the server decides that, and
  // this only presents it. Such a row is counted and never drawn: a blank row in a list still says
  // somebody is there and where they stand in the order, which is most of what a name would have
  // said. The shortfall is a state of this surface rather than a gap in it, so it is written out
  // as a number.
  const named = data.invitations.filter((row) => row.caverName.trim() !== '');
  const withheld = data.invitations.length - named.length;

  return (
    <div data-testid={`${idPrefix}-invitations`}>
      <Card size="small" title={t(`${keys}.title`)}>
        {/* The limit and the counts as the server worked them out. A hand-picked person holds a
            place wherever they stand in the order, so these numbers cannot be recovered from the
            rows by counting them. */}
        <Typography.Paragraph type="secondary" data-testid={`${idPrefix}-invitations-limit`}>
          {data.maxParticipants == null
            ? t(`${keys}.noLimit`, { attending: data.attendingCount })
            : t(`${keys}.limit`, {
                attending: data.attendingCount,
                max: data.maxParticipants,
                waiting: data.waitingCount,
              })}
        </Typography.Paragraph>

        {canEdit && (
          <Flex gap={8} wrap style={{ marginBottom: 12 }}>
            <AutoComplete
              style={{ minWidth: 240 }}
              value={invitee.name}
              // Keyed by the person and not by their name: two people in the club's records can be
              // written down under the same name, and options keyed by the text would collide —
              // which the list resolves by dropping one of them, so one of the two becomes
              // unaskable with nothing said about it.
              options={(cavers ?? []).map((caver) => ({
                key: caver.id,
                value: caver.name,
                id: caver.id,
              }))}
              // Choosing names a person; typing after that names whoever the text names, which
              // this form cannot ask. Both are recorded and the difference is read on send.
              onSelect={(value, option) =>
                setInvitee({ caverId: (option as { id: string }).id, loadedName: value, name: value })
              }
              onChange={(value) => setInvitee((current) => ({ ...current, name: value }))}
            >
              {/* Drawn as a plain text box rather than left to the suggestion control's own, so
                  that what is on screen is the text this form reads and there is no second copy
                  of it to disagree. */}
              <Input
                data-testid={`${idPrefix}-invite-name`}
                placeholder={t(`${keys}.invitePlaceholder`)}
                onPressEnter={() => void invite()}
              />
            </AutoComplete>
            <Button
              type="primary"
              icon={<UserAddOutlined />}
              loading={inviting}
              onClick={() => void invite()}
              data-testid={`${idPrefix}-invite`}
            >
              {t(`${keys}.invite`)}
            </Button>
          </Flex>
        )}

        <List
          size="small"
          dataSource={named}
          // A list with nothing drawable in it is not the same as a list with nobody on it, and
          // saying nobody has been asked when somebody has would be a false statement rather than
          // a withheld one. Which of the two this is, is the only thing said here; how many are
          // withheld is the number below, in both cases.
          locale={{
            emptyText: t(withheld > 0 ? `${keys}.emptyWithheld` : `${keys}.empty`),
          }}
          renderItem={(row) => {
            const draft: Draft = drafts[row.caverId] ?? {
              response: row.response,
              note: row.note ?? '',
            };
            const dirty = drafts[row.caverId] !== undefined;
            return (
              <List.Item
                data-testid={`${idPrefix}-invitation-${row.caverId}`}
                actions={[
                  ...(row.mayAnswer
                    ? [
                        <Select<TripInvitationAnswer>
                          key="answer"
                          size="small"
                          style={{ width: 110 }}
                          value={draft.response}
                          data-testid={`${idPrefix}-invitation-answer-${row.caverId}`}
                          onChange={(value) =>
                            setDrafts((current) => ({
                              ...current,
                              [row.caverId]: { ...draft, response: value },
                            }))
                          }
                          options={[
                            // Not answered is where every row starts and is not a thing anybody
                            // can choose, so it is offered as a label for what is stored and
                            // never as an option — without it the control would fall back to
                            // drawing the stored value itself, in an untranslated word.
                            ...(draft.response === 'pending'
                              ? [
                                  {
                                    value: 'pending' as TripInvitationAnswer,
                                    label: t(`${keys}.responseValues.pending`),
                                    disabled: true,
                                  },
                                ]
                              : []),
                            ...ANSWERS.map((value) => ({
                              value,
                              label: t(`${keys}.responseValues.${value}`),
                              disabled: false,
                            })),
                          ]}
                        />,
                        <Input
                          key="note"
                          size="small"
                          style={{ width: 180 }}
                          maxLength={NOTE_MAX}
                          value={draft.note}
                          placeholder={t(`${keys}.notePlaceholder`)}
                          data-testid={`${idPrefix}-invitation-note-${row.caverId}`}
                          onChange={(e) =>
                            setDrafts((current) => ({
                              ...current,
                              [row.caverId]: { ...draft, note: e.target.value },
                            }))
                          }
                        />,
                        <Button
                          key="save"
                          size="small"
                          type="primary"
                          disabled={!dirty}
                          onClick={() => void answer(row, draft)}
                          data-testid={`${idPrefix}-invitation-save-${row.caverId}`}
                        >
                          {t('common.save')}
                        </Button>,
                      ]
                    : []),
                  ...(canEdit
                    ? [
                        // Picking somebody out is the organiser's override and sits beside the
                        // order rather than instead of it: the order underneath is unchanged and
                        // stays visible. The server refuses picking anybody who has not said they
                        // are coming, so the control is only offered where it would be honoured.
                        <Tooltip
                          key="select"
                          title={row.selectedAt ? t(`${keys}.deselect`) : t(`${keys}.select`)}
                        >
                          <Button
                            size="small"
                            disabled={row.response !== 'yes'}
                            icon={row.selectedAt ? <StarFilled /> : <StarOutlined />}
                            onClick={() => void onSelect(row.caverId, !row.selectedAt)}
                            data-testid={`${idPrefix}-invitation-select-${row.caverId}`}
                          />
                        </Tooltip>,
                        <Popconfirm
                          key="remove"
                          title={t(`${keys}.removeConfirm`)}
                          onConfirm={() => void onRemove(row.caverId)}
                        >
                          <Button
                            size="small"
                            danger
                            icon={<DeleteOutlined />}
                            data-testid={`${idPrefix}-invitation-remove-${row.caverId}`}
                          />
                        </Popconfirm>,
                      ]
                    : []),
                ]}
              >
                <List.Item.Meta
                  title={
                    <Flex gap={6} wrap align="center">
                      <span>{row.caverName}</span>
                      <Tag color={ANSWER_COLOUR[row.response]}>
                        {t(`${keys}.responseValues.${row.response}`)}
                      </Tag>
                      {/* Absent for anything that is not a yes, and absent rather than nought:
                          nobody holds place zero in the order people answered in. */}
                      {row.place != null && (
                        <Tag data-testid={`${idPrefix}-invitation-place-${row.caverId}`}>
                          {t(`${keys}.place`, { place: row.place })}
                        </Tag>
                      )}
                      <Tag color={row.attending ? 'blue' : undefined}>
                        {row.attending ? t(`${keys}.attending`) : t(`${keys}.waiting`)}
                      </Tag>
                      {row.selectedAt && <Tag color="gold">{t(`${keys}.selected`)}</Tag>}
                    </Flex>
                  }
                  description={row.note}
                />
              </List.Item>
            );
          }}
        />

        {withheld > 0 && (
          <Tooltip title={t(`${keys}.withheldDetail`)}>
            <Tag icon={<EyeInvisibleOutlined />} data-testid={`${idPrefix}-invitations-withheld`}>
              {t(`${keys}.withheld`, { count: withheld })}
            </Tag>
          </Tooltip>
        )}

        {footer}
      </Card>
    </div>
  );
}
