// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { InboxOutlined, PlayCircleOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  AutoComplete,
  Button,
  Card,
  Checkbox,
  Collapse,
  Flex,
  Form,
  Input,
  InputNumber,
  Select,
  Slider,
  Typography,
  Upload,
} from 'antd';
import type { UploadFile } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useSubmitTerrainBuild,
  useTerrainSourceDirectories,
  useUploadTerrainRaster,
  type TerrainBuildSourceRequest,
  type TerrainHeightDatum,
} from '../../../api/hooks.ts';
import { areaTooLarge, type TerrainBbox } from './terrainArea.ts';
import { DEFAULT_DEPTH, MAX_DEPTH, MIN_DEPTH, depthBand, depthExceedsCoverage } from './terrainDepth.ts';
import { terrainProblemMessage } from './terrainProblems.ts';
import TerrainAreaField from './TerrainAreaField.tsx';

/**
 * What the browser refuses to send before the server refuses to read it.
 *
 * The server caps a raster upload at the same figure. Without this the browser would push half a
 * gigabyte up a domestic connection and only then be told, which is the one refusal worth spending
 * a constant to avoid.
 */
const MAX_RASTER_BYTES = 512 * 1024 * 1024;

/** The extensions the pipeline will try to open; anything else is a wasted upload. */
const RASTER_EXTENSIONS = ['.asc', '.dem', '.hgt', '.img', '.tif', '.tiff'];

interface FormValues {
  area: TerrainBbox | null;
  fetchCoverage: boolean;
  maxDepth: number;
  attribution: string;
  licence: string;
  directory: string;
  heightDatum: TerrainHeightDatum;
  geoidHeightM: number;
}

const emptyValues: FormValues = {
  area: null,
  fetchCoverage: true,
  maxDepth: DEFAULT_DEPTH,
  attribution: '',
  licence: '',
  directory: '',
  heightDatum: 'orthometric',
  geoidHeightM: 0,
};

interface Props {
  /** Whether this caller may start a build at all; the whole card is hidden without it. */
  canExecute: boolean;
  /**
   * Whether this caller is a full administrator. Naming a directory on the server is a question
   * about the machine rather than about anything in it, and the server holds that higher bar — so
   * the control is not offered at all below it. A control that always refuses is worse than none.
   */
  isFullAdmin: boolean;
}

/**
 * Starts a build: the rectangle, where its elevation data comes from, and how deep to go.
 *
 * Uploaded rasters go up as they are chosen and come back as references a submit names, so the
 * submit itself carries no bytes. One credit covers every raster the operator supplies in this
 * build — uploads and a named directory alike — because they are in practice one dataset, and
 * asking for a separate credit per file would be a form nobody fills in honestly.
 */
