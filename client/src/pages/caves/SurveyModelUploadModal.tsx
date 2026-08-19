// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { InboxOutlined } from '@ant-design/icons';
import { Alert, App, Form, InputNumber, Modal, Radio, Typography, Upload } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCaveSummary,
  useUploadSurveyModel,
  type SurveyMeshDeclaration,
} from '../../api/hooks.ts';
import { formatSize } from '../../components/attachments/fileFormat.ts';
import { surveyModelProblemMessage } from './surveyModelProblems.ts';

/** What the server accepts, and the cap it refuses past. */
const ACCEPTED_EXTENSIONS = '.lox,.3d,.stl';
const MAX_MODEL_BYTES = 100 * 1024 * 1024;

/** Deepest and highest a cave's zero level can plausibly sit, matching what the server accepts. */
const LOWEST_HEIGHT_M = -500;
const HIGHEST_HEIGHT_M = 9000;

/**
 * A wall mesh is bare triangles with no coordinate system in the file, so its uploader has to
 * declare one. The line-plot formats carry their own georeferencing and are asked nothing.
 */
function isWallMesh(file: File | undefined): boolean {
  return file !== undefined && file.name.toLowerCase().endsWith('.stl');
}

interface DeclarationForm {
  coordinates: 'local' | 'projected';
  sourceEpsg?: number;
  originLongitude?: number;
  originLatitude?: number;
  originHeightM?: number;
}

/**
 * Uploading a survey model, and — for a wall mesh — collecting what the conversion cannot work out
 * for itself.
 *
 * <b>Why the questions are asked at all.</b> An `.stl` is a triangle soup: no coordinate system, no
 * datum, nothing that says where on Earth the numbers are. Read as the neighbouring projected zone
 * the very same file lands hundreds of kilometres away, in a valid-looking place, so the system is
 * declared rather than guessed. And the height in these files is measured from the export's own
 * origin, never above sea level — the highest value across a set of real cave exports was 38 m, in
 * a country whose caves sit between 200 and 2000 — so the altitude of the file's zero plane is
 * asked for separately.
 *
 * <b>Why the origin is only sometimes offered.</b> A local export is the ordinary case and the
 * cave's own entrance is usually its origin, so the entrance position is filled in — but a cave
 * whose location is protected shows a deliberately-blurred position to accounts without
 * exact-location access, and seeding the form with that would write a wrong anchor into the
 * database and look like a successful upload. The offer is made only when the server says the
 * position this account holds is the true one.
 */
