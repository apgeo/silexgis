// SPDX-License-Identifier: AGPL-3.0-or-later
import { useRef, useState } from 'react';
import { DeleteOutlined, EditOutlined, LockOutlined, PlusOutlined } from '@ant-design/icons';
import {
  App,
  Card,
  Empty,
  Flex,
  Form,
  Input,
  Modal,
  Popconfirm,
  Result,
  Select,
  Skeleton,
  Space,
  Tag,
  Typography,
} from 'antd';
import Button from '../../components/Button.tsx';
import ReadFailedAlert from '../../components/ReadFailedAlert.tsx';
import { KEEP_OPEN_ON_OUTSIDE_CLICK, useDiscardGuard, useFormEdits } from '../../components/useDiscardGuard.ts';
import { useTranslation } from 'react-i18next';
import {
  useCan,
  useCavingGroups,
  useChecklists,
  useCreateChecklist,
  useDeleteChecklist,
  useEffectiveAccess,
  useMe,
  useReadChecklistForChange,
  useUpdateChecklist,
  parseAccessActions,
  type ChecklistInfo,
  type ChecklistWrite,
} from '../../api/hooks.ts';
import List from '../../components/List.tsx';
import PermissionsModal from '../../components/permissions/PermissionsModal.tsx';
import { checklistProblem, type ChecklistAct } from './checklistProblems.ts';
import { isConcurrencyConflict } from '../../api/client.ts';

/** The audience a list is given, in the order they widen. */
const visibilities = ['private', 'cavingGroup', 'authenticated', 'public'] as const;

type FormValues = {
  title: string;
  description?: string;
  visibility: ChecklistWrite['visibility'];
  cavingGroupId?: string | null;
  items: { id?: string; text: string }[];
};

/**
 * Where a person writes the lists trips work through.
 *
 * There is one kind of list on this page, deliberately. What an administrator publishes for the
 * whole installation is a row written here like any other and given an audience everyone falls
 * inside — so the page has no "publish" act, no separate section, and no notion of a default. A
 * wider audience is the only difference, and it is chosen from the same control a caver uses to
 * share their own list with their club.
 *
 * Nothing on this page knows what any trip has settled. How much of a list a party has worked
 * through is read on that trip, and it changes nothing about the list.
 */
