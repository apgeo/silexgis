// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import {
  ArrowDownOutlined,
  ArrowUpOutlined,
  CopyOutlined,
  DeleteOutlined,
  EditOutlined,
  ExportOutlined,
  PlusOutlined,
  StarFilled,
  StarOutlined,
} from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Card,
  Descriptions,
  Empty,
  Flex,
  Input,
  Popconfirm,
  Result,
  Select,
  Spin,
  Tag,
  Typography,
} from 'antd';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { ApiError } from '../../api/client.ts';
import {
  useDeleteResLinkMember,
  useMember,
  useMe,
  useResLink,
  useUpdateResLink,
  useUpdateResLinkMember,
  type ResLink,
  type ResLinkMember,
} from '../../api/hooks.ts';
import AddMemberModal from '../../components/reslinks/AddMemberModal.tsx';
import { useMayEditResLink } from '../../components/reslinks/permissions.ts';
import { resLinkProblemMessage } from '../../components/reslinks/problems.ts';
import RelationSelect from '../../components/reslinks/RelationSelect.tsx';
import { relationPhrase } from '../../components/reslinks/relations.ts';
import {
  anchorStateNote,
  anchorSummary,
  linkPageUrl,
  memberRoute,
  targetTypeEntry,
} from '../../components/reslinks/registry.ts';

/** Members of one target type, in the order the link records them. */
function groupByType(members: ResLinkMember[]): [string, ResLinkMember[]][] {
  const groups = new Map<string, ResLinkMember[]>();
  for (const member of members) {
    const bucket = groups.get(member.targetType);
    if (bucket) {
      bucket.push(member);
    } else {
      groups.set(member.targetType, [member]);
    }
  }
  return [...groups.entries()];
}

