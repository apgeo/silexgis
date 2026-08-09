// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import {
  DeleteOutlined, EditOutlined, FolderOpenOutlined, FolderOutlined, InboxOutlined, PlusOutlined,
  UploadOutlined,
} from '@ant-design/icons';
import {
  App, Alert, Breadcrumb, Button, Collapse, Empty, Flex, Form, Input, Layout, Modal, Popconfirm,
  Select, Space, Spin, Switch, Table, Tag, Tooltip, Tree, Typography,
} from 'antd';
import type { DataNode, TreeProps } from 'antd/es/tree';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { ApiError } from '../../api/client.ts';
import {
  hasAccessAction,
  useCabinetDocuments,
  useCabinets,
  useCapabilities,
  useCreateCabinet,
  useDeleteCabinet,
  useDocumentTypes,
  useFileDocument,
  useTags,
  useUpdateCabinet,
  type CabinetDocument,
  type CabinetInfo,
} from '../../api/hooks.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';
import BulkFilingBar from '../../components/uploads/BulkFilingBar.tsx';
import UnfiledDocuments from '../../components/uploads/UnfiledDocuments.tsx';
import UploadDrawer from '../../components/uploads/UploadDrawer.tsx';

/**
 * The tree's first row is not a cabinet: it is the inbox, and it is a query rather than a
 * shelf. Giving it a reserved key keeps the selection one piece of state instead of two.
 */
const UnfiledKey = '__unfiled__';

