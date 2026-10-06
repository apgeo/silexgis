// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, PictureOutlined, PlusOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Card,
  Checkbox,
  ColorPicker,
  Flex,
  Form,
  Input,
  InputNumber,
  Select,
  Space,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import i18n from '../../../i18n';
import {
  useRequestTerrainDerivative,
  useTerrainBuilds,
  type TerrainBuild,
  type TerrainDerivativeCreate,
} from '../../../api/hooks.ts';
import { formatBbox } from './terrainArea.ts';
import { geometryBbox } from './terrainBuild.ts';
import {
  DEFAULT_ALTITUDE,
  DEFAULT_AZIMUTH,
  DEFAULT_COLOUR_RAMP,
  DEFAULT_CONTOUR_INTERVAL,
  DEFAULT_Z_FACTOR,
  DERIVATIVE_BUILD_CHOICES,
  HILLSHADE_LIGHTINGS,
  MAX_CONTOUR_INTERVAL,
  MAX_NAME_LENGTH,
  MAX_Z_FACTOR,
  MIN_CONTOUR_INTERVAL,
  RUGGEDNESS_FITS,
  SLOPE_UNITS,
  SURFACE_FITS,
  TERRAIN_DERIVATIVE_KINDS,
  altitudeValid,
  azimuthValid,
  colourRampProblem,
  contourIntervalValid,
  defaultDerivativeName,
  hexToRgb,
  readsColourRamp,
  readsContourInterval,
  readsEdges,
  readsLight,
  readsRuggednessFit,
  readsSlopeUnit,
  readsSurfaceFit,
  zFactorValid,
  type ColourStopDraft,
  type TerrainDerivativeKind,
  type TerrainHillshadeLighting,
  type TerrainRuggednessFit,
  type TerrainSlopeUnit,
  type TerrainSurfaceFit,
} from './terrainDerivative.ts';
import { terrainProblemMessage } from './terrainProblems.ts';

interface FormValues {
  terrainBuildId: string | undefined;
  derivative: TerrainDerivativeKind;
  name: string;
  lighting: TerrainHillshadeLighting;
  azimuthDegrees: number;
  altitudeDegrees: number;
  zFactor: number;
  surfaceFit: TerrainSurfaceFit;
  slopeUnit: TerrainSlopeUnit;
  ruggednessFit: TerrainRuggednessFit;
  computeEdges: boolean;
  colourRamp: ColourStopDraft[];
  contourIntervalMetres: number;
}

interface Props {
  /** Whether this caller may ask for a picture; without it the form is shown but cannot send. */
  canExecute: boolean;
}

/**
 * Asks for a picture of the ground to be computed from one finished build.
 *
 * The form opens on the build the scene is drawing, because a picture of any other ground would
 * be out of date the moment it was finished. Each kind of picture shows only the settings it
 * reads, and only those are sent: the server fills the rest with its defaults and strips them
 * again before deciding whether this is a picture it already has, so sending a slope unit with a
 * hillshade would change nothing about the file and only muddle what the register says about it.
 *
 * Shown, disabled, to somebody who may read the register but not add to it, with one line saying
 * which right is missing. The register below is theirs to read, and a form they can see but not
 * send tells them what the right would let them do.
 */
