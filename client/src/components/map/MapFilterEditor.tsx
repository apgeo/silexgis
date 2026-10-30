// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { FilterOutlined, SaveOutlined } from '@ant-design/icons';
import { App, Drawer, Flex, Form, Input, Modal, Select, Tooltip } from 'antd';
import { useTranslation } from 'react-i18next';
import { appliesTo, documentOf, useCreateSavedFilter, useSavedFilters } from '../../api/hooks/savedFilters.ts';
import { holdsPosition } from '../../filters/spatial.ts';
import { SpatialSourcesContext, type SpatialSources } from '../../filters/spatialSources.ts';
import { audienceFields, type SavedFilterAudience } from '../../filters/savedFilterAudience.ts';
import {
  MAP_FILTER_WORLD,
  mapEditorDocument,
  mapFilterOfEditor,
  tagOfMapFilter,
  type MapFilter,
} from '../../map/mapFilters.ts';
import { drawFilterOutline, viewOutline } from '../../map/filterOutline.ts';
import { getWorkspaceMap, mapLonLatExtent } from '../../map/mapContext.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';
import Button from '../Button.tsx';
import FilterEditor from '../filters/FilterEditor.tsx';
import SavedFilterAudienceField from '../filters/SavedFilterAudienceField.tsx';
import { savedFilterSaveFailedKey, useSavedFilterAudienceChoice } from '../filters/useSavedFilterAudienceChoice.ts';

interface Props {
  /** The filter the map draws through now. */
  filter: MapFilter | null;
  /** Adopts a filter the person chose or wrote; null lifts the map's filter. */
  onChange: (filter: MapFilter | null) => void;
}

/**
 * The map's own filter, written as conditions in the editor every other surface uses.
 *
 * The button opens the editor on the conditions in force — a copy of the saved filter's, when the
 * map is drawing through one — and Apply hands back a document filter, so the map, its address, a
 * saved view and a link all carry what was written. A saved filter can also be chosen here by name
 * (the map then follows the filter by id, as for the pinned chips), and the conditions on screen
 * can be saved as a new one.
 *
 * The spatial condition is offered with the map's own places: the ground on screen, an outline
 * drawn on the map, and the selected object to measure from. Each is written as the plain value
 * every other surface would hold (a ring of positions, an object), and the map's routes run it
 * under the caller's access like any other; a filter holding a view or a drawn outline is not
 * storable, so Save is off for it and says why.
 *
 * Nothing about protection is decided here: the map's routes answer a document under the caller's
 * own access, exactly as for the same filter typed anywhere else.
 */
