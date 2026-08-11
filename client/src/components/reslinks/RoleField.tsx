// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { PlusOutlined } from '@ant-design/icons';
import { App, Flex, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useDeleteResLinkMember,
  useResLinkRelationTypes,
  useResLinksForTarget,
  useUpdateResLinkMember,
  type ResLink,
  type ResLinkMember,
} from '../../api/hooks.ts';
import AddMemberModal from './AddMemberModal.tsx';
import MemberChip from './MemberChip.tsx';
import { mayEditResLink } from './permissions.ts';
import { resLinkProblemMessage } from './problems.ts';
import type { ResLinkTargetType } from './registry.ts';

/**
 * How many links of one role this field asks for. A role is normally one link, and stays
 * one in practice because a second author amends the existing link rather than opening a
 * rival one — but nothing forces that, so the bound is generous and the field says so when
 * the server counted more than it is showing.
 */
const MAX_ROWS = 200;

export interface RoleFieldProps {
  /** The entity the role is about — the end the relation reads from. */
  entityType: ResLinkTargetType;
  entityId: string;
  /** How this entity should read as a member while a link is being composed. */
  entityTitle?: string | null;
  /** The relation code this field stands for. */
  relationCode: string;
  /** The field's own title, already translated. */
  label: string;
  /** Offers the record-another action. A surface that only reports leaves it off. */
  canAdd?: boolean;
  /**
   * Draws each chip's containment path ahead of its name. Worth the room where the names
   * repeat across the installation — a work area is a passage or a sector, and the same
   * name turns up in every second cave — and noise where they do not.
   */
  showPath?: boolean;
  /**
   * Shows each member's note beside its chip, and lets whoever may curate the link write
   * it. Off by default: most roles name a thing and the name is the whole of what they
   * say, while a lead is a described continuation and unreadable without the description.
   */
  showNotes?: boolean;
}

/**
 * One role of one entity, as a labelled field: every member of every link carrying this
 * relation, as a row of chips, with a control to record another.
 *
 * Three things about it are not decoration.
 *
 * **It renders the union, not the first link.** A role is whatever the relation filter
 * returns, merged — two people recording the same role separately produce two links, and a
 * field that drew the first would silently hide the other's work. Removing a chip removes
 * that member from *its own* link, which need not be the link its neighbour belongs to, so
 * every chip carries the link it came from rather than being flattened into a bare member.
 *
 * **A member with no display is the disclosure rule working**, not a failure: the target
 * is one this caller may not read, so nothing of it travels. It reads as a restricted
 * chip, never as an error and never as something still loading.
 *
 * That is a different case from a *withheld membership*, and the two must not be confused
 * when reading this field. A member whose target this caller may not read still arrives,
 * stripped; a member the disclosure rule withholds — the position-protected feature a
 * sibling would place, which is what the entity this field belongs to can do to its own
 * link when it carries coordinates — is dropped from the response before it ever gets
 * here, because a bare row would still name which feature was being kept back. So a
 * withheld membership is a chip that is simply not there, and this field has no signal to
 * give for it: whether a link is showing everything it holds is not a question the read
 * shape lets any client answer.
 *
 * **The controls come from the link's own answer.** Whether this caller may curate a link
 * is decided by the server and travels on it; the field never re-derives it from the
 * creator, which would show no control to somebody the server would have accepted.
 */
