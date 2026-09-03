// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import {
  App,
  Alert,
  Checkbox,
  Flex,
  Input,
  InputNumber,
  Select,
  Tooltip,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useAddResLinkMember,
  useCreateResLink,
  useDeleteResLink,
  useResLinkPointDefault,
  useUpdateResLink,
  type AnchorKind,
  type ResLink,
  type ResLinkMemberAdd,
  type Visibility,
} from '../../api/hooks.ts';
import DialogHost from '../DialogHost.tsx';
import PointField from '../settings/PointField.tsx';
import { resLinkProblemMessage } from './problems.ts';
import RelationSelect from './RelationSelect.tsx';
import ResLinkTargetPicker from './ResLinkTargetPicker.tsx';
import {
  admittedAnchorKinds,
  anchorKindEntry,
  anchorProblem,
  canComposeAnchor,
  RESLINK_TARGET_TYPES,
  targetTypeEntry,
  type ResLinkTargetType,
} from './registry.ts';

/** The entity a link flow started from — the first member of a link the modal creates. */
export interface LinkOrigin {
  targetType: ResLinkTargetType;
  targetId: string;
  title?: string | null;
  /**
   * Which part of the origin the link is about, when the flow started from a part rather than
   * from the whole thing — a passage somebody dragged across in an annotated text, and later a
   * region drawn on a page.
   *
   * Composed by the caller rather than in this dialog, and deliberately: a passage is chosen by
   * selecting it in the text, which has already happened by the time this opens. The anchor
   * editors here are for the member being *added*, which is a different question.
   *
   * Absent means the whole resource, which is what every other flow into this dialog means.
   */
  anchor?: { anchorKind: AnchorKind; anchor: unknown; anchorFileId?: string | null } | null;
}

/** The origin's own member row, whole or anchored, written once for both create paths. */
function originMember(origin: LinkOrigin, isMain: boolean, sortOrder: number) {
  return {
    targetType: origin.targetType,
    targetId: origin.targetId,
    isMain,
    sortOrder,
    note: null,
    anchorKind: origin.anchor?.anchorKind ?? ('whole' as AnchorKind),
    anchor: origin.anchor?.anchor ?? null,
    anchorFileId: origin.anchor?.anchorFileId ?? null,
  };
}

interface Props {
  open: boolean;
  onClose: () => void;
  /** The link being extended. Absent means the modal creates a new link from `origin`. */
  link?: ResLink | null;
  /** Required when creating: the member the new link starts from. */
  origin?: LinkOrigin;
  /**
   * The relation a new link must carry, when the flow already knows it — a field that
   * stands for one role opens this dialog to record another target of that role, so the
   * relation is not a question and the origin is the end the relation reads from. Both
   * controls that would ask are left out rather than shown pre-filled and disabled: a
   * disabled control invites the reader to work out why it cannot be changed.
   */
  relation?: { typeId: number; directed: boolean } | null;
  onCreated?: (link: ResLink) => void;
}

/** Shortest query worth a round trip, matching the app's other pickers. */
/** Server limit on a member note; saying so here beats a refusal after the round trip. */
const NOTE_MAX = 2000;

const pointVisibilities: Visibility[] = ['private', 'cavingGroup', 'authenticated', 'public'];