export default function SurveyModelUploadModal({
  caveId,
  open,
  onClose,
}: {
  caveId: string;
  open: boolean;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<DeclarationForm>();
  const [file, setFile] = useState<File>();
  const [tooLarge, setTooLarge] = useState(false);
  const upload = useUploadSurveyModel();
  const { data: summary } = useCaveSummary(caveId);

  // The true position, or nothing. Both halves are read: the capability the caller was granted,
  // and the record's own statement that what it sent is not blurred.
  const entrance = summary?.mainEntrance;
  const exactOrigin =
    summary?.permissions.canViewExactLocation === true &&
    entrance?.approximateLocation === false &&
    entrance.geom
      ? {
          longitude: Number(entrance.geom.coordinates[0]),
          latitude: Number(entrance.geom.coordinates[1]),
        }
      : undefined;

  // Why there is nothing to offer, which is three different things and must not be said as one.
  //
  // Only one of them is a protection decision. Telling somebody their own unprotected cave is
  // being blurred from them is simply false, and it invites a support question about a rule that
  // is not in force — so the blurring is claimed only when the server actually said so, a cave
  // with no entrance position on record is described as what it is, and a summary still on its way
  // says nothing at all rather than accusing the installation of something while it loads.
  const originProblem: 'obfuscated' | 'noEntrance' | undefined = !summary
    ? undefined
    : exactOrigin
      ? undefined
      : summary.permissions.canViewExactLocation !== true || entrance?.approximateLocation === true
        ? 'obfuscated'
        : 'noEntrance';

  const mesh = isWallMesh(file);
  const coordinates = Form.useWatch('coordinates', form) ?? 'local';

  // The two halves separately, because the effect below depends on the numbers rather than on the
  // object holding them: a fresh object every render would re-run it on every render.
  const originLongitude = exactOrigin?.longitude;
  const originLatitude = exactOrigin?.latitude;

  // Initial values are read once, when the form first mounts, and the cave summary they come from
  // is a request that can answer after a file has already been chosen. Without this the note would
  // say the fields were filled in from the entrance while sitting over two empty required ones.
  // Anything the uploader has typed themselves wins: this only ever fills a field nobody touched.
  useEffect(() => {
    if (!mesh || originLongitude === undefined || originLatitude === undefined) {
      return;
    }
    const fill: Partial<DeclarationForm> = {};
    if (!form.isFieldTouched('originLongitude') && form.getFieldValue('originLongitude') === undefined) {
      fill.originLongitude = originLongitude;
    }
    if (!form.isFieldTouched('originLatitude') && form.getFieldValue('originLatitude') === undefined) {
      fill.originLatitude = originLatitude;
    }
    if (Object.keys(fill).length > 0) {
      form.setFieldsValue(fill);
    }
  }, [mesh, originLongitude, originLatitude, form]);

  const close = () => {
    form.resetFields();
    setFile(undefined);
    setTooLarge(false);
    onClose();
  };

  const submit = async () => {
    if (!file) {
      return;
    }
    let declaration: SurveyMeshDeclaration | undefined;
    if (mesh) {
      let values: DeclarationForm;
      try {
        values = await form.validateFields();
      } catch {
        // The form has already marked the fields it is unhappy with; nothing else to say.
        return;
      }
      // Written as two whole literals rather than one with a spread in it. A spread carries its
      // properties in without being checked against the target type, so a field the server had
      // renamed would still compile here and be refused at run time by every upload; a literal
      // assigned straight to the contract's own type is checked name by name.
      declaration =
        values.coordinates === 'projected'
          ? { originHeightM: values.originHeightM!, sourceEpsg: values.sourceEpsg }
          : {
              originHeightM: values.originHeightM!,
              originLongitude: values.originLongitude,
              originLatitude: values.originLatitude,
            };
    }
    try {
      await upload.mutateAsync({ caveId, file, declaration });
      message.success(t('surveyModels.uploaded'));
      close();
    } catch (error) {
      message.error(surveyModelProblemMessage(error, t));
    }
  };

  return (
    <Modal
      title={t('surveyModels.uploadTitle')}
      open={open}
      onCancel={close}
      onOk={() => void submit()}
      okText={t('surveyModels.upload')}
      okButtonProps={{ disabled: !file || tooLarge, loading: upload.isPending }}
      confirmLoading={upload.isPending}
      width="min(640px, 95vw)"
      destroyOnHidden
    >
      <Upload.Dragger
        accept={ACCEPTED_EXTENSIONS}
        multiple={false}
        maxCount={1}
        showUploadList={false}
        beforeUpload={(chosen) => {
          setFile(chosen);
          setTooLarge(chosen.size > MAX_MODEL_BYTES);
          return false;
        }}
        data-testid="survey-model-file"
      >
        <p className="ant-upload-drag-icon">
          <InboxOutlined />
        </p>
        <p className="ant-upload-text">{file ? file.name : t('surveyModels.chooseFile')}</p>
        <p className="ant-upload-hint">
          {file ? formatSize(file.size) : t('surveyModels.acceptHint')}
        </p>
      </Upload.Dragger>

      {tooLarge && (
        <Alert
          type="error"
          showIcon
          style={{ marginTop: 12 }}
          title={t('surveyModels.problems.sizeInvalid')}
        />
      )}

      {mesh && (
        <Form
          form={form}
          layout="vertical"
          style={{ marginTop: 16 }}
          initialValues={{
            coordinates: 'local',
            originLongitude: exactOrigin?.longitude,
            originLatitude: exactOrigin?.latitude,
          }}
        >
          <Typography.Text strong>{t('surveyModels.meshDeclaration')}</Typography.Text>
          <Typography.Paragraph type="secondary" style={{ marginTop: 4 }}>
            {t('surveyModels.meshDeclarationHint')}
          </Typography.Paragraph>

          <Form.Item name="coordinates">
            <Radio.Group>
              <Radio value="local">{t('surveyModels.coordinatesLocal')}</Radio>
              <Radio value="projected">{t('surveyModels.coordinatesProjected')}</Radio>
            </Radio.Group>
          </Form.Item>

          {coordinates === 'projected' ? (
            <>
              <Typography.Paragraph type="secondary">
                {t('surveyModels.coordinatesProjectedHint')}
              </Typography.Paragraph>
              <Form.Item
                name="sourceEpsg"
                label={t('surveyModels.sourceEpsg')}
                rules={[{ required: true, message: t('surveyModels.problems.crsInvalid') }]}
              >
                <InputNumber min={1} step={1} style={{ width: 200 }} />
              </Form.Item>
            </>
          ) : (
            <>
              <Typography.Paragraph type="secondary">
                {t('surveyModels.coordinatesLocalHint')}
              </Typography.Paragraph>
              {exactOrigin && (
                <Alert
                  type="info"
                  showIcon
                  style={{ marginBottom: 12 }}
                  title={t('surveyModels.originFromEntrance')}
                />
              )}
              {originProblem && (
                <Alert
                  type="warning"
                  showIcon
                  style={{ marginBottom: 12 }}
                  title={
                    originProblem === 'obfuscated'
                      ? t('surveyModels.originObfuscated')
                      : t('surveyModels.originNoEntrance')
                  }
                />
              )}
              <Form.Item
                name="originLongitude"
                label={t('surveyModels.originLongitude')}
                rules={[{ required: true, message: t('surveyModels.problems.originInvalid') }]}
              >
                <InputNumber min={-180} max={180} step={0.00001} style={{ width: 200 }} />
              </Form.Item>
              <Form.Item
                name="originLatitude"
                label={t('surveyModels.originLatitude')}
                rules={[{ required: true, message: t('surveyModels.problems.originInvalid') }]}
              >
                <InputNumber min={-90} max={90} step={0.00001} style={{ width: 200 }} />
              </Form.Item>
            </>
          )}

          <Form.Item
            name="originHeightM"
            label={t('surveyModels.originHeightM')}
            extra={t('surveyModels.originHeightHint')}
            rules={[{ required: true, message: t('surveyModels.problems.heightInvalid') }]}
          >
            <InputNumber
              min={LOWEST_HEIGHT_M}
              max={HIGHEST_HEIGHT_M}
              step={1}
              style={{ width: 200 }}
            />
          </Form.Item>
        </Form>
      )}
    </Modal>
  );
}