export default function RoleField({
  entityType,
  entityId,
  entityTitle,
  relationCode,
  label,
  canAdd,
  showPath,
  showNotes,
}: RoleFieldProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [adding, setAdding] = useState(false);
  const deleteMember = useDeleteResLinkMember();
  const updateMember = useUpdateResLinkMember();

  const { data } = useResLinksForTarget(entityType, entityId, {
    relation: relationCode,
    pageSize: MAX_ROWS,
  });
  // The vocabulary is one cached list for the whole page, so a screen of ten of these
  // fields asks for it once however many of them need to know a code's row id.
  const { data: relationTypes } = useResLinkRelationTypes(canAdd ?? false);

  const links = data?.items ?? [];
  const total = data?.totalItems ?? 0;
  const relationType = (relationTypes ?? []).find((type) => type.code === relationCode);

  // The entity whose field this is is the end the relation reads from; repeating its own
  // chip in its own field would say nothing.
  const entries = links.flatMap((link) =>
    link.members
      .filter((member) => !(member.targetType === entityType && member.targetId === entityId))
      .map((member) => ({ link, member })),
  );

  // Recording another target amends a link this caller may curate, so a role stays one
  // link wherever the rules allow it. With no such link — none recorded yet, or the only
  // one belongs to somebody whose link this caller may not touch — a second link of the
  // same role is opened, and the field renders both as one union.
  const host: ResLink | undefined = links.find((link) => mayEditResLink(link));

  const remove = async (link: ResLink, memberId: string) => {
    try {
      await deleteMember.mutateAsync({ id: link.id, memberId });
    } catch (error) {
      message.error(resLinkProblemMessage(error, t));
    }
  };

  const saveNote = async (link: ResLink, member: ResLinkMember, value: string) => {
    const note = value.trim() === '' ? null : value.trim();
    if (note === (member.note ?? null)) {
      return;
    }
    try {
      await updateMember.mutateAsync({
        id: link.id,
        memberId: member.id,
        // A member write replaces the whole membership, so the marker and the order have
        // to be sent back unchanged: leaving them out would move the end the relation
        // reads from, or reshuffle the row, as a side effect of writing a sentence.
        body: { isMain: member.isMain, sortOrder: member.sortOrder, note },
      });
    } catch (error) {
      message.error(resLinkProblemMessage(error, t));
    }
  };

  const addButton = canAdd && (relationType || host) && (
    <Tag
      style={{ borderStyle: 'dashed', cursor: 'pointer', marginInlineEnd: 0 }}
      onClick={() => setAdding(true)}
    >
      <PlusOutlined /> {t('trips.roleAdd')}
    </Tag>
  );

  // Mounted only while open: the picker asks the server for a target feed, and a page
  // carrying ten of these fields should ask for none of it until somebody records
  // something.
  const dialog = adding && (
    <AddMemberModal
      open
      onClose={() => setAdding(false)}
      link={host ?? null}
      origin={
        host ? undefined : { targetType: entityType, targetId: entityId, title: entityTitle }
      }
      // Handed in even when amending an existing link, where it names no relation the
      // dialog will send: it also says the origin is the end this relation reads from, so
      // the dialog does not offer to hand the marker to the target of a role — which is
      // reachable on a link still holding nothing but the origin.
      relation={relationType ? { typeId: relationType.id, directed: relationType.directed } : null}
    />
  );

  // Nothing recorded and nothing to record with: no empty field. A field with an add
  // control still draws, so the two roles a trip is expected to state can be stated.
  if (entries.length === 0 && !addButton) {
    return null;
  }

  return (
    <Flex align="baseline" gap={8} wrap data-testid={`role-field-${relationCode}`}>
      <Typography.Text type="secondary">{label}</Typography.Text>
      <Flex align="center" gap={4} wrap>
        {entries.map(({ link, member }) => (
          <Flex key={`${link.id}:${member.id}`} align="center" gap={4}>
            <MemberChip
              member={member}
              showPath={showPath}
              onRemove={mayEditResLink(link) ? () => void remove(link, member.id) : undefined}
            />
            {/* Never beside a restricted chip: a note is an author's description of the
                thing, and describing something whose name was withheld undoes the
                withholding. Somebody who may curate the link writes it in place; everyone
                else reads it, and an empty one takes no room at all. */}
            {showNotes &&
              member.display &&
              (mayEditResLink(link) ? (
                <Typography.Text
                  type="secondary"
                  editable={{
                    // Given explicitly because the children may be the invitation to write
                    // one rather than the note, and editing an invitation would store it.
                    text: member.note ?? '',
                    tooltip: t('trips.roleNoteEdit'),
                    onChange: (value) => void saveNote(link, member, value),
                  }}
                >
                  {member.note || t('trips.roleNoteEmpty')}
                </Typography.Text>
              ) : (
                member.note && <Typography.Text type="secondary">{member.note}</Typography.Text>
              ))}
          </Flex>
        ))}
        {addButton}
      </Flex>
      {/* The count is of links, not of chips, and it counts what the server admitted —
          saying so beats a row that quietly stops short of what was recorded. */}
      {total > links.length && (
        <Typography.Text type="secondary">
          {t('trips.roleShowingFirst', { count: links.length, total })}
        </Typography.Text>
      )}
      {dialog}
    </Flex>
  );
}
