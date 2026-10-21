// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState } from 'react';
import {
  App,
  Alert,
  Card,
  Descriptions,
  Empty,
  Flex,
  Form,
  Input,
  Modal,
  Popconfirm,
  Select,
  Spin,
  Tag,
  Typography,
} from 'antd';
import Button from '../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import List from '../../components/List.tsx';
import { ApiError } from '../../api/client.ts';
import ObjectSelector from '../../components/selector/ObjectSelector.tsx';
import type { SelectorScope } from '../../filters/selection.ts';
import {
  useCavingGroups,
  useCreateSyncSet,
  useDeleteSyncSet,
  useFilterResolve,
  useSyncCapabilities,
  useSyncSets,
  useUpdateSyncSet,
  type SyncSet,
  type Visibility,
} from '../../api/hooks.ts';

/** A set's roots are caves, and only caves: the server refuses any other kind of feature. */
const CAVE_SCOPES: readonly SelectorScope[] = [
  { id: 'caves', labelKey: 'filters.scopes.caves', world: 'feature', where: { field: 'kind', value: 'cave' } },
];

interface FormValues {
  name: string;
  cavingGroupId?: string | null;
  uploadVisibility: Visibility;
  rootFeatureIds: string[];
}

/**
 * The caves a phone carries, chosen here and revocable here.
 *
 * The choosing could be done from a device, but the *reviewing* of it cannot: a caver who wants
 * to know what a phone they no longer have is still entitled to ask for, and to end that, needs
 * a surface that is not the phone. That is what this page is for, which is why deleting a set is
 * as prominent as making one.
 *
 * A set names caves; it never owns them. Deleting one takes nothing away from the caves it named
 * and undoes nothing that was already synced — it ends the licence, not the data.
 */