export default function TerrainBuildForm({ canExecute, isFullAdmin }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<FormValues>();
  const submit = useSubmitTerrainBuild();
  const upload = useUploadTerrainRaster();
  const directories = useTerrainSourceDirectories(canExecute && isFullAdmin);

  const [files, setFiles] = useState<UploadFile[]>([]);
  // The references the server handed back for what has been sent, keyed by the file the browser
  // knows about, so removing a file from the list removes it from the build.
  const [references, setReferences] = useState<Record<string, string>>({});

  // A mirror of what is typed, kept by the form itself rather than read back out of it: the
  // rectangle, the depth and the sources each decide what else the form shows, and a form that
  // renders from its own store is a form whose warnings appear one keystroke late.
  // A field the form is not currently showing is absent from what it reports, so every read
  // falls back to what the form started with rather than to nothing.
  const [values, setValues] = useState<Partial<FormValues>>(emptyValues);
  const area = values.area ?? null;
  const fetchCoverage = values.fetchCoverage ?? emptyValues.fetchCoverage;
  const maxDepth = values.maxDepth ?? emptyValues.maxDepth;
  const directory = values.directory ?? '';
  const heightDatum = values.heightDatum ?? emptyValues.heightDatum;

  const uploadedReferences = files
    .map((file) => references[file.uid])
    .filter((reference): reference is string => !!reference);
  /**
   * The files chosen that the server is not holding: still going up, or gone up and failed.
   *
   * Read off the list rather than off the upload mutation. Several rasters go up at once and they
   * all share one mutation, whose pending flag follows whichever started last — so a small file
   * finishing first says "done" while a four-hundred-megabyte one is still climbing, and a file
   * that failed says "done" too. Either way the build would be submitted naming only the rasters
   * that happened to arrive, and a build silently missing the fine data it was started for is the
   * worst outcome this form has: it costs hours and looks like success.
   */
  const pendingUploads = files.filter((file) => !references[file.uid]);
  const hasOwnRasters = uploadedReferences.length > 0 || directory.trim().length > 0;
  const tooLarge = area != null && areaTooLarge(area);
  // Only worth saying when the coverage is the whole of what this build has to work from.
  const capped = fetchCoverage && depthExceedsCoverage(maxDepth, hasOwnRasters);

  const start = async (submitted: Partial<FormValues>) => {
    if (!submitted.area) {
      message.error(t('terrain.form.areaRequired'));
      return;
    }
    // Checked here as well as on the button: a file can fail while the pointer is on its way down.
    if (pendingUploads.length > 0) {
      message.error(
        t('terrain.form.uploadsPending', {
          files: pendingUploads.map((file) => file.name).join(', '),
        }),
      );
      return;
    }
    const attribution = (submitted.attribution ?? '').trim();
    const licence = (submitted.licence ?? '').trim() || null;
    const named = (submitted.directory ?? '').trim();
    const wantsCoverage = submitted.fetchCoverage ?? emptyValues.fetchCoverage;
    const datum = submitted.heightDatum ?? emptyValues.heightDatum;
    const sources: TerrainBuildSourceRequest[] = [
      ...uploadedReferences.map((reference) => ({
        kind: 'uploaded' as const,
        reference,
        attribution,
        licence,
      })),
      ...(named
        ? [{ kind: 'serverDirectory' as const, reference: named, attribution, licence }]
        : []),
    ];
    if (sources.length > 0 && !attribution) {
      message.error(t('terrain.form.attributionRequired'));
      return;
    }
    if (!wantsCoverage && sources.length === 0) {
      message.error(t('terrain.problems.noSources'));
      return;
    }

    const [west, south, east, north] = submitted.area;
    try {
      await submit.mutateAsync({
        west,
        south,
        east,
        north,
        maxDepth: submitted.maxDepth ?? emptyValues.maxDepth,
        heightDatum: datum,
        // Only meaningful when the heights are measured from the ellipsoid; sending anything else
        // for an orthometric build would record a correction that is never applied.
        geoidHeightM: datum === 'ellipsoidal' ? (submitted.geoidHeightM ?? 0) : 0,
        fetchCoverage: wantsCoverage,
        sources,
      });
      message.success(t('terrain.form.started'));
      form.resetFields();
      // Resetting the store does not report a change, so the mirror is put back by hand.
      setValues(emptyValues);
      setFiles([]);
      setReferences({});
    } catch (error) {
      message.error(terrainProblemMessage(error, t));
    }
  };

  if (!canExecute) {
    return null;
  }

  return (
    <Card title={t('terrain.form.title')} size="small">
      <Form
        form={form}
        layout="vertical"
        initialValues={emptyValues}
        onValuesChange={(_, all: FormValues) => setValues(all)}
        onFinish={(submitted) => void start(submitted)}
      >
        <Form.Item name="area" label={t('terrain.form.area')} required>
          <TerrainAreaField />
        </Form.Item>
        {tooLarge && (
          <Alert
            type="warning"
            showIcon
            style={{ marginBottom: 16 }}
            data-testid="terrain-area-too-large"
            message={t('terrain.form.areaTooLarge')}
          />
        )}

        <Typography.Title level={5}>{t('terrain.form.sources')}</Typography.Title>
        <Form.Item name="fetchCoverage" valuePropName="checked" style={{ marginBottom: 8 }}>
          <Checkbox>{t('terrain.form.fetchCoverage')}</Checkbox>
        </Form.Item>
        <Typography.Paragraph type="secondary">
          {t('terrain.form.fetchCoverageHint')}
        </Typography.Paragraph>

        <Form.Item label={t('terrain.form.upload')}>
          <Upload.Dragger
            multiple
            fileList={files}
            accept={RASTER_EXTENSIONS.join(',')}
            beforeUpload={(file) => {
              if (file.size > MAX_RASTER_BYTES) {
                message.error(t('terrain.form.rasterTooLarge'));
                return Upload.LIST_IGNORE;
              }
              return true;
            }}
            customRequest={({ file, onSuccess, onError }) => {
              const chosen = file as File & { uid: string };
              upload
                .mutateAsync(chosen)
                .then((result) => {
                  setReferences((current) => ({ ...current, [chosen.uid]: result.reference }));
                  onSuccess?.(result);
                })
                .catch((error: unknown) => {
                  message.error(terrainProblemMessage(error, t));
                  onError?.(error as Error);
                });
            }}
            onChange={({ fileList }) => setFiles(fileList)}
            onRemove={(file) => {
              setReferences((current) => {
                const next = { ...current };
                delete next[file.uid];
                return next;
              });
              return true;
            }}
          >
            <p className="ant-upload-drag-icon">
              <InboxOutlined />
            </p>
            <p className="ant-upload-text">{t('terrain.form.uploadHint')}</p>
            <p className="ant-upload-hint">
              {t('terrain.form.uploadFormats', { formats: RASTER_EXTENSIONS.join(' ') })}
            </p>
          </Upload.Dragger>
          {pendingUploads.length > 0 && (
            <Typography.Text type="warning" data-testid="terrain-uploads-pending">
              {t('terrain.form.uploadsPending', {
                files: pendingUploads.map((file) => file.name).join(', '),
              })}
            </Typography.Text>
          )}
        </Form.Item>

        {isFullAdmin && (
          <Form.Item
            name="directory"
            label={t('terrain.form.directory')}
            extra={t('terrain.form.directoryHint')}
          >
            <AutoComplete
              allowClear
              data-testid="terrain-directory"
              options={(directories.data?.roots ?? []).map((root) => ({ value: root }))}
              placeholder={t('terrain.form.directoryPlaceholder')}
            />
          </Form.Item>
        )}
        {isFullAdmin && directories.data?.roots.length === 0 && (
          <Alert
            type="info"
            showIcon
            style={{ marginBottom: 16 }}
            message={t('terrain.problems.noRoots')}
          />
        )}

        {(files.length > 0 || directory.trim().length > 0) && (
          <Flex gap={16} wrap>
            <Form.Item
              name="attribution"
              label={t('terrain.form.attribution')}
              extra={t('terrain.form.attributionHint')}
              rules={[{ required: true, message: t('terrain.form.attributionRequired') }]}
              style={{ flex: '1 1 320px' }}
            >
              <Input maxLength={500} />
            </Form.Item>
            <Form.Item
              name="licence"
              label={t('terrain.form.licence')}
              style={{ flex: '1 1 240px' }}
            >
              <Input maxLength={200} />
            </Form.Item>
          </Flex>
        )}

        <Typography.Title level={5}>{t('terrain.form.depth')}</Typography.Title>
        {/* Two controls over one field: dragging is how a depth is chosen, typing is how one
            already decided on is entered, and antd keeps both on the same value. */}
        <Flex gap={16} align="start">
          <Form.Item name="maxDepth" style={{ flex: 1, marginBottom: 0 }}>
            <Slider
              min={MIN_DEPTH}
              max={MAX_DEPTH}
              marks={{
                [MIN_DEPTH]: String(MIN_DEPTH),
                [DEFAULT_DEPTH]: String(DEFAULT_DEPTH),
                [MAX_DEPTH]: String(MAX_DEPTH),
              }}
            />
          </Form.Item>
          <Form.Item name="maxDepth" label={t('terrain.form.depth')} noStyle>
            <InputNumber
              min={MIN_DEPTH}
              max={MAX_DEPTH}
              aria-label={t('terrain.form.depth')}
              data-testid="terrain-depth"
            />
          </Form.Item>
        </Flex>
        <Typography.Paragraph type="secondary" data-testid="terrain-depth-meaning">
          {t(`terrain.depthBands.${depthBand(maxDepth)}`, { depth: maxDepth })}
        </Typography.Paragraph>
        {capped && (
          <Alert
            type="warning"
            showIcon
            style={{ marginBottom: 16 }}
            data-testid="terrain-depth-capped"
            message={t('terrain.form.depthCapped')}
            description={t('terrain.form.depthCappedDetail')}
          />
        )}

        <Collapse
          ghost
          size="small"
          style={{ marginBottom: 16 }}
          items={[
            {
              key: 'heights',
              label: t('terrain.form.heights'),
              children: (
                <Flex gap={16} wrap>
                  <Form.Item
                    name="heightDatum"
                    label={t('terrain.form.heightDatum')}
                    extra={t('terrain.form.heightDatumHint')}
                    style={{ flex: '1 1 240px' }}
                  >
                    <Select
                      options={[
                        { value: 'orthometric', label: t('terrain.heightDatums.orthometric') },
                        { value: 'ellipsoidal', label: t('terrain.heightDatums.ellipsoidal') },
                      ]}
                    />
                  </Form.Item>
                  {heightDatum === 'ellipsoidal' && (
                    <Form.Item
                      name="geoidHeightM"
                      label={t('terrain.form.geoidHeight')}
                      extra={t('terrain.form.geoidHeightHint')}
                      style={{ flex: '1 1 240px' }}
                    >
                      <InputNumber min={-200} max={200} step={0.1} style={{ width: '100%' }} />
                    </Form.Item>
                  )}
                </Flex>
              ),
            },
          ]}
        />

        {/*
          Said before the button rather than after the pipeline. Nothing the server publishes says
          whether the tile-making service is running here, and the step that needs it comes after
          the data has been obtained and prepared — which on a county-sized rectangle is hours of
          work and gigabytes of disk. Somebody about to spend that should know in one sentence what
          this installation may not be able to finish.
        */}
        <Typography.Paragraph type="secondary" data-testid="terrain-bake-service">
          {t('terrain.form.bakeService')}
        </Typography.Paragraph>

        <Button
          type="primary"
          htmlType="submit"
          icon={<PlayCircleOutlined />}
          loading={submit.isPending}
          disabled={!area || tooLarge || pendingUploads.length > 0}
        >
          {t('terrain.form.start')}
        </Button>
      </Form>
    </Card>
  );
}