/** Bytes as the shortest unit that keeps the number readable. */
function formatSize(bytes: number | null): string {
  if (bytes === null) {
    return '';
  }
  const units = ['B', 'KB', 'MB', 'GB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${unit === 0 ? value : value.toFixed(1)} ${units[unit]}`;
}

/**
 * Turns the flat cabinet list into the tree antd draws. The server orders siblings by
 * name and every cabinet names its parent, so one pass over the list is enough — no
 * recursion over the server, and no second request per level.
 */
function toTree(cabinets: CabinetInfo[]): DataNode[] {
  const nodes = new Map<string, DataNode & { children: DataNode[] }>();
  for (const cabinet of cabinets) {
    nodes.set(cabinet.id, {
      key: cabinet.id,
      title: cabinet.documentCount > 0 ? `${cabinet.name} (${cabinet.documentCount})` : cabinet.name,
      icon: cabinet.documentCount > 0 ? <FolderOpenOutlined /> : <FolderOutlined />,
      children: [],
    });
  }
  const roots: DataNode[] = [];
  for (const cabinet of cabinets) {
    const node = nodes.get(cabinet.id)!;
    const parent = cabinet.parentId === null ? undefined : nodes.get(cabinet.parentId);
    if (parent) {
      parent.children.push(node);
    } else {
      roots.push(node);
    }
  }
  return roots;
}

interface CabinetDraft {
  name: string;
  description: string | null;
  parentId: string | null;
  defaultDocumentTypeId: number | null;
  defaultVisibility: CabinetInfo['defaults']['visibility'];
  defaultTagIds: number[];
  requiredMetadataKeys: string[];
}

/**
 * Naming a cabinet — a new one, or one being renamed or moved.
 *
 * A component of its own, and mounted only while a cabinet is actually being named, because
 * the form store belongs with the fields it drives. Held by the page instead, it would exist
 * on every visit to this route with no fields attached to it, and the page would have to seed
 * it by hand at each of the two places that open this — which is what `initialValues` is for.
 */
function CabinetEditModal({
  editing,
  cabinets,
  defaultParentId,
  busy,
  onCancel,
  onSubmit,
}: {
  editing: CabinetInfo | 'new';
  cabinets: CabinetInfo[];
  defaultParentId?: string;
  busy: boolean;
  onCancel: () => void;
  onSubmit: (draft: CabinetDraft) => Promise<void>;
}) {
  const { t } = useTranslation();
  const { data: documentTypes } = useDocumentTypes();
  const { data: tags } = useTags('');
  const [form] = Form.useForm<{
    name: string;
    description?: string;
    parentId?: string;
    defaultDocumentTypeId?: number;
    defaultVisibility?: CabinetInfo['defaults']['visibility'];
    defaultTagIds?: number[];
    requiredMetadataKeys?: string[];
  }>();

  const submit = async () => {
    const values = await form.validateFields();
    await onSubmit({
      name: values.name,
      description: values.description ?? null,
      parentId: values.parentId ?? null,
      defaultDocumentTypeId: values.defaultDocumentTypeId ?? null,
      defaultVisibility: values.defaultVisibility ?? null,
      defaultTagIds: values.defaultTagIds ?? [],
      requiredMetadataKeys: values.requiredMetadataKeys ?? [],
    });
  };

  return (
    <Modal
      title={editing === 'new' ? t('cabinets.new') : t('cabinets.edit')}
      open
      onCancel={onCancel}
      onOk={() => void submit()}
      confirmLoading={busy}
      destroyOnHidden
    >
      <Form
        form={form}
        layout="vertical"
        requiredMark={false}
        initialValues={
          editing === 'new'
            ? { parentId: defaultParentId }
            : {
                name: editing.name,
                description: editing.description ?? undefined,
                parentId: editing.parentId ?? undefined,
                // This shelf's own settings, not the ones it inherits: the editor has to show
                // what this cabinet says, or saving would silently copy its parent's answers
                // onto it and freeze them there.
                defaultDocumentTypeId: editing.defaults.documentTypeId ?? undefined,
                defaultVisibility: editing.defaults.visibility ?? undefined,
                defaultTagIds: [...editing.defaults.tagIds],
                requiredMetadataKeys: [...editing.defaults.requiredMetadataKeys],
              }
        }
      >
        <Form.Item name="name" label={t('cabinets.name')} rules={[{ required: true }, { max: 200 }]}>
          <Input />
        </Form.Item>
        <Form.Item name="description" label={t('cabinets.description')} rules={[{ max: 1000 }]}>
          <Input.TextArea rows={2} />
        </Form.Item>
        <Form.Item name="parentId" label={t('cabinets.parent')}>
          <Select
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder={t('cabinets.noParent')}
            // A cabinet cannot sit inside itself; the server refuses a cycle outright,
            // and offering the option here would only be a way to discover that.
            options={cabinets
              .filter((cabinet) => editing === 'new' || cabinet.id !== editing.id)
              .map((cabinet) => ({ value: cabinet.id, label: cabinet.name }))}
          />
        </Form.Item>

        <Typography.Text strong>{t('cabinets.defaults')}</Typography.Text>
        <Typography.Paragraph type="secondary" style={{ marginTop: 4 }}>
          {t('cabinets.defaultsHint')}
        </Typography.Paragraph>

        <Form.Item name="defaultDocumentTypeId" label={t('cabinets.defaultType')}>
          <Select
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder={t('cabinets.defaultInherit')}
            options={(documentTypes ?? []).map((type) => ({ value: type.id, label: type.name }))}
          />
        </Form.Item>
        <Form.Item name="defaultVisibility" label={t('cabinets.defaultVisibility')}>
          <Select
            allowClear
            placeholder={t('cabinets.defaultInherit')}
            options={(['private', 'cavingGroup', 'internal', 'public'] as const).map((value) => ({
              value,
              label: t(`caves.visibilityValues.${value}`),
            }))}
          />
        </Form.Item>
        <Form.Item name="defaultTagIds" label={t('cabinets.defaultTags')}>
          <Select
            mode="multiple"
            allowClear
            optionFilterProp="label"
            placeholder={t('cabinets.defaultTagsHint')}
            options={(tags ?? []).map((tag) => ({ value: tag.id, label: tag.name }))}
          />
        </Form.Item>
        <Form.Item
          name="requiredMetadataKeys"
          label={t('cabinets.requiredMetadata')}
          help={t('cabinets.requiredMetadataHint')}
        >
          <Select mode="tags" allowClear tokenSeparators={[',', ' ']} />
        </Form.Item>
      </Form>
    </Modal>
  );
}

/**
 * The filing tree and what is on the selected shelf.
 *
 * A cabinet is a security anchor as much as a folder: rules can be scoped to one, so
 * moving a cabinet or filing a document into it moves access. That is why every write here
 * is gated on the right to write documents rather than on some separate notion of folder
 * administration.
 *
 * The count drawn on a cabinet and the listing beside it come from the same read rule and
 * count the same documents — a number larger than the list would say exactly how much the
 * list had withheld. Both mean the documents filed directly on that cabinet, which is what
 * the listing shows until the subtree switch below is turned on; with it on the listing
 * reaches further than the label, and is the only time the two differ.
 */
export default function CabinetsPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const isMobile = useIsMobile();
  const { data: capabilities } = useCapabilities();
  const canRead = hasAccessAction(capabilities?.domains.documents, 'read');
  // One right governs the tree: whoever may write documents may arrange where they live.
  // The server re-decides it per cabinet, so this only hides controls it would refuse.
  const canWrite = hasAccessAction(capabilities?.domains.documents, 'write');

  const { data: cabinets, isPending } = useCabinets(canRead);
  const [selected, setSelected] = useState<string>();
  const [selectedDocuments, setSelectedDocuments] = useState<string[]>([]);
  const [uploading, setUploading] = useState(false);
  const showingUnfiled = selected === UnfiledKey;
  // Where the shelf is stacked above the documents rather than beside them, it starts open —
  // nothing has been picked yet, so the tree is the only thing there is to do.
  const [shelfOpen, setShelfOpen] = useState(true);
  const [includeSubtree, setIncludeSubtree] = useState(false);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  // The inbox is a reserved key rather than a cabinet, so nothing is asked about it here —
  // the inbox reads its own listing. Passing it through would ask the cabinet route about an
  // id that is not one, which answers 404 on every visit.
  const { data: documents, isFetching } = useCabinetDocuments(showingUnfiled ? undefined : selected, {
    includeSubtree,
    page,
    pageSize,
  });

  const createCabinet = useCreateCabinet();
  const updateCabinet = useUpdateCabinet();
  const deleteCabinet = useDeleteCabinet();
  const fileDocument = useFileDocument();

  const [editing, setEditing] = useState<CabinetInfo | 'new' | null>(null);
  const [refiling, setRefiling] = useState<CabinetDocument | null>(null);
  const [refileTarget, setRefileTarget] = useState<string>();

  const byId = useMemo(
    () => new Map((cabinets ?? []).map((cabinet) => [cabinet.id, cabinet])),
    [cabinets],
  );
  const treeData = useMemo(() => toTree(cabinets ?? []), [cabinets]);
  const current = selected === undefined ? undefined : byId.get(selected);

  if (!capabilities) {
    return <Spin style={{ display: 'block', marginTop: '20vh' }} />;
  }
  if (!canRead) {
    return <Alert type="error" showIcon title={t('admin.forbidden')} style={{ margin: 16 }} />;
  }

  const failed = (error: unknown, fallback = 'common.saveFailed') => {
    const code = error instanceof ApiError ? error.code : undefined;
    const named: Record<string, string> = {
      'cabinet.name_taken': 'cabinets.nameTaken',
      'cabinet.in_use': 'cabinets.inUse',
      'cabinet.not_empty': 'cabinets.notEmpty',
      'cabinet.cycle': 'cabinets.cycle',
      'cabinet.too_deep': 'cabinets.tooDeep',
      'document.write_forbidden': 'cabinets.filingForbidden',
    };
    message.error(t(code && named[code] ? named[code] : fallback));
  };

  const submit = async (body: CabinetDraft) => {
    try {
      if (editing === 'new') {
        const created = await createCabinet.mutateAsync(body);
        setSelected(created.id);
      } else if (editing) {
        await updateCabinet.mutateAsync({ id: editing.id, ...body });
      }
      setEditing(null);
      message.success(t('common.saved'));
    } catch (error) {
      failed(error);
    }
  };

  const onDelete = async (cabinet: CabinetInfo) => {
    try {
      await deleteCabinet.mutateAsync(cabinet.id);
      if (selected === cabinet.id) {
        setSelected(undefined);
      }
      message.success(t('common.deleted'));
    } catch (error) {
      failed(error);
    }
  };

  // Dropping a cabinet on another re-parents it, taking everything below it with it.
  // Dropping between siblings means "make this a sibling of theirs", i.e. move it under
  // their parent — antd reports that as a drop with `dropToGap`.
  const onDrop: TreeProps['onDrop'] = (info) => {
    const dragged = String(info.dragNode.key);
    const target = byId.get(String(info.node.key));
    const cabinet = byId.get(dragged);
    if (!cabinet || !target) {
      return;
    }
    const parentId = info.dropToGap ? target.parentId : target.id;
    if (parentId === cabinet.id || parentId === cabinet.parentId) {
      return;
    }
    void (async () => {
      try {
        await updateCabinet.mutateAsync({
          id: cabinet.id,
          name: cabinet.name,
          description: cabinet.description,
          parentId,
        });
        message.success(t('common.saved'));
      } catch (error) {
        failed(error);
      }
    })();
  };

  // Filing adds a shelf rather than moving between them: a document may sit on several,
  // and each one that a rule names reaches it. Taking it off a shelf is the separate,
  // equally guarded act below — collapsing the two into one "move" would hide that a
  // document can legitimately be in two places, and would need two requests that can
  // half-succeed.
  const fileElsewhere = async () => {
    if (!refiling || !refileTarget) {
      return;
    }
    try {
      await fileDocument.mutateAsync({
        cabinetId: refileTarget,
        documentId: refiling.id,
        filed: true,
      });
      setRefiling(null);
      setRefileTarget(undefined);
      message.success(t('common.saved'));
    } catch (error) {
      failed(error);
    }
  };

  const unfile = async (row: CabinetDocument) => {
    if (!selected) {
      return;
    }
    try {
      await fileDocument.mutateAsync({ cabinetId: selected, documentId: row.id, filed: false });
      message.success(t('common.saved'));
    } catch (error) {
      failed(error);
    }
  };

  const crumbs = (current?.ancestorIds ?? [])
    .map((id) => byId.get(id))
    .filter((cabinet): cabinet is CabinetInfo => cabinet !== undefined);

  // Built once and hosted twice: beside the documents where there is room for both, and
  // stacked above them where there is not. One notion of "the shelf" rather than two that
  // can drift apart.
  const shelf = (
    <>
      <Flex justify="space-between" align="center" style={{ marginBottom: 12 }}>
        <Typography.Text strong>{t('cabinets.title')}</Typography.Text>
        {canWrite && (
          <Button
            size="small"
            icon={<PlusOutlined />}
            onClick={() => setEditing('new')}
          >
            {t('cabinets.new')}
          </Button>
        )}
      </Flex>
      {isPending ? (
        <Spin />
      ) : (
        <Tree
          showIcon
          blockNode
          // Dragging a cabinet into another is a desktop act. On a touch screen a drag is how
          // the page is scrolled, so offering it there is offering a way to refile an archive
          // by accident; the same move stays available through the parent field when editing.
          draggable={canWrite && !isMobile}
          defaultExpandAll
          onDrop={onDrop}
          // The inbox rides at the top of the same tree rather than beside it: it is where
          // an upload lands when nobody has decided yet, so it belongs where somebody is
          // already looking for a place to put things.
          treeData={[
            {
              key: UnfiledKey,
              title: t('cabinets.unfiled'),
              icon: <InboxOutlined />,
              selectable: true,
              isLeaf: true,
            },
            ...treeData,
          ]}
          selectedKeys={selected ? [selected] : []}
          onSelect={(keys) => {
            setSelected(keys.length > 0 ? String(keys[0]) : undefined);
            setSelectedDocuments([]);
            setPage(1);
            // Picking a shelf on a phone is picking what to read next: the tree steps out of
            // the way rather than leaving the documents pushed off the bottom of the screen.
            setShelfOpen(false);
          }}
        />
      )}
    </>
  );

  const documentsPane = (
    <>
      {showingUnfiled ? (
        <UnfiledDocuments cabinets={cabinets ?? []} canWrite={canWrite} />
      ) : current === undefined ? (
        <Empty description={t('cabinets.pickOne')} style={{ marginTop: '15vh' }} />
      ) : (
        <>
          <Breadcrumb
            style={{ marginBottom: 8 }}
            items={crumbs.map((cabinet) => ({
              title:
                cabinet.id === current.id ? (
                  cabinet.name
                ) : (
                  <Typography.Link onClick={() => setSelected(cabinet.id)}>
                    {cabinet.name}
                  </Typography.Link>
                ),
            }))}
          />
          <Flex justify="space-between" align="center" style={{ marginBottom: 12 }}>
            <Typography.Title level={4} style={{ margin: 0 }}>
              {current.name}
            </Typography.Title>
            <Space>
              <Tooltip title={t('cabinets.includeSubtreeHint')}>
                <Space size={6}>
                  <Switch
                    size="small"
                    checked={includeSubtree}
                    onChange={(checked) => {
                      setIncludeSubtree(checked);
                      setPage(1);
                    }}
                  />
                  <Typography.Text type="secondary">{t('cabinets.includeSubtree')}</Typography.Text>
                </Space>
              </Tooltip>
              {canWrite && (
                <Button
                  icon={<EditOutlined />}
                  onClick={() => setEditing(current)}
                >
                  {t('common.edit')}
                </Button>
              )}
              {canWrite && (
                <Button
                  type="primary"
                  icon={<UploadOutlined />}
                  onClick={() => setUploading(true)}
                >
                  {t('uploads.upload')}
                </Button>
              )}
              {canWrite && (
                <Popconfirm
                  title={t('cabinets.deleteConfirm')}
                  onConfirm={() => void onDelete(current)}
                >
                  <Button danger icon={<DeleteOutlined />} />
                </Popconfirm>
              )}
            </Space>
          </Flex>
          {current.description && (
            <Typography.Paragraph type="secondary">{current.description}</Typography.Paragraph>
          )}
          <Alert
            type="info"
            showIcon
            style={{ marginBottom: 12 }}
            title={t('cabinets.filingMovesAccess')}
          />

          {current.defaults.effectiveRequiredMetadataKeys.length > 0 && (
            <Alert
              type="warning"
              showIcon
              style={{ marginBottom: 12 }}
              message={t('cabinets.expectsMetadata', {
                keys: current.defaults.effectiveRequiredMetadataKeys.join(', '),
              })}
            />
          )}

          {canWrite && selectedDocuments.length > 0 && (
            <BulkFilingBar
              documentIds={selectedDocuments}
              cabinets={cabinets ?? []}
              currentCabinetId={current.id}
              onDone={() => setSelectedDocuments([])}
            />
          )}

          <Table<CabinetDocument>
            scroll={{ x: 'max-content' }}
            rowKey="id"
            size="middle"
            loading={isFetching && !documents}
            dataSource={documents?.items}
            locale={{ emptyText: t('cabinets.noDocuments') }}
            rowSelection={
              canWrite
                ? {
                    selectedRowKeys: selectedDocuments,
                    onChange: (keys) => setSelectedDocuments(keys.map(String)),
                  }
                : undefined
            }
            onChange={(pagination) => {
              setPage(pagination.current ?? 1);
              setPageSize(pagination.pageSize ?? 20);
            }}
            pagination={{
              current: documents?.page,
              pageSize: documents?.pageSize,
              total: documents?.totalItems,
              showSizeChanger: true,
            }}
            columns={[
              {
                title: t('documents.title'),
                dataIndex: 'title',
                ellipsis: true,
                // The title is the way in. A shelf that only lists what is on it, with no
                // way to open any of it, is a catalogue rather than an archive.
                render: (value: string, row: CabinetDocument) => (
                  <Space size={6}>
                    <Link to={`/documents/${row.id}`}>{value}</Link>
                    {row.missingMetadataKeys.length > 0 && (
                      // A checklist rather than a refusal: the upload was never blocked for
                      // this, so the mark is where somebody finds out what is still to fill in.
                      <Tooltip
                        title={t('cabinets.missingMetadata', {
                          keys: row.missingMetadataKeys.join(', '),
                        })}
                      >
                        <Tag color="warning">{t('cabinets.incomplete')}</Tag>
                      </Tooltip>
                    )}
                  </Space>
                ),
              },
              {
                title: t('documents.visibility'),
                dataIndex: 'visibility',
                width: 160,
                render: (value: CabinetDocument['visibility']) => (
                  <Tag>{t(`caves.visibilityValues.${value}`)}</Tag>
                ),
              },
              {
                title: t('cabinets.size'),
                dataIndex: 'sizeBytes',
                width: 110,
                align: 'right',
                render: (value: number | null) => formatSize(value),
              },
              ...(canWrite
                ? [
                    {
                      title: '',
                      key: 'actions',
                      width: 200,
                      render: (_: unknown, row: CabinetDocument) => (
                        <Flex gap={8}>
                          <Button
                            size="small"
                            onClick={() => {
                              setRefiling(row);
                              setRefileTarget(undefined);
                            }}
                          >
                            {t('cabinets.fileElsewhere')}
                          </Button>
                          <Popconfirm
                            title={t('cabinets.unfileConfirm')}
                            onConfirm={() => void unfile(row)}
                          >
                            <Button size="small" type="text" danger icon={<DeleteOutlined />} />
                          </Popconfirm>
                        </Flex>
                      ),
                    },
                  ]
                : []),
            ]}
          />
        </>
      )}
    </>
  );

  const modals = (
    <>
      {/* Mounted only while it is open. A closed drawer's hooks would still run — fetching
          the upload limits on every visit to this page for a control nobody opened. */}
      {uploading && (
        <UploadDrawer
          open
          onClose={() => setUploading(false)}
          cabinetId={current?.id}
          cabinetName={current?.name}
          requiredMetadataKeys={current?.defaults.effectiveRequiredMetadataKeys}
        />
      )}

      {editing !== null && (
        <CabinetEditModal
          editing={editing}
          cabinets={cabinets ?? []}
          defaultParentId={selected}
          busy={createCabinet.isPending || updateCabinet.isPending}
          onCancel={() => setEditing(null)}
          onSubmit={submit}
        />
      )}

      <Modal
        title={t('cabinets.fileElsewhere')}
        open={refiling !== null}
        onCancel={() => setRefiling(null)}
        onOk={() => void fileElsewhere()}
        okButtonProps={{ disabled: !refileTarget }}
        confirmLoading={fileDocument.isPending}
        destroyOnHidden
      >
        <Typography.Paragraph type="secondary">{t('cabinets.fileElsewhereHint')}</Typography.Paragraph>
        <Select
          style={{ width: '100%' }}
          showSearch
          optionFilterProp="label"
          placeholder={t('cabinets.pickCabinet')}
          value={refileTarget}
          onChange={setRefileTarget}
          options={(cabinets ?? [])
            .filter((cabinet) => cabinet.id !== selected)
            .map((cabinet) => ({ value: cabinet.id, label: cabinet.name }))}
        />
      </Modal>
    </>
  );

  if (isMobile) {
    // One column. A side panel wide enough to read cabinet names in would leave a phone barely
    // a hundred points for the documents themselves, and the shell already owns this screen's
    // left edge, so the shelf folds upward from the top instead of into a second drawer.
    return (
      <Flex vertical style={{ height: '100%', padding: 12, overflow: 'auto' }}>
        <Collapse
          size="small"
          style={{ marginBottom: 12 }}
          activeKey={shelfOpen ? ['shelf'] : []}
          onChange={(keys) => setShelfOpen(keys.length > 0)}
          items={[
            {
              key: 'shelf',
              // The header names where the reader is when the shelf is folded away, which is
              // most of the time — otherwise the one thing on screen says nothing.
              label: current?.name ?? t('cabinets.title'),
              children: shelf,
            },
          ]}
        />
        <div style={{ minWidth: 0 }}>{documentsPane}</div>
        {modals}
      </Flex>
    );
  }

  return (
    <Layout style={{ height: '100%', background: 'transparent' }}>
      <Layout.Sider width={280} theme="light" style={{ padding: 16, overflow: 'auto' }}>
        {shelf}
      </Layout.Sider>

      <Layout.Content style={{ padding: 24, overflow: 'auto', minWidth: 0 }}>
        {documentsPane}
      </Layout.Content>
      {modals}
    </Layout>
  );
}