export default function ChecklistsPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data, isLoading, isError, error, refetch } = useChecklists();
  const { data: cavingGroups } = useCavingGroups();

  // Handing one list to one person by name is narrower than any audience the visibility control
  // offers, and it goes through the same object-permissions dialog a trip or a cave has. The
  // listing does not say per row who may manage a list's rules, so the lock is drawn for the
  // list's owner — who always may — and for a caller holding that right over every list; the
  // server decides for anybody else, and the dialog says so when it is refused.
  const { data: me } = useMe();
  const managesAnyList = useCan('checklists', 'managePermissions');
  const writesAnyList = useCan('checklists', 'write');
  const deletesAnyList = useCan('checklists', 'delete');
  const [sharing, setSharing] = useState<ChecklistInfo | null>(null);
  const create = useCreateChecklist();
  const update = useUpdateChecklist();
  const remove = useDeleteChecklist();
  const readForChange = useReadChecklistForChange();
  const [editing, setEditing] = useState<ChecklistInfo | null>(null);
  // The list whose current reading is being fetched before it is opened or deleted.
  const [reading, setReading] = useState<string | null>(null);
  // The version of the list the dialog was filled from: the rewrite is whole, so it has to be
  // refused if somebody changed the list after these lines were read.
  const openedAtVersion = useRef<string | undefined>(undefined);
  const [open, setOpen] = useState(false);
  const [initial, setInitial] = useState<FormValues | undefined>(undefined);
  const [form] = Form.useForm<FormValues>();
  const requestDiscard = useDiscardGuard();
  const edits = useFormEdits(open);

  const tell = (error: unknown, act: ChecklistAct) => {
    const sentence = checklistProblem(error, act, t);
    if (sentence) {
      void message.error(sentence);
    }
  };

  // Each listing row carries its lines and the version they were read at, so the dialog is filled
  // from the row and the rewrite is held to the row's version: a list changed since the row was
  // read is refused rather than rewritten from lines nobody saw. A row without a version is read
  // on its own first, and the dialog is filled from that answer and held to its version.
  const openForEdit = async (list: ChecklistInfo) => {
    if (list.version) {
      openedAtVersion.current = list.version;
      openFor(list);
      return;
    }
    setReading(list.id);
    try {
      const held = await readForChange(list.id);
      openedAtVersion.current = held.version;
      openFor(held.list);
    } catch (error) {
      tell(error, 'open');
    } finally {
      setReading(null);
    }
  };

  // A delete is decided on the list as this page showed it, so it is held to the version of the
  // row shown; a list changed since is refused, and the shared conflict prompt says so while the
  // lists are read again. A row without a version falls back to reading the list now and deleting
  // only when it has not moved since the listing was read.
  const deleteShown = async (list: ChecklistInfo) => {
    if (list.version) {
      remove.mutate(
        { id: list.id, ifMatch: list.version },
        {
          onError: (error) => {
            if (isConcurrencyConflict(error)) {
              void refetch();
            }
            tell(error, 'delete');
          },
        },
      );
      return;
    }
    setReading(list.id);
    try {
      const held = await readForChange(list.id);
      if (held.list.updatedAt !== list.updatedAt) {
        void message.warning(t('checklists.changedSinceShown'));
        void refetch();
        return;
      }

      remove.mutate(
        { id: list.id, ifMatch: held.version },
        { onError: (error) => tell(error, 'delete') },
      );
    } catch (error) {
      tell(error, 'delete');
    } finally {
      setReading(null);
    }
  };

  const openFor = (list: ChecklistInfo | null) => {
    if (!list) {
      openedAtVersion.current = undefined;
    }
    setEditing(list);
    // Handed to the form as it mounts rather than written into it here. The dialog's body is
    // destroyed while it is shut, so at this moment the form instance is attached to no form, and
    // writing into it then made the form library report an error in the console on every opening.
    // The dialog mounts a fresh form each time it opens, so these are what that form starts from.
    setInitial({
      title: list?.title ?? '',
      description: list?.description ?? undefined,
      visibility: list?.visibility ?? 'private',
      cavingGroupId: list?.cavingGroupId ?? undefined,
      // The line's id travels with it so that rewording keeps every confirmation made against
      // it — a line rewritten the morning of the trip is the same line, not a new one.
      items: list?.items.map((item) => ({ id: item.id, text: item.text })) ?? [{ text: '' }],
    });
    setOpen(true);
  };

  const submit = async (values: FormValues) => {
    const body: ChecklistWrite = {
      title: values.title,
      description: values.description ?? null,
      visibility: values.visibility,
      cavingGroupId: values.cavingGroupId ?? null,
      items: (values.items ?? []).map((item) => ({ id: item.id ?? null, text: item.text })),
    };
    try {
      if (editing) {
        await update.mutateAsync({ id: editing.id, body, ifMatch: openedAtVersion.current });
      } else {
        await create.mutateAsync(body);
      }
      setOpen(false);
      void message.success(t('checklists.saved'));
    } catch (error) {
      // The dialog stays open with what was typed, whatever the refusal.
      tell(error, 'save');
    }
  };

  return (
    <>
      <Flex justify="space-between" align="center" gap="middle" wrap style={{ marginBottom: 12 }}>
        <Space orientation="vertical" size={0}>
          <Typography.Title level={3} style={{ margin: 0 }}>
            {t('checklists.title')}
          </Typography.Title>
          <Typography.Text type="secondary">{t('checklists.subtitle')}</Typography.Text>
        </Space>
        <Button type="primary" icon={<PlusOutlined />} onClick={() => openFor(null)}>
          {t('checklists.new')}
        </Button>
      </Flex>

      {/* A list already on screen stays through a failed re-read, under a notice. */}
      <ReadFailedAlert isError={isError && data !== undefined} error={error} held onRetry={refetch} testId="checklists-refresh-failed" />
      {isLoading ? (
        <Skeleton active />
      ) : isError && data === undefined ? (
        <Result
          status="warning"
          title={t('checklists.loadFailed')}
          extra={<Button onClick={() => void refetch()}>{t('common.retry')}</Button>}
        />
      ) : !data?.length ? (
        <Empty description={t('checklists.empty')} />
      ) : (
        <List
          dataSource={data}
          renderItem={(list) => (
            <Card size="small" style={{ marginBottom: 12 }}>
              <Flex justify="space-between" align="flex-start" gap="middle" wrap>
                <Space orientation="vertical" size={2}>
                  <Typography.Text strong>{list.title}</Typography.Text>
                  {list.description ? (
                    <Typography.Text type="secondary">{list.description}</Typography.Text>
                  ) : null}
                  <Space size={4} wrap>
                    <Tag>{t(`checklists.visibilityValues.${list.visibility}`)}</Tag>
                    <Tag>{t('checklists.lineCount', { count: list.items.length })}</Tag>
                  </Space>
                </Space>
                <Space>
                  {(managesAnyList || list.ownerUserId === me?.id) && (
                    <Button
                      icon={<LockOutlined />}
                      onClick={() => setSharing(list)}
                      aria-label={t('permissions.button')}
                      data-testid={`checklist-permissions-${list.id}`}
                    />
                  )}
                  <ChangeActions
                    list={list}
                    owned={list.ownerUserId === me?.id}
                    writesAnyList={writesAnyList}
                    deletesAnyList={deletesAnyList}
                    reading={reading === list.id}
                    onEdit={() => void openForEdit(list)}
                    onDelete={() => void deleteShown(list)}
                  />
                </Space>
              </Flex>
            </Card>
          )}
        />
      )}

      {sharing && (
        <PermissionsModal
          entityType="checklist"
          entityId={sharing.id}
          open
          onClose={() => setSharing(null)}
        />
      )}

      <Modal
        open={open}
        title={editing ? t('checklists.edit') : t('checklists.new')}
        onCancel={() => requestDiscard(edits.edited, () => setOpen(false))}
        onOk={() => void form.submit()}
        confirmLoading={create.isPending || update.isPending}
        mask={KEEP_OPEN_ON_OUTSIDE_CLICK}
        destroyOnHidden
      >
        <Form
          form={form}
          layout="vertical"
          initialValues={initial}
          onValuesChange={edits.onValuesChange}
          onFinish={(values) => void submit(values)}
        >
          <Form.Item
            name="title"
            label={t('checklists.fieldTitle')}
            rules={[{ required: true, max: 200 }]}
          >
            <Input />
          </Form.Item>
          <Form.Item name="description" label={t('checklists.fieldDescription')} rules={[{ max: 2000 }]}>
            <Input.TextArea rows={2} />
          </Form.Item>
          <Form.Item
            name="visibility"
            label={t('checklists.fieldVisibility')}
            extra={t('checklists.visibilityHint')}
            rules={[{ required: true }]}
          >
            <Select
              options={visibilities.map((value) => ({
                value,
                label: t(`checklists.visibilityValues.${value}`),
              }))}
            />
          </Form.Item>
          <Form.Item name="cavingGroupId" label={t('checklists.fieldCavingGroup')}>
            <Select
              allowClear
              options={(cavingGroups ?? []).map((group) => ({
                value: group.id,
                label: group.name,
              }))}
            />
          </Form.Item>

          <Form.List name="items">
            {(fields, { add, remove: removeLine }) => (
              <Space orientation="vertical" size={4} style={{ width: '100%' }}>
                <Typography.Text strong>{t('checklists.fieldItems')}</Typography.Text>
                {fields.map((field) => (
                  <Flex key={field.key} gap={8} align="baseline">
                    {/* The line's id rides along hidden: it is what a confirmation names, so
                        losing it here would strand every tick made against the line. */}
                    <Form.Item name={[field.name, 'id']} hidden noStyle>
                      <Input type="hidden" />
                    </Form.Item>
                    <Form.Item
                      name={[field.name, 'text']}
                      style={{ flex: 1, marginBottom: 8 }}
                      rules={[{ required: true, max: 500 }]}
                    >
                      <Input placeholder={t('checklists.itemPlaceholder')} />
                    </Form.Item>
                    <Button
                      type="text"
                      danger
                      icon={<DeleteOutlined />}
                      aria-label={t('checklists.removeLine')}
                      onClick={() => removeLine(field.name)}
                    />
                  </Flex>
                ))}
                <Button type="dashed" icon={<PlusOutlined />} onClick={() => add({ text: '' })} block>
                  {t('checklists.addLine')}
                </Button>
              </Space>
            )}
          </Form.List>
        </Form>
      </Modal>
    </>
  );
}