export default function SyncSettingsPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<FormValues>();
  const [editing, setEditing] = useState<SyncSet | null>(null);
  const [open, setOpen] = useState(false);

  const { data: capabilities } = useSyncCapabilities();
  const { data: sets, isPending } = useSyncSets();
  const { data: groups } = useCavingGroups();

  const create = useCreateSyncSet();
  const update = useUpdateSyncSet();
  const remove = useDeleteSyncSet();

  const uploadVisibility = Form.useWatch('uploadVisibility', form);

  useEffect(() => {
    if (!open) {
      return;
    }

    form.setFieldsValue({
      name: editing?.name ?? '',
      // A caving group is offered, never assumed: binding one says which club's rows a device
      // will create, and the safe answer for somebody who did not think about it is none.
      cavingGroupId: editing?.cavingGroupId ?? null,
      uploadVisibility: editing?.uploadVisibility ?? 'private',
      rootFeatureIds: editing?.rootFeatureIds ?? [],
    });
  }, [open, editing, form]);

  // Every stored root is named by id, under the caver's own visibility, in one question — never
  // from whatever a search last answered. Only a cave the server will not describe to them is
  // called one they can no longer read; a readable cave that matched no recent search is still
  // named, because this list is the one surface that exists to tell them the truth about it.
  const rootIds = useMemo(
    () => [...new Set((sets ?? []).flatMap((set) => set.rootFeatureIds))].sort(),
    [sets],
  );
  const { data: resolvedRoots } = useFilterResolve('feature', rootIds);
  const caveNames = useMemo(
    () => new Map((resolvedRoots ?? []).map((hit) => [hit.id, hit.title] as [string, string])),
    [resolvedRoots],
  );

  if (isPending) {
    return <Spin />;
  }

  const openFor = (set: SyncSet | null) => {
    setEditing(set);
    setOpen(true);
  };

  const onFinish = async (values: FormValues) => {
    const body = {
      name: values.name,
      cavingGroupId: values.cavingGroupId ?? null,
      uploadVisibility: values.uploadVisibility,
      rootFeatureIds: values.rootFeatureIds ?? [],
      // The settings document belongs to the device: it is stored verbatim and never
      // interpreted here, so this page sends an empty one rather than inventing keys for it.
      settings: (editing?.settings ?? {}) as Record<string, unknown>,
    };

    try {
      if (editing) {
        await update.mutateAsync({ id: editing.id, body });
      } else {
        await create.mutateAsync(body);
      }
      message.success(t('common.saved'));
      setOpen(false);
    } catch (error) {
      message.error(describeRefusal(error));
    }
  };

  /**
   * Says what the server actually refused. The choices this form offers are wider than what the
   * server accepts — a club is offered from the whole installation while only one the caver
   * belongs to may be bound, and a cave may stop being readable between the picking and the
   * saving — so collapsing every refusal into "could not save" leaves a caver looking at a
   * legal-looking selection with no way to find out which part of it is not.
   */
  function describeRefusal(error: unknown): string {
    if (error instanceof ApiError) {
      if (error.code === 'sync.caving_group_forbidden') {
        return t('settings.sync.notInCavingGroup');
      }
      if (error.code === 'sync.root_not_found') {
        return t('settings.sync.caveUnreadable');
      }
      if (error.code === 'sync.set_conflict') {
        return t('settings.sync.changedElsewhere');
      }
    }
    return t('common.saveFailed');
  }

  const onDelete = async (id: string) => {
    try {
      await remove.mutateAsync(id);
      message.success(t('settings.sync.revoked'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Flex vertical gap={16}>
      <Alert type="info" showIcon title={t('settings.sync.intro')} />

      {capabilities && (
        <Card size="small" title={t('settings.sync.serverTitle')}>
          <Descriptions size="small" column={1}>
            <Descriptions.Item label={t('settings.sync.contractVersion')}>
              {capabilities.contractVersion}
            </Descriptions.Item>
            <Descriptions.Item label={t('settings.sync.limits')}>
              {t('settings.sync.limitsValue', {
                pageSize: capabilities.pageSizeMax,
                uploadRows: capabilities.uploadRowsMax,
              })}
            </Descriptions.Item>
            <Descriptions.Item label={t('settings.sync.served')}>
              {capabilities.features.length > 0
                ? capabilities.features.join(', ')
                : t('settings.sync.servedNone')}
            </Descriptions.Item>
          </Descriptions>
        </Card>
      )}

      <Card
        size="small"
        title={t('settings.sync.setsTitle')}
        extra={
          <Button type="primary" onClick={() => openFor(null)} data-testid="sync-set-new">
            {t('settings.sync.newSet')}
          </Button>
        }
      >
        {(sets ?? []).length === 0 ? (
          <Empty description={t('settings.sync.noSets')} />
        ) : (
          <List
            dataSource={sets ?? []}
            renderItem={(set) => (
              <List.Item
                actions={[
                  <Button key="edit" type="link" onClick={() => openFor(set)}>
                    {t('common.edit')}
                  </Button>,
                  <Popconfirm
                    key="revoke"
                    title={t('settings.sync.revokeConfirm')}
                    okText={t('settings.sync.revoke')}
                    cancelText={t('common.cancel')}
                    onConfirm={() => void onDelete(set.id)}
                  >
                    <Button type="link" danger loading={remove.isPending}>
                      {t('settings.sync.revoke')}
                    </Button>
                  </Popconfirm>,
                ]}
              >
                <List.Item.Meta
                  title={set.name}
                  description={
                    <Flex vertical gap={4}>
                      <Typography.Text type="secondary">
                        {t('settings.sync.caveCount', { total: set.rootFeatureIds.length })}
                      </Typography.Text>
                      <Flex gap={4} wrap>
                        {set.rootFeatureIds.map((id) => (
                          <Tag key={id}>{caveNames.get(id) ?? t('settings.sync.unnamedCave')}</Tag>
                        ))}
                      </Flex>
                      <Typography.Text type="secondary">
                        {t('settings.sync.uploadsAs', {
                          visibility: t(`caves.visibilityValues.${set.uploadVisibility}`),
                        })}
                      </Typography.Text>
                    </Flex>
                  }
                />
              </List.Item>
            )}
          />
        )}
      </Card>

      <Modal
        open={open}
        title={editing ? t('settings.sync.editSet') : t('settings.sync.newSet')}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        confirmLoading={create.isPending || update.isPending}
        onOk={() => void form.submit()}
        onCancel={() => setOpen(false)}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" onFinish={(values) => void onFinish(values)}>
          <Form.Item name="name" label={t('settings.sync.name')} rules={[{ required: true, max: 200 }]}>
            <Input data-testid="sync-set-name" />
          </Form.Item>

          <Form.Item name="rootFeatureIds" label={t('settings.sync.caves')}>
            {/* Searched on the server, so a cave beyond any first page is reached by typing. The
                caves already chosen stay listed, ticked, above whatever the search found, so
                one is taken off the phone by unticking it. */}
            <ObjectSelector
              scopes={CAVE_SCOPES}
              multiple
              pageSize={8}
              sorts={[]}
              placeholder={t('settings.sync.cavesPlaceholder')}
              aria-label={t('settings.sync.cavesPlaceholder')}
              data-testid="sync-set-caves"
            />
          </Form.Item>

          <Form.Item
            name="uploadVisibility"
            label={t('settings.sync.uploadVisibility')}
            extra={t('settings.sync.uploadVisibilityHelp')}
          >
            <Select
              options={(['private', 'cavingGroup', 'authenticated', 'public'] as const).map((value) => ({
                value,
                label: t(`caves.visibilityValues.${value}`),
              }))}
            />
          </Form.Item>

          {/*
            Always shown, not only when the installation has clubs. Club visibility with no club
            named is a band that admits nobody, and hiding the field while leaving that choice on
            the list above is exactly how a caver arrives at it.
          */}
          <Form.Item
            name="cavingGroupId"
            label={t('settings.sync.cavingGroup')}
            rules={[
              {
                required: uploadVisibility === 'cavingGroup',
                message: t('settings.sync.cavingGroupRequired'),
              },
            ]}
          >
            <Select
              allowClear
              placeholder={t('settings.sync.noCavingGroup')}
              options={(groups ?? []).map((group) => ({ value: group.id, label: group.name }))}
              notFoundContent={t('settings.sync.noCavingGroupsHere')}
            />
          </Form.Item>
        </Form>
      </Modal>
    </Flex>
  );
}