export default function MapFilterEditor({ filter, onChange }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [open, setOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const { data: savedFilters } = useSavedFilters();
  const create = useCreateSavedFilter();
  const [form] = Form.useForm<{ name: string; description?: string; audience: SavedFilterAudience }>();
  const { groups, otherGroups, defaultAudience } = useSavedFilterAudienceChoice();

  const savedInForce = filter?.kind === 'saved' ? savedFilters?.find((f) => f.id === filter.savedFilterId) : undefined;
  const seed = mapEditorDocument(filter, savedInForce ? documentOf(savedInForce) : null);
  // A tag-only filter is what the layer panel's tag chooser writes; it is not "advanced" use.
  const onMark = filter !== null && tagOfMapFilter(filter) === null;
  // A view, a drawn outline or a typed point is the question of the moment, not something to store.
  const savable = filter?.kind === 'document' && !holdsPosition(filter.document);
  const selection = useWorkspaceStore((s) => s.selection);
  const selectedFeatureId = selection?.kind === 'feature' ? selection.featureId : null;
  // While an outline is being drawn the editor steps aside (kept mounted, so the row that asked
  // receives the outline) and the map is the whole screen.
  const [drawing, setDrawing] = useState(false);
  const sources = useMemo<SpatialSources>(() => ({
    currentView: () => viewOutline(mapLonLatExtent()),
    selectedFeatureId,
    drawOutline: async () => {
      setDrawing(true);
      message.info(t('mapFilter.drawHint'));
      try {
        return await drawFilterOutline(getWorkspaceMap());
      } finally {
        setDrawing(false);
      }
    },
  }), [selectedFeatureId, message, t]);

  const save = async () => {
    if (filter?.kind !== 'document') {
      message.warning(t('savedFilters.nothingToSave'));
      return;
    }
    const values = await form.validateFields();
    try {
      const created = await create.mutateAsync({
        name: values.name.trim(),
        description: values.description?.trim() || null,
        document: filter.document,
        ...audienceFields(values.audience),
      });
      setSaving(false);
      form.resetFields();
      message.success(t('mapFilter.saved', { name: created.name }));
      onChange({ kind: 'saved', savedFilterId: created.id });
      setOpen(false);
    } catch (error) {
      message.error(t(savedFilterSaveFailedKey(error)));
    }
  };

  return (
    <>
      <Button
        size="small"
        icon={<FilterOutlined />}
        type={onMark ? 'primary' : 'default'}
        onClick={() => setOpen(true)}
        data-testid="map-filter-edit"
      >
        {t('mapFilter.edit')}
      </Button>
      <Drawer
        open={open && !drawing}
        title={t('mapFilter.editTitle')}
        size="large"
        destroyOnHidden={!drawing}
        onClose={() => setOpen(false)}
      >
        <Flex vertical gap={16}>
          <Flex gap={8} wrap>
            <Select
              style={{ width: 260 }}
              placeholder={t('mapFilter.applySaved')}
              value={null}
              data-testid="map-filter-apply-saved"
              showSearch
              optionFilterProp="label"
              notFoundContent={t('mapFilter.noSaved')}
              options={(savedFilters ?? []).map((f) => ({
                value: f.id,
                label: f.name,
                // A filter about something else, or one this account can no longer run, would only
                // be refused by the map's routes.
                disabled: !appliesTo(f, MAP_FILTER_WORLD),
              }))}
              onChange={(id: string | null) => {
                if (id) {
                  onChange({ kind: 'saved', savedFilterId: id });
                  setOpen(false);
                }
              }}
            />
            <Tooltip title={filter?.kind === 'document' && !savable ? t('filters.editor.spatial.notSaved') : undefined}>
              <span>
                <Button
                  icon={<SaveOutlined />}
                  disabled={!savable}
                  onClick={() => setSaving(true)}
                  data-testid="map-filter-save"
                >
                  {t('mapFilter.saveAs')}
                </Button>
              </span>
            </Tooltip>
          </Flex>
          {/* Keyed by what it opens on, so a filter that moves while it is open (a link followed, a
              view applied) re-seeds it instead of leaving a draft of an older question to be applied
              over the newer one. */}
          <SpatialSourcesContext.Provider value={sources}>
            <FilterEditor
              key={JSON.stringify(seed)}
              world={MAP_FILTER_WORLD}
              document={seed}
              onCancel={() => setOpen(false)}
              onApply={(edited) => {
                onChange(mapFilterOfEditor(edited));
                setOpen(false);
              }}
            />
          </SpatialSourcesContext.Provider>
        </Flex>
      </Drawer>
      <Modal
        open={saving}
        title={t('mapFilter.saveTitle')}
        okText={t('common.save')}
        confirmLoading={create.isPending}
        onOk={() => void save()}
        onCancel={() => setSaving(false)}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" preserve={false} initialValues={{ audience: defaultAudience }}>
          <Form.Item
            name="name"
            label={t('savedFilters.name')}
            rules={[{ required: true, whitespace: true, max: 200 }]}
          >
            <Input data-testid="map-saved-filter-name" autoFocus />
          </Form.Item>
          <Form.Item name="description" label={t('savedFilters.description')} rules={[{ max: 2000 }]}>
            <Input.TextArea rows={3} data-testid="map-saved-filter-description" />
          </Form.Item>
          <Form.Item name="audience" label={t('savedFilters.audience')}>
            <SavedFilterAudienceField groups={groups} otherGroups={otherGroups} showDefaultLink testId="map-saved-filter-audience" />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}