function MemberCard({
  link,
  member,
  editing,
  onMoved,
  canMoveUp,
  canMoveDown,
}: {
  link: ResLink;
  member: ResLinkMember;
  editing: boolean;
  onMoved: (member: ResLinkMember, direction: -1 | 1) => void;
  canMoveUp: boolean;
  canMoveDown: boolean;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const updateLink = useUpdateResLink();
  const removeMember = useDeleteResLinkMember();

  const entry = targetTypeEntry(member.targetType);
  const Icon = entry.icon;
  const summary = member.display ? anchorSummary(member.anchorKind, member.anchor, t) : null;
  const stateNote = member.display ? anchorStateNote(member.anchorState, t) : null;
  const route = memberRoute(member.targetType, member.targetId, member.display);
  const directed = Boolean(link.relationType?.directed);

  // The marker moves through the link, not the member: the server takes exactly one main
  // per directed link, so naming the new one in a single link update is the only way to
  // move it without passing through a state it would refuse.
  const makeMain = async () => {
    try {
      await updateLink.mutateAsync({
        id: link.id,
        body: {
          description: link.description,
          relationTypeId: link.relationType?.id ?? null,
          mainMemberId: member.id,
        },
      });
      message.success(t('common.saved'));
    } catch (error) {
      message.error(resLinkProblemMessage(error, t));
    }
  };

  const remove = async () => {
    try {
      await removeMember.mutateAsync({ id: link.id, memberId: member.id });
      message.success(t('common.saved'));
    } catch (error) {
      message.error(resLinkProblemMessage(error, t));
    }
  };

  return (
    <Card size="small" style={{ marginTop: 8 }}>
      <Flex gap={12} align="flex-start">
        {member.display?.thumbnailUrl ? (
          <img
            src={member.display.thumbnailUrl}
            alt=""
            style={{ width: 64, height: 64, objectFit: 'cover', borderRadius: 4 }}
          />
        ) : (
          <Icon style={{ fontSize: 24, marginTop: 4 }} />
        )}
        <Flex vertical gap={4} flex={1}>
          <Flex align="center" gap={8} wrap>
            <Typography.Text strong>
              {member.display ? member.display.title : t('resLinks.restricted')}
            </Typography.Text>
            {member.isMain && (
              <Tag icon={<StarFilled />} color="gold">
                {t('resLinks.mainMember')}
              </Tag>
            )}
            {summary && <Tag>{summary}</Tag>}
          </Flex>
          {member.display?.subtitle && (
            <Typography.Text type="secondary">{member.display.subtitle}</Typography.Text>
          )}
          {!member.display && (
            <Typography.Text type="secondary">{t('resLinks.restrictedHint')}</Typography.Text>
          )}
          {/* The note travels for a member this reader may not be told about, and is not
              shown: an author's words beside an otherwise nameless card narrow down what
              the hidden thing is, which is what withholding the display was for. */}
          {member.display && member.note && <Typography.Text>{member.note}</Typography.Text>}
          {/* How well the anchor still points at what it was measured against. An exact one
              says nothing; the rest are shown rather than silently re-aimed. */}
          {stateNote && <Alert type="warning" showIcon message={stateNote} />}
          <Flex gap={8} wrap>
            {route && (
              <Link to={route}>
                <Button size="small" icon={<ExportOutlined />}>
                  {t('resLinks.openTarget')}
                </Button>
              </Link>
            )}
            {editing && directed && !member.isMain && (
              <Button size="small" icon={<StarOutlined />} onClick={() => void makeMain()}>
                {t('resLinks.setMain')}
              </Button>
            )}
            {editing && (
              <>
                <Button
                  size="small"
                  icon={<ArrowUpOutlined />}
                  disabled={!canMoveUp}
                  aria-label={t('resLinks.moveUp')}
                  onClick={() => onMoved(member, -1)}
                />
                <Button
                  size="small"
                  icon={<ArrowDownOutlined />}
                  disabled={!canMoveDown}
                  aria-label={t('resLinks.moveDown')}
                  onClick={() => onMoved(member, 1)}
                />
                <Popconfirm
                  title={t('resLinks.removeMemberConfirm')}
                  okButtonProps={{ danger: true }}
                  onConfirm={() => void remove()}
                >
                  <Button size="small" danger icon={<DeleteOutlined />} aria-label={t('resLinks.removeMember')} />
                </Popconfirm>
              </>
            )}
          </Flex>
        </Flex>
      </Flex>
    </Card>
  );
}

/**
 * One link, in full, at the address its short code keeps for life. This is what someone
 * pasting a link into a chat sends people to, so it stands on its own: what the relation
 * says, who recorded it and when, and every member the reader is allowed to be told about.
 */
export default function LinkPage() {
  const { code = '' } = useParams();
  const { t } = useTranslation();
  const { message } = App.useApp();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const { data: me } = useMe();
  const { data: link, isLoading, isError, error } = useResLink(code);
  const { data: creator } = useMember(link?.createdBy ?? '');
  const mayEdit = useMayEditResLink(link);
  const updateLink = useUpdateResLink();
  const updateMember = useUpdateResLinkMember();

  const [editing, setEditing] = useState(false);
  const [description, setDescription] = useState('');
  const [relationTypeId, setRelationTypeId] = useState<number | null>(null);
  const [relationDirected, setRelationDirected] = useState(false);
  const [mainMemberId, setMainMemberId] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const seededFor = useRef<string | null>(null);

  // The row menu on a panel opens this page straight into edit mode — for whoever may
  // actually edit it, so a reader handed that address does not get a form that only
  // earns refusals.
  useEffect(() => {
    if (searchParams.get('edit') === '1' && mayEdit) {
      setEditing(true);
    }
  }, [searchParams, mayEdit]);

  // Seeded once per link, not on every refetch: adding a member or moving the marker
  // re-reads the link, and re-seeding there would wipe a description being typed beside
  // the very button that caused the re-read.
  useEffect(() => {
    if (link && seededFor.current !== link.id) {
      seededFor.current = link.id;
      setDescription(link.description ?? '');
      setRelationTypeId(link.relationType?.id ?? null);
      setRelationDirected(Boolean(link.relationType?.directed));
      setMainMemberId(link.members.find((member) => member.isMain)?.id ?? null);
    }
  }, [link]);

  if (isLoading) {
    return <Spin style={{ display: 'block', marginTop: '20vh' }} />;
  }

  if (isError || !link) {
    // An address that leads nowhere is news about the link; a failed read is news about
    // this attempt at it, and must not wear the same face — the page whose job is to
    // answer "does this address resolve" would otherwise say no during an outage.
    const dead =
      error instanceof ApiError && (error.status === 404 || error.code === 'reslink.code.invalid');
    return (
      <Result
        status={dead ? '404' : 'error'}
        title={dead ? t('resLinks.problems.codeUnresolved') : t('common.loadFailed')}
        subTitle={dead ? undefined : resLinkProblemMessage(error, t)}
        extra={
          <Button type="primary" onClick={() => void navigate('/')}>
            {t('common.back')}
          </Button>
        }
      />
    );
  }

  const mine = Boolean(me && link.createdBy && me.id === link.createdBy);
  const url = linkPageUrl(link.shortCode);
  const recordedAt = dayjs(link.createdAt).format('YYYY-MM-DD HH:mm');

  const copyUrl = async () => {
    try {
      await navigator.clipboard.writeText(url);
      message.success(t('resLinks.urlCopied'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  // A relation that reads one way needs the end it reads from, and one that reads both
  // ways refuses to have one — so the marker travels with the relation in a single write.
  // Sending nothing keeps whatever marker a directed link already carries, which is what
  // a link whose main member is withheld from this reader depends on.
  const savedDirected = Boolean(link.relationType?.directed);
  const directionNeedsMain =
    relationDirected && !savedDirected && link.members.length >= 2 && mainMemberId === null;

  const saveDetails = async () => {
    try {
      await updateLink.mutateAsync({
        id: link.id,
        body: {
          description: description.trim() === '' ? null : description.trim(),
          relationTypeId,
          mainMemberId: relationDirected ? mainMemberId : null,
        },
      });
      message.success(t('common.saved'));
    } catch (err) {
      message.error(resLinkProblemMessage(err, t));
    }
  };

  const ordered = [...link.members].sort((a, b) => a.sortOrder - b.sortOrder);

  /**
   * Moves a member one place inside the group it is rendered in. Two things this cannot
   * be: a swap with whatever sits next in the flat order (the reader sees members bucketed
   * by kind, so that would move a card past rows it is not next to on screen), and an
   * exchange of the two positions the pair already holds (several members can legitimately
   * hold the same number — the server breaks the tie by age — and exchanging equal numbers
   * moves nothing). So the whole visible order is written out as a run of distinct
   * positions, and only the rows whose position actually changes are written.
   */
  const move = async (member: ResLinkMember, direction: -1 | 1) => {
    const siblings = ordered.filter((candidate) => candidate.targetType === member.targetType);
    const neighbour = siblings[siblings.findIndex((c) => c.id === member.id) + direction];
    if (!neighbour) {
      return;
    }
    const moved = [...ordered];
    const from = moved.findIndex((candidate) => candidate.id === member.id);
    const to = moved.findIndex((candidate) => candidate.id === neighbour.id);
    [moved[from], moved[to]] = [moved[to], moved[from]];

    try {
      for (const [position, row] of moved.entries()) {
        if (row.sortOrder !== position) {
          await updateMember.mutateAsync({
            id: link.id,
            memberId: row.id,
            body: { isMain: row.isMain, sortOrder: position, note: row.note },
          });
        }
      }
      message.success(t('common.saved'));
    } catch (err) {
      message.error(resLinkProblemMessage(err, t));
    }
  };

  const groups = groupByType(ordered);

  return (
    <div>
      <Flex justify="space-between" align="flex-start" gap={8} wrap>
        <Typography.Title level={3} style={{ marginTop: 0 }}>
          {relationPhrase(link.relationType, 'forward', t)}
        </Typography.Title>
        <Flex gap={8} wrap>
          <Button icon={<CopyOutlined />} onClick={() => void copyUrl()}>
            {t('resLinks.copyUrl')}
          </Button>
          {mayEdit && (
            <Button
              type={editing ? 'primary' : 'default'}
              icon={<EditOutlined />}
              onClick={() => setEditing((previous) => !previous)}
            >
              {editing ? t('resLinks.doneEditing') : t('resLinks.edit')}
            </Button>
          )}
        </Flex>
      </Flex>

      <Descriptions column={1} size="small" bordered>
        <Descriptions.Item label={t('resLinks.linkAddress')}>
          <Typography.Text copyable={{ text: url }}>{url}</Typography.Text>
        </Descriptions.Item>
        <Descriptions.Item label={t('resLinks.created')}>
          {/* Who asserted the relation is half of what a pasted address has to carry. The
              creator is stored as an account id and named through the directory, which
              answers with whatever that profile lets this reader see; a link whose creator
              is gone, or whose name resolves to nothing, still says when. */}
          {mine
            ? t('resLinks.createdByYou', { when: recordedAt })
            : creator?.label
              ? t('resLinks.createdBy', { when: recordedAt, who: creator.label })
              : recordedAt}
        </Descriptions.Item>
        {!editing && link.description && (
          <Descriptions.Item label={t('resLinks.description')}>
            <Typography.Paragraph style={{ whiteSpace: 'pre-wrap', marginBottom: 0 }}>
              {link.description}
            </Typography.Paragraph>
          </Descriptions.Item>
        )}
      </Descriptions>

      {editing && (
        <Card size="small" title={t('resLinks.editDetails')} style={{ marginTop: 16 }}>
          <Flex vertical gap={8}>
            <RelationSelect
              value={relationTypeId}
              onChange={(next, directedNext) => {
                setRelationTypeId(next);
                setRelationDirected(directedNext);
                // A relation that reads both ways has no end to read from, so the marker
                // goes with it; one that reads one way starts from the marker in place.
                setMainMemberId(
                  directedNext
                    ? (mainMemberId ?? link.members.find((member) => member.isMain)?.id ?? null)
                    : null,
                );
              }}
              mainLabel={
                link.members.find((member) => member.id === mainMemberId)?.display?.title ?? null
              }
            />
            {/* Named here rather than card by card, because the relation and the end it
                reads from have to change together — a directed relation refuses to exist
                without one, and an undirected one refuses to keep it. */}
            {relationDirected && link.members.length >= 2 && (
              <Select
                value={mainMemberId ?? undefined}
                onChange={setMainMemberId}
                placeholder={t('resLinks.mainMemberPlaceholder')}
                aria-label={t('resLinks.mainMember')}
                options={link.members.map((member) => ({
                  value: member.id,
                  label: member.display ? member.display.title : t('resLinks.restricted'),
                }))}
              />
            )}
            <Input.TextArea
              value={description}
              onChange={(event) => setDescription(event.target.value)}
              maxLength={4000}
              rows={3}
              placeholder={t('resLinks.descriptionPlaceholder')}
              aria-label={t('resLinks.description')}
            />
            <Flex gap={8}>
              <Button
                type="primary"
                loading={updateLink.isPending}
                disabled={directionNeedsMain}
                onClick={() => void saveDetails()}
              >
                {t('common.save')}
              </Button>
              <Button icon={<PlusOutlined />} onClick={() => setAdding(true)}>
                {t('resLinks.addMemberTitle')}
              </Button>
            </Flex>
          </Flex>
        </Card>
      )}

      <Typography.Title level={5} style={{ marginTop: 16 }}>
        {t('resLinks.members')}
      </Typography.Title>
      {groups.length === 0 ? (
        <Empty description={t('resLinks.noVisibleMembers')} image={Empty.PRESENTED_IMAGE_SIMPLE} />
      ) : (
        groups.map(([type, members]) => (
          <div key={type} style={{ marginTop: 12 }}>
            <Typography.Text type="secondary">{t(targetTypeEntry(type).labelKey)}</Typography.Text>
            {members.map((member) => (
              <MemberCard
                key={member.id}
                link={link}
                member={member}
                editing={editing}
                onMoved={(moved, direction) => void move(moved, direction)}
                // Against the group the card is rendered in: an arrow that cannot move
                // anything the reader can see is an arrow that must not be offered.
                canMoveUp={members[0]?.id !== member.id}
                canMoveDown={members.at(-1)?.id !== member.id}
              />
            ))}
          </div>
        ))
      )}

      <AddMemberModal open={adding} onClose={() => setAdding(false)} link={link} />
    </div>
  );
}