export default function TerrainDerivativeForm({ canExecute }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<FormValues>();
  const request = useRequestTerrainDerivative();
  const { data: builds } = useTerrainBuilds({ page: 1, pageSize: DERIVATIVE_BUILD_CHOICES });

  const finished = (builds?.items ?? []).filter((build) => build.status === 'succeeded');
  const drawn = finished.find((build) => build.isActive) ?? finished[0];

  // A mirror of what is chosen, kept by the form itself: which settings are shown is decided by
  // the kind, and a form that renders from its own store shows them one keystroke late.
  const [kind, setKind] = useState<TerrainDerivativeKind>('hillshade');
  const [lighting, setLighting] = useState<TerrainHillshadeLighting>('single');

  const initial: FormValues = {
    terrainBuildId: drawn?.id,
    derivative: 'hillshade',
    name: defaultDerivativeName('hillshade', t),
    lighting: 'single',
    azimuthDegrees: DEFAULT_AZIMUTH,
    altitudeDegrees: DEFAULT_ALTITUDE,
    zFactor: DEFAULT_Z_FACTOR,
    surfaceFit: 'horn',
    slopeUnit: 'degrees',
    ruggednessFit: 'riley',
    computeEdges: true,
    colourRamp: DEFAULT_COLOUR_RAMP.map((stop) => ({ ...stop })),
    contourIntervalMetres: DEFAULT_CONTOUR_INTERVAL,
  };

  const buildLabel = (build: TerrainBuild): string => {
    const bbox = geometryBbox(build.extent);
    const parts = [
      bbox === null ? build.id : formatBbox(bbox),
      `${t('terrain.list.depth')} ${build.requestedMaxDepth}`,
      new Date(build.createdAt).toLocaleDateString(i18n.resolvedLanguage),
    ];
    if (build.isActive) {
      parts.push(t('terrain.derivativeForm.buildCurrent'));
    }
    return parts.join(' · ');
  };

  const changeKind = (next: TerrainDerivativeKind) => {
    // The name follows the kind only while it is still the name the kind gave it; one somebody
    // typed is theirs and stays.
    if ((form.getFieldValue('name') as string) === defaultDerivativeName(kind, t)) {
      form.setFieldValue('name', defaultDerivativeName(next, t));
    }
    setKind(next);
  };

  const send = async (values: FormValues) => {
    if (!values.terrainBuildId) {
      message.error(t('terrain.derivativeForm.buildRequired'));
      return;
    }
    const chosen = values.derivative;
    const lit = readsLight(chosen);
    const body: TerrainDerivativeCreate = {
      terrainBuildId: values.terrainBuildId,
      derivative: chosen,
      name: values.name.trim(),
      lighting: lit ? values.lighting : null,
      // Four lights stand at fixed compass points, so a direction sent with them would be
      // recorded against a picture that was never lit from it.
      azimuthDegrees: lit && values.lighting === 'single' ? values.azimuthDegrees : null,
      altitudeDegrees: lit ? values.altitudeDegrees : null,
      zFactor: lit ? values.zFactor : null,
      surfaceFit: readsSurfaceFit(chosen) ? values.surfaceFit : null,
      slopeUnit: readsSlopeUnit(chosen) ? values.slopeUnit : null,
      ruggednessFit: readsRuggednessFit(chosen) ? values.ruggednessFit : null,
      computeEdges: readsEdges(chosen) ? values.computeEdges : null,
      contourIntervalMetres: readsContourInterval(chosen) ? values.contourIntervalMetres : null,
      colourRamp: readsColourRamp(chosen)
        ? values.colourRamp.map((stop) => ({
            elevation: stop.elevation ?? 0,
            ...hexToRgb(stop.colour),
            alpha: 255,
          }))
        : null,
    };
    try {
      await request.mutateAsync(body);
      message.success(t('terrain.derivativeForm.requested'));
    } catch (error) {
      message.error(terrainProblemMessage(error, t));
    }
  };

  const noBuilds = builds !== undefined && finished.length === 0;

  return (
    <Card
      title={
        <>
          <PictureOutlined /> {t('terrain.derivativeForm.title')}
        </>
      }
      size="small"
      data-testid="terrain-derivative-form"
    >
      <Typography.Paragraph type="secondary">{t('terrain.derivativeForm.intro')}</Typography.Paragraph>
      {!canExecute && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          data-testid="terrain-derivative-needs-execute"
          title={t('terrain.derivativeForm.needsExecute')}
        />
      )}
      {canExecute && noBuilds && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          data-testid="terrain-derivative-no-builds"
          title={t('terrain.derivativeForm.noBuilds')}
        />
      )}
      <Form
        form={form}
        layout="vertical"
        disabled={!canExecute}
        initialValues={initial}
        onFinish={(values) => void send(values)}
      >
        <Flex gap={16} wrap>
          <Form.Item
            name="terrainBuildId"
            label={t('terrain.derivativeForm.build')}
            rules={[{ required: true, message: t('terrain.derivativeForm.buildRequired') }]}
            style={{ flex: '2 1 320px' }}
          >
            <Select
              data-testid="terrain-derivative-build"
              placeholder={t('terrain.derivativeForm.buildPlaceholder')}
              options={finished.map((build) => ({ value: build.id, label: buildLabel(build) }))}
            />
          </Form.Item>
          <Form.Item
            name="derivative"
            label={t('terrain.derivativeForm.kind')}
            style={{ flex: '1 1 240px' }}
          >
            <Select
              data-testid="terrain-derivative-kind"
              onChange={changeKind}
              options={TERRAIN_DERIVATIVE_KINDS.map((each) => ({
                value: each,
                label: t(`terrain.derivatives.kinds.${each}`),
              }))}
            />
          </Form.Item>
          <Form.Item
            name="name"
            label={t('terrain.derivativeForm.name')}
            extra={t('terrain.derivativeForm.nameHint')}
            rules={[
              {
                required: true,
                whitespace: true,
                message: t('terrain.derivativeForm.nameRequired'),
              },
              {
                max: MAX_NAME_LENGTH,
                message: t('terrain.derivativeForm.nameTooLong', { max: MAX_NAME_LENGTH }),
              },
            ]}
            style={{ flex: '1 1 240px' }}
          >
            <Input data-testid="terrain-derivative-name" maxLength={MAX_NAME_LENGTH} />
          </Form.Item>
        </Flex>

        {readsLight(kind) && (
          <Flex gap={16} wrap>
            <Form.Item
              name="lighting"
              label={t('terrain.derivativeForm.lighting')}
              extra={t('terrain.derivativeForm.lightingHint')}
              style={{ flex: '1 1 200px' }}
            >
              <Select
                data-testid="terrain-derivative-lighting"
                onChange={setLighting}
                options={HILLSHADE_LIGHTINGS.map((each) => ({
                  value: each,
                  label: t(`terrain.derivativeForm.lightings.${each}`),
                }))}
              />
            </Form.Item>
            {lighting === 'single' && (
              <Form.Item
                name="azimuthDegrees"
                label={t('terrain.derivativeForm.azimuth')}
                extra={t('terrain.derivativeForm.azimuthHint')}
                rules={[
                  {
                    validator: (_, value: number) =>
                      azimuthValid(value)
                        ? Promise.resolve()
                        : Promise.reject(new Error(t('terrain.derivativeForm.azimuthInvalid'))),
                  },
                ]}
                style={{ flex: '1 1 160px' }}
              >
                <InputNumber
                  data-testid="terrain-derivative-azimuth"
                  min={0}
                  max={359.9}
                  step={5}
                  style={{ width: '100%' }}
                />
              </Form.Item>
            )}
            <Form.Item
              name="altitudeDegrees"
              label={t('terrain.derivativeForm.altitude')}
              rules={[
                {
                  validator: (_, value: number) =>
                    altitudeValid(value)
                      ? Promise.resolve()
                      : Promise.reject(new Error(t('terrain.derivativeForm.altitudeInvalid'))),
                },
              ]}
              style={{ flex: '1 1 160px' }}
            >
              <InputNumber
                data-testid="terrain-derivative-altitude"
                min={0}
                max={90}
                step={5}
                style={{ width: '100%' }}
              />
            </Form.Item>
            <Form.Item
              name="zFactor"
              label={t('terrain.derivativeForm.zFactor')}
              extra={t('terrain.derivativeForm.zFactorHint')}
              rules={[
                {
                  validator: (_, value: number) =>
                    zFactorValid(value)
                      ? Promise.resolve()
                      : Promise.reject(
                          new Error(t('terrain.derivativeForm.zFactorInvalid', { max: MAX_Z_FACTOR })),
                        ),
                },
              ]}
              style={{ flex: '1 1 160px' }}
            >
              <InputNumber
                data-testid="terrain-derivative-zfactor"
                min={0}
                max={MAX_Z_FACTOR}
                step={0.5}
                style={{ width: '100%' }}
              />
            </Form.Item>
          </Flex>
        )}

        {(readsSurfaceFit(kind) || readsSlopeUnit(kind) || readsRuggednessFit(kind)) && (
          <Flex gap={16} wrap>
            {readsSurfaceFit(kind) && (
              <Form.Item
                name="surfaceFit"
                label={t('terrain.derivativeForm.surfaceFit')}
                extra={t('terrain.derivativeForm.surfaceFitHint')}
                style={{ flex: '1 1 280px' }}
              >
                <Select
                  data-testid="terrain-derivative-surface-fit"
                  options={SURFACE_FITS.map((each) => ({
                    value: each,
                    label: t(`terrain.derivativeForm.surfaceFits.${each}`),
                  }))}
                />
              </Form.Item>
            )}
            {readsSlopeUnit(kind) && (
              <Form.Item
                name="slopeUnit"
                label={t('terrain.derivativeForm.slopeUnit')}
                style={{ flex: '1 1 240px' }}
              >
                <Select
                  data-testid="terrain-derivative-slope-unit"
                  options={SLOPE_UNITS.map((each) => ({
                    value: each,
                    label: t(`terrain.derivativeForm.slopeUnits.${each}`),
                  }))}
                />
              </Form.Item>
            )}
            {readsRuggednessFit(kind) && (
              <Form.Item
                name="ruggednessFit"
                label={t('terrain.derivativeForm.ruggednessFit')}
                extra={t('terrain.derivativeForm.ruggednessFitHint')}
                style={{ flex: '1 1 280px' }}
              >
                <Select
                  data-testid="terrain-derivative-ruggedness-fit"
                  options={RUGGEDNESS_FITS.map((each) => ({
                    value: each,
                    label: t(`terrain.derivativeForm.ruggednessFits.${each}`),
                  }))}
                />
              </Form.Item>
            )}
          </Flex>
        )}

        {readsColourRamp(kind) && (
          <Form.List
            name="colourRamp"
            rules={[
              {
                validator: (_, stops: ColourStopDraft[]) => {
                  const problem = colourRampProblem(stops ?? []);
                  return problem === null
                    ? Promise.resolve()
                    : Promise.reject(new Error(t(`terrain.derivativeForm.rampProblems.${problem}`)));
                },
              },
            ]}
          >
            {(fields, { add, remove }, { errors }) => (
              <Space orientation="vertical" size={4} style={{ width: '100%', marginBottom: 16 }}>
                <Typography.Text strong>{t('terrain.derivativeForm.colourRamp')}</Typography.Text>
                <Typography.Text type="secondary">
                  {t('terrain.derivativeForm.colourRampHint')}
                </Typography.Text>
                {fields.map((field) => (
                  <Flex key={field.key} gap={8} align="baseline" data-testid="terrain-derivative-stop">
                    <Form.Item
                      name={[field.name, 'elevation']}
                      style={{ marginBottom: 8 }}
                      rules={[{ required: true, message: t('terrain.derivativeForm.rampProblems.heightMissing') }]}
                    >
                      <InputNumber
                        aria-label={t('terrain.derivativeForm.stopElevation')}
                        placeholder={t('terrain.derivativeForm.stopElevation')}
                        step={50}
                      />
                    </Form.Item>
                    {/* Held as the `#rrggbb` text rather than the picker's own colour object, so
                        the value the form carries is the one the request is built from. */}
                    <Form.Item
                      name={[field.name, 'colour']}
                      style={{ marginBottom: 8 }}
                      getValueFromEvent={(_colour: unknown, hex: string) => hex}
                    >
                      <ColorPicker format="hex" disabledAlpha showText />
                    </Form.Item>
                    <Button
                      type="text"
                      danger
                      icon={<DeleteOutlined />}
                      aria-label={t('terrain.derivativeForm.removeStop')}
                      onClick={() => remove(field.name)}
                    />
                  </Flex>
                ))}
                <Button
                  type="dashed"
                  icon={<PlusOutlined />}
                  data-testid="terrain-derivative-add-stop"
                  onClick={() => add({ elevation: null, colour: '#808080' })}
                >
                  {t('terrain.derivativeForm.addStop')}
                </Button>
                <Form.ErrorList errors={errors} />
              </Space>
            )}
          </Form.List>
        )}

        {readsContourInterval(kind) && (
          <Form.Item
            name="contourIntervalMetres"
            label={t('terrain.derivativeForm.contourInterval')}
            extra={t('terrain.derivativeForm.contourIntervalHint')}
            rules={[
              {
                validator: (_, value: number) =>
                  contourIntervalValid(value)
                    ? Promise.resolve()
                    : Promise.reject(
                        new Error(
                          t('terrain.derivativeForm.contourIntervalInvalid', {
                            min: MIN_CONTOUR_INTERVAL,
                            max: MAX_CONTOUR_INTERVAL,
                          }),
                        ),
                      ),
              },
            ]}
            style={{ maxWidth: 320 }}
          >
            <InputNumber
              data-testid="terrain-derivative-contour-interval"
              min={0}
              max={MAX_CONTOUR_INTERVAL}
              step={5}
              style={{ width: '100%' }}
            />
          </Form.Item>
        )}

        {readsEdges(kind) && (
          <Form.Item
            name="computeEdges"
            valuePropName="checked"
            extra={t('terrain.derivativeForm.computeEdgesHint')}
          >
            <Checkbox data-testid="terrain-derivative-edges">
              {t('terrain.derivativeForm.computeEdges')}
            </Checkbox>
          </Form.Item>
        )}

        <Button
          type="primary"
          htmlType="submit"
          icon={<PictureOutlined />}
          data-testid="terrain-derivative-submit"
          loading={request.isPending}
          disabled={!canExecute || finished.length === 0}
        >
          {t('terrain.derivativeForm.submit')}
        </Button>
      </Form>
    </Card>
  );
}