/**
 * Edit and Delete for one list, drawn only for somebody who may take them.
 *
 * The listing does not say per row what the reader may do, and a list published to everybody is
 * read by people who may not change it, so offering both to everyone sends them into a refusal the
 * page could have foreseen. The owner always may, and so may a holder of the right over every list;
 * for anybody else the server is asked about that one list, which also covers a list handed to a
 * person by name. While that answer is not in, the buttons are not drawn.
 */
function ChangeActions({
  list,
  owned,
  writesAnyList,
  deletesAnyList,
  reading,
  onEdit,
  onDelete,
}: {
  list: ChecklistInfo;
  owned: boolean;
  writesAnyList: boolean;
  deletesAnyList: boolean;
  reading: boolean;
  onEdit: () => void;
  onDelete: () => void;
}) {
  const { t } = useTranslation();
  const settled = owned || (writesAnyList && deletesAnyList);
  const { data: effective } = useEffectiveAccess('checklist', list.id, { enabled: !settled });
  const held = effective ? parseAccessActions(effective.actions) : null;
  const mayEdit = owned || writesAnyList || (held?.has('write') ?? false);
  const mayDelete = owned || deletesAnyList || (held?.has('delete') ?? false);

  return (
    <>
      {mayEdit && (
        <Button icon={<EditOutlined />} loading={reading} onClick={onEdit} aria-label={t('common.edit')} />
      )}
      {mayDelete && (
        <Popconfirm title={t('checklists.deleteConfirm')} onConfirm={onDelete}>
          <Button danger icon={<DeleteOutlined />} disabled={reading} aria-label={t('checklists.delete')} />
        </Popconfirm>
      )}
    </>
  );
}