export default function AddMemberModal({
  open,
  onClose,
  link,
  origin,
  relation,
  onCreated,
}: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const createLink = useCreateResLink();
  const updateLink = useUpdateResLink();
  const deleteLink = useDeleteResLink();
  const addMember = useAddResLinkMember();

  const creating = !link;

  const [targetType, setTargetType] = useState<ResLinkTargetType>('feature');
  const [source, setSource] = useState<'existing' | 'newPoint'>('existing');
  const [targetId, setTargetId] = useState<string | null>(null);
  const [targetTitle, setTargetTitle] = useState<string | null>(null);
  const [anchorKind, setAnchorKind] = useState<AnchorKind>('whole');
  const [anchor, setAnchor] = useState<unknown>(null);
  /**
   * The file a region's fractions were measured against, set by the editor that composes one.
   * Only the region kind has it; every other editor leaves it null, which is what those kinds
   * require and what the server checks.
   */
  const [anchorFileId, setAnchorFileId] = useState<string | null>(null);
  const [note, setNote] = useState('');
  const [isMain, setIsMain] = useState(false);
  const presetRelationTypeId = relation?.typeId ?? null;
  const presetRelationDirected = relation?.directed ?? false;
  const [relationTypeId, setRelationTypeId] = useState<number | null>(presetRelationTypeId);
  const [relationDirected, setRelationDirected] = useState(presetRelationDirected);
  const [point, setPoint] = useState<[number, number] | null>(null);
  // Kept apart from the picked position rather than folded into it: the map picker deals in
  // lon/lat and is shared with flows that have no notion of height, so height is stated
  // beside it and travels with the point only when someone actually knew one.
  const [altitude, setAltitude] = useState<number | null>(null);
  const [pointName, setPointName] = useState('');
  const [pointVisibility, setPointVisibility] = useState<Visibility | 'default'>('default');

  // Every open starts clean: a half-composed member left over from a dialog the user backed
  // out of would be submitted against whatever they opened next.
  useEffect(() => {
    if (open) {
      setTargetType('feature');
      setSource('existing');
      setTargetId(null);
      setTargetTitle(null);
      setAnchorKind('whole');
      setAnchor(null);
      setAnchorFileId(null);
      setNote('');
      setRelationTypeId(presetRelationTypeId);
      setRelationDirected(presetRelationDirected);
      setPoint(null);
      setAltitude(null);
      setPointName('');
      setPointVisibility('default');
      // A directed link needs exactly one main member, and the one the user is standing on
      // is the natural place to read it from — so a new member is not it by default.
      setIsMain(false);
    }
  }, [open, presetRelationTypeId, presetRelationDirected]);

  // Who the point will be visible to is the server's rule applied to this caller's own
  // group roster — a fact only the server holds — so it is asked for rather than guessed,
  // and asked for only once the user is actually placing a point.
  const { data: pointDefault, isError: audienceUnknown } = useResLinkPointDefault(
    open && source === 'newPoint',
  );

  const anchorKinds = admittedAnchorKinds(targetType);
  const editorEntry = anchorKindEntry(anchorKind);
  const AnchorEditor = editorEntry?.editor ?? null;

  // A directed relation reads from one end, so exactly one member carries the marker. On an
  // existing link the marker is already placed unless the link is still a single member, and
  // moving it is the link page's job — offering it here would only earn a refusal.
  // A relation handed in fixes which end reads it: the origin. Offering the marker there
  // would offer a way to record a role backwards, which the field it was opened from could
  // then not draw.
  const existingMain = link?.members.some((member) => member.isMain) ?? false;
  const directed = creating ? relationDirected : Boolean(link?.relationType?.directed);
  const mainAvailable = directed && !existingMain && !relation;
  const mainDisabledReason = !directed
    ? t('resLinks.mainNeedsDirected')
    : existingMain
      ? t('resLinks.mainAlreadySet')
      : null;

  const anchorMessage = anchorProblem(anchorKind, anchor, t);
  const targetChosen = source === 'newPoint' ? point !== null : targetId !== null;
  const canSubmit = targetChosen && anchorMessage === null && note.length <= NOTE_MAX;

  // Who the point ends up visible to, named rather than described. An explicit choice
  // states itself; the default is whatever the server just said it would be for this
  // caller. Two cases are deliberately not silent-by-omission: while the answer is on its
  // way there is nothing to name yet, but if it cannot be had at all the form says so —
  // placing a cave position with no idea who will see it is the thing to avoid.
  const audienceNotice =
    pointVisibility !== 'default'
      ? t(`resLinks.point.audience.${pointVisibility}`)
      : pointDefault === undefined
        ? audienceUnknown
          ? t('resLinks.point.audienceUnknown')
          : null
        : pointDefault.cavingGroupName
          ? t('resLinks.point.audienceDefaultGroup', { group: pointDefault.cavingGroupName })
          : pointDefault.visibility === 'authenticated'
            ? t('resLinks.point.audienceDefaultAuthenticated')
            : t(`resLinks.point.audience.${pointDefault.visibility}`);

  // A member added to a link goes after the ones already there. Withheld members are not
  // in this list, so the number can repeat one of theirs — the server breaks that tie by
  // when the rows were written, which still puts the newcomer last.
  const nextSortOrder = link
    ? link.members.reduce((highest, member) => Math.max(highest, member.sortOrder), -1) + 1
    : 0;


  /** The member as the add endpoint takes it — the only shape that mints a point. */
  const memberBody = (sortOrder: number, main: boolean): ResLinkMemberAdd =>
    source === 'newPoint'
      ? {
          targetType: null,
          targetId: null,
          newGeoPoint: {
            lon: point![0],
            lat: point![1],
            z: altitude,
            name: pointName.trim() === '' ? null : pointName.trim(),
            visibility: pointVisibility === 'default' ? null : pointVisibility,
          },
          isMain: main,
          sortOrder,
          note: note.trim() === '' ? null : note.trim(),
          // A point minted here is the point itself; parts of it do not exist to address.
          anchorKind: 'whole',
          anchor: null,
          anchorFileId: null,
        }
      : {
          targetType,
          targetId,
          newGeoPoint: null,
          isMain: main,
          sortOrder,
          note: note.trim() === '' ? null : note.trim(),
          anchorKind,
          anchor: anchorKind === 'whole' ? null : anchor,
          anchorFileId: anchorKind === 'whole' ? null : anchorFileId,
        };

  /**
   * Creating a link around a spot that does not exist yet takes more than one call: a link
   * is created against things that already exist, and minting the point is a favour only
   * the member-add endpoint does. So the link is created holding the entity the user
   * started on, the point joins it as a second member, and — because a directed relation
   * refuses to hold two members without exactly one marked as the end it reads from — the
   * marker rides in on the new member and moves afterwards if the user left it on the
   * origin. A link created but left without its second member would be a fragment nobody
   * asked for, so a failure there takes the link with it.
   */
  const createWithNewPoint = async (start: LinkOrigin, main: boolean): Promise<ResLink> => {
    const created = await createLink.mutateAsync({
      relationTypeId,
      description: null,
      members: [originMember(start, false, 0)],
    });

    try {
      const added = await addMember.mutateAsync({
        id: created.id,
        body: memberBody(1, directed),
      });
      if (directed && !main) {
        return await updateLink.mutateAsync({
          id: created.id,
          body: { description: null, relationTypeId, mainMemberId: created.members[0].id },
        });
      }
      return { ...created, members: [...created.members, added] };
    } catch (error) {
      // Best effort: the point, if it was made, is a place on the map the user asked for
      // and stays. The half-built link does not.
      await deleteLink.mutateAsync(created.id).catch(() => undefined);
      throw error;
    }
  };

  const submit = async () => {
    if (!canSubmit) {
      return;
    }
    const main = mainAvailable && isMain;
    try {
      if (creating) {
        if (!origin) {
          return;
        }
        if (source === 'newPoint') {
          // Evaluated before the callback, not inside its argument list: an optional call
          // on an absent callback never evaluates what is handed to it.
          const created = await createWithNewPoint(origin, main);
          onCreated?.(created);
        } else {
          const created = await createLink.mutateAsync({
            relationTypeId,
            description: null,
            members: [
              // The end a directed link reads from: the entity the user started on, unless they
              // handed the marker to the member they are adding.
              originMember(origin, directed && !main, 0),
              {
                targetType,
                targetId: targetId!,
                isMain: main,
                sortOrder: 1,
                note: note.trim() === '' ? null : note.trim(),
                anchorKind,
                anchor: anchorKind === 'whole' ? null : anchor,
                anchorFileId: anchorKind === 'whole' ? null : anchorFileId,
              },
            ],
          });
          onCreated?.(created);
        }
      } else {
        await addMember.mutateAsync({ id: link.id, body: memberBody(nextSortOrder, main) });
      }
      message.success(t('common.saved'));
      onClose();
    } catch (error) {
      message.error(resLinkProblemMessage(error, t));
    }
  };

  return (
    <DialogHost
      kind="reslink-add-member"
      open={open}
      title={creating ? t('resLinks.createTitle') : t('resLinks.addMemberTitle')}
      onCancel={onClose}
      onOk={() => void submit()}
      okLoading={createLink.isPending || addMember.isPending || updateLink.isPending}
      // Nothing to send is refused where the button is, not silently on the click.
      okDisabled={!canSubmit}
      width={560}
    >
      <Flex vertical gap={12}>
        {creating && origin && !relation && (
          <Flex vertical gap={4}>
            <Typography.Text strong>{t('resLinks.relation')}</Typography.Text>
            <RelationSelect
              value={relationTypeId}
              onChange={(next, directedNext) => {
                setRelationTypeId(next);
                setRelationDirected(directedNext);
              }}
              // Always a name: the origin is the end a new directed link reads from unless
              // the marker is handed over, so the preview must never fall back to asking
              // for a marker that is already placed.
              mainLabel={
                isMain
                  ? (targetTitle ?? t('resLinks.newMember'))
                  : (origin.title ?? t('resLinks.thisItem'))
              }
            />
          </Flex>
        )}

        <Flex vertical gap={4}>
          <Typography.Text strong>{t('resLinks.memberType')}</Typography.Text>
          <Select<ResLinkTargetType>
            value={targetType}
            onChange={(next) => {
              setTargetType(next);
              setTargetId(null);
              setTargetTitle(null);
              setAnchorKind('whole');
              setAnchor(null);
              setAnchorFileId(null);
              if (next !== 'feature') {
                setSource('existing');
              }
            }}
            options={RESLINK_TARGET_TYPES.map((type) => ({
              value: type,
              label: t(targetTypeEntry(type).labelKey),
            }))}
            aria-label={t('resLinks.memberType')}
          />
        </Flex>

        {targetType === 'feature' && (
          <Select<'existing' | 'newPoint'>
            value={source}
            onChange={setSource}
            aria-label={t('resLinks.memberSource')}
            options={[
              { value: 'existing', label: t('resLinks.pickExisting') },
              { value: 'newPoint', label: t('resLinks.newPointOnMap') },
            ]}
          />
        )}

        {source === 'existing' ? (
          <Flex vertical gap={4}>
            <Typography.Text strong>{t('resLinks.target')}</Typography.Text>
            <ResLinkTargetPicker
              targetType={targetType}
              value={targetId}
              onChange={(id, title) => {
                setTargetId(id);
                setTargetTitle(title);
              }}
            />
          </Flex>
        ) : (
          <Flex vertical gap={8}>
            <Typography.Text strong>{t('resLinks.point.title')}</Typography.Text>
            <PointField value={point} onChange={setPoint} />
            {/* Optional, and left empty rather than guessed: a height nobody measured is
                worse than none at all, and the map does not need one to show the point. */}
            <InputNumber<number>
              value={altitude}
              onChange={setAltitude}
              placeholder={t('resLinks.point.altitudePlaceholder')}
              aria-label={t('resLinks.point.altitude')}
              style={{ width: '100%' }}
            />
            <Input
              value={pointName}
              onChange={(event) => setPointName(event.target.value)}
              maxLength={200}
              placeholder={t('resLinks.point.namePlaceholder')}
              aria-label={t('resLinks.point.name')}
            />
            <Select<Visibility | 'default'>
              value={pointVisibility}
              onChange={setPointVisibility}
              aria-label={t('resLinks.point.visibility')}
              options={[
                { value: 'default', label: t('resLinks.point.visibilityDefault') },
                ...pointVisibilities.map((value) => ({
                  value,
                  label: t(`caves.visibilityValues.${value}`),
                })),
              ]}
            />
            {/* Who will be able to see the point, named where it is made rather than
                discovered afterwards. The default names the audience it actually resolves
                to for this caller; until the answer arrives the notice stays silent rather
                than showing a rule the reader would have to apply to themselves. */}
            {audienceNotice && (
              <Alert
                type={pointVisibility === 'default' && !pointDefault ? 'warning' : 'info'}
                showIcon
                title={audienceNotice}
              />
            )}
          </Flex>
        )}

        {source === 'existing' && anchorKinds.length > 1 && (
          <Flex vertical gap={4}>
            <Typography.Text strong>{t('resLinks.anchorKind')}</Typography.Text>
            <Select<AnchorKind>
              value={anchorKind}
              onChange={(next) => {
                setAnchorKind(next);
                setAnchor(null);
                setAnchorFileId(null);
              }}
              aria-label={t('resLinks.anchorKind')}
              // Kinds whose selector has not shipped stay on the list, disabled: the scope
              // is honest about what a link can address, and about what it cannot yet.
              options={anchorKinds.map((kind) => {
                const composable = canComposeAnchor(kind);
                const label = t(`resLinks.anchorKinds.${kind}`);
                return {
                  value: kind,
                  disabled: !composable,
                  label: composable ? (
                    label
                  ) : (
                    <Tooltip title={t('resLinks.anchorKindPending')}>
                      <span>{label}</span>
                    </Tooltip>
                  ),
                };
              })}
            />
          </Flex>
        )}

        {AnchorEditor && (
          <Flex vertical gap={4}>
            <AnchorEditor
              value={anchor}
              onChange={(next, pin) => {
                setAnchor(next);
                // Undefined means "this kind has no pin", which is not the same as clearing one;
                // only an editor that composes coordinates against a file ever passes it.
                setAnchorFileId(pin ?? null);
              }}
              // A selector that has to show the resource needs to know which resource; the
              // numeric editors ignore this.
              target={targetId === null ? undefined : { targetType, targetId }}
            />
            {anchorMessage && <Typography.Text type="danger">{anchorMessage}</Typography.Text>}
          </Flex>
        )}

        <Input.TextArea
          value={note}
          onChange={(event) => setNote(event.target.value)}
          maxLength={NOTE_MAX}
          rows={2}
          placeholder={t('resLinks.notePlaceholder')}
          aria-label={t('resLinks.note')}
        />

        {!relation && (
          <Tooltip title={mainDisabledReason}>
            <Checkbox
              checked={isMain}
              disabled={!mainAvailable}
              onChange={(event) => setIsMain(event.target.checked)}
            >
              {t('resLinks.markAsMain')}
            </Checkbox>
          </Tooltip>
        )}
      </Flex>
    </DialogHost>
  );
}
