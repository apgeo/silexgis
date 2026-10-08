// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import {
  App,
  Button,
  Card,
  Empty,
  Flex,
  Form,
  Input,
  InputNumber,
  Table,
  Tag,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  surveyModelCanPlaceACaver,
  useCaveDepthPlaces,
  useDeleteCaveDepthPlace,
  useSurveyModels,
  useWriteCaveDepthPlace,
  type CaveDepthPlace,
} from '../../api/hooks.ts';
import SurveyStationInput from '../../components/caves/SurveyStationInput.tsx';

interface DeclarationForm {
  depthM: number | null;
  stationName: string;
  placeLabel: string;
}

/**
 * What this cave's depths mean.
 *
 * A depth on its own is not a place. Turning one into a position means looking for the station
 * nearest to it, and "nearest" cannot tell which of several stations within a metre of each other
 * is the place people actually mean by that number — a club knows, and nothing in the geometry
 * does. Declared here once, that answer is used by every path that turns a depth into a position:
 * a report typed while a trip is underground, a correction made afterwards, and a sheet imported
 * from a coordinator's spreadsheet.
 *
 * The name is the half that gets used most. A caver on the phone says "at the Meander", not "at
 * 96 metres" and certainly not "at station 3.14", and a named place is what the report card and
 * the importer can then offer instead of a number.
 *
 * <b>One declaration per depth, and the depth is the key.</b> Two answers to "what is at 96 m" is
 * no answer, and the resolution that consults these has to be able to take one — so writing a
 * depth that is already declared replaces it rather than adding beside it.
 *
 * <b>Which is why editing a declaration's depth is two writes and not one.</b> The server knows
 * nothing about "the row being edited": a write at 97 m is a declaration at 97 m, and the one at
 * 96 m it was meant to correct stays where it was. Left like that, the same place is declared
 * twice under two depths — the report chooser offers two "Meandru", and an imported row naming it
 * is refused because which station is meant cannot be decided. So a correction that moves the
 * depth withdraws the declaration it moved, once the new one has landed.
 *
 * <b>A declaration naming a station the cave's current survey does not hold is marked.</b> A
 * declaration is only followed where its station exists, so one that was mistyped, or whose
 * station was renamed in the survey since made the current one, quietly stops meaning anything —
 * every report of that depth goes back to the nearest station and nothing says why. The server
 * says, per declaration, whether the survey marked as the cave's current line plot holds the
 * station; only an outright "no" is marked here. A survey that has merely been uploaded is not
 * judged until it is made the current one: arriving does not take the mark from the survey that
 * has it. Where the answer is withheld — no current survey read yet, or a reader who may not be
 * told the cave's survey — nothing is drawn at all, so a row with no mark never claims the
 * station is there.
 *
 * <b>The station is offered as it is typed, out of the cave's current survey,</b> and any text is
 * still taken: a cave can declare its places before its survey is uploaded, and a reader who is
 * not told the survey's stations is offered none and can still write the name down.
 */
export default function CaveDepthPlacesSection({
  caveId,
  canEdit,
}: {
  caveId: string;
  canEdit: boolean;
}) {
  const { t } = useTranslation();
  const { message, modal } = App.useApp();
  const { data: places } = useCaveDepthPlaces(caveId);
  const { data: models } = useSurveyModels(caveId);
  // The survey names are offered from: the one marked as the cave's current line plot, once it
  // has been read — which is, by the same rule, the only survey the server judges each row's
  // station against. Where there is none the field is a plain text box and no row is marked:
  // offering names out of some other survey of the cave would be offering names the mark beside
  // each row may then contradict.
  const offeredFrom =
    (models ?? []).find((model) => model.isCurrent && surveyModelCanPlaceACaver(model))?.id ?? null;
  const someMissing = (places ?? []).some((place) => place.stationInSurvey === false);
  const write = useWriteCaveDepthPlace(caveId);
  const remove = useDeleteCaveDepthPlace(caveId);
  const [form] = Form.useForm<DeclarationForm>();
  const [adding, setAdding] = useState(false);
  /** The declaration the form was filled from, or null while it is declaring a new one. */
  const [editing, setEditing] = useState<CaveDepthPlace | null>(null);

  const leaveForm = () => {
    form.resetFields();
    setAdding(false);
    setEditing(null);
  };

  const save = async (values: DeclarationForm) => {
    if (values.depthM === null || values.depthM === undefined) {
      return;
    }

    try {
      await write.mutateAsync({
        depthM: values.depthM,
        stationName: values.stationName.trim(),
        placeLabel: values.placeLabel.trim() || null,
      });
    } catch (error) {
      // The one refusal with something to act on: the station named is known to the survey only
      // by a number that the next export hands to another station. Everything else is a save
      // that did not happen, and says so.
      message.error(
        error instanceof ApiError && error.code === 'cave_depth_place.station_nameless'
          ? t('caves.depthPlaces.stationNameless')
          : t('caves.depthPlaces.saveFailed'),
      );
      return;
    }

    // A correction that moved the depth has left the row it was filled from behind, under the old
    // depth, and the depth is the key — so that row is withdrawn now. After the write and not
    // before it: a write that fails then leaves the old declaration standing, which is a cave
    // still declaring the place, rather than one declaring nothing there at all.
    const moved = editing !== null && editing.depthM !== values.depthM ? editing : null;
    if (moved !== null) {
      try {
        await remove.mutateAsync({ id: moved.id });
      } catch {
        // Said as what it is, which is not a failed save: the new declaration is there. What is
        // also there is the old one, and until it goes the place answers to two depths.
        message.error(
          t('caves.depthPlaces.oldDepthKept', { depth: values.depthM, old: moved.depthM }),
        );
        leaveForm();
        return;
      }
    }

    leaveForm();
    message.success(t('caves.depthPlaces.saved'));
  };

  const confirmRemove = (place: CaveDepthPlace) =>
    modal.confirm({
      title: t('caves.depthPlaces.withdrawConfirm', { depth: place.depthM }),
      okButtonProps: { danger: true },
      onOk: async () => {
        try {
          await remove.mutateAsync({ id: place.id });
        } catch {
          message.error(t('caves.depthPlaces.saveFailed'));
        }
      },
    });

  /**
   * Filling the form from a row, so changing a declaration is one press rather than retyping it.
   * The row is remembered as well as copied, because saving under a changed depth has to know
   * which declaration it is a correction of — see {@link save}.
   */
  const edit = (place: CaveDepthPlace) => {
    form.setFieldsValue({
      depthM: place.depthM,
      stationName: place.stationName,
      placeLabel: place.placeLabel ?? '',
    });
    setEditing(place);
    setAdding(true);
  };

  return (
    <Card
      size="small"
      title={t('caves.depthPlaces.title')}
      style={{ marginBottom: 16 }}
      data-testid="cave-depth-places"
      extra={
        canEdit && !adding ? (
          <Button
            size="small"
            icon={<PlusOutlined />}
            onClick={() => setAdding(true)}
            data-testid="cave-depth-place-add"
          >
            {t('caves.depthPlaces.declare')}
          </Button>
        ) : null
      }
    >
      <Typography.Paragraph type="secondary" style={{ marginBottom: 12 }}>
        {t('caves.depthPlaces.help')}
      </Typography.Paragraph>

      {places && places.length > 0 ? (
        <Table<CaveDepthPlace>
          size="small"
          rowKey="id"
          pagination={false}
          dataSource={places}
          columns={[
            {
              title: t('caves.depthPlaces.depth'),
              dataIndex: 'depthM',
              width: 110,
              render: (depth: number) => t('caves.depthPlaces.metres', { depth }),
            },
            {
              title: t('caves.depthPlaces.station'),
              dataIndex: 'stationName',
              render: (station: string, place: CaveDepthPlace) => (
                <Flex gap={8} align="center" wrap>
                  <span>{station}</span>
                  {/* Only an outright "no". Null is the server not saying — no survey read, or
                      a reader who is not told — and is drawn exactly as "yes" is: as nothing. */}
                  {place.stationInSurvey === false && (
                    <Tag
                      color="warning"
                      style={{ marginInlineEnd: 0 }}
                      data-testid={`cave-depth-place-not-in-survey-${place.id}`}
                    >
                      {t('caves.depthPlaces.stationNotInSurvey')}
                    </Tag>
                  )}
                </Flex>
              ),
            },
            {
              title: t('caves.depthPlaces.place'),
              dataIndex: 'placeLabel',
              render: (label: string | null) =>
                label ?? (
                  <Typography.Text type="secondary">
                    {t('caves.depthPlaces.unnamed')}
                  </Typography.Text>
                ),
            },
            ...(canEdit
              ? [
                  {
                    title: '',
                    key: 'actions',
                    width: 120,
                    render: (_: unknown, place: CaveDepthPlace) => (
                      <Flex gap={4}>
                        <Button
                          size="small"
                          type="link"
                          onClick={() => edit(place)}
                          data-testid={`cave-depth-place-edit-${place.id}`}
                        >
                          {t('common.edit')}
                        </Button>
                        <Button
                          size="small"
                          type="text"
                          danger
                          icon={<DeleteOutlined />}
                          aria-label={t('caves.depthPlaces.withdraw')}
                          onClick={() => confirmRemove(place)}
                        />
                      </Flex>
                    ),
                  },
                ]
              : []),
          ]}
        />
      ) : (
        <Empty
          image={null}
          description={t('caves.depthPlaces.none')}
          data-testid="cave-depth-places-empty"
        />
      )}

      {/* What the mark means and what to do about it, said once under the table rather than in a
          tooltip per row: a tooltip is not reachable on a phone or from a keyboard, and the answer
          is the same for every marked row. */}
      {someMissing && (
        <Typography.Paragraph
          type="secondary"
          style={{ marginTop: 8, marginBottom: 0 }}
          data-testid="cave-depth-places-not-in-survey-help"
        >
          {t('caves.depthPlaces.stationNotInSurveyHelp')}
        </Typography.Paragraph>
      )}

      {canEdit && adding && (
        <Form<DeclarationForm>
          form={form}
          layout="inline"
          style={{ marginTop: 12, rowGap: 8 }}
          initialValues={{ depthM: null, stationName: '', placeLabel: '' }}
          onFinish={save}
        >
          <Form.Item
            name="depthM"
            label={t('caves.depthPlaces.depth')}
            rules={[{ required: true, message: t('caves.depthPlaces.depthRequired') }]}
          >
            {/* The unit inside the field rather than hung off it as an addon: the library has
                deprecated the addon on a number field, and the warning it prints is a console error
                that the browser sweep files as a defect on every page this card is drawn on. */}
            <InputNumber
              step={0.1}
              style={{ width: 120 }}
              data-testid="cave-depth-place-depth"
              suffix="m"
            />
          </Form.Item>
          <Form.Item
            name="stationName"
            label={t('caves.depthPlaces.station')}
            rules={[{ required: true, message: t('caves.depthPlaces.stationRequired') }]}
          >
            <SurveyStationInput
              surveyModelId={offeredFrom}
              style={{ width: 180 }}
              data-testid="cave-depth-place-station"
            />
          </Form.Item>
          <Form.Item name="placeLabel" label={t('caves.depthPlaces.place')}>
            <Input style={{ width: 200 }} data-testid="cave-depth-place-label" />
          </Form.Item>
          <Form.Item>
            <Flex gap={8}>
              <Button
                type="primary"
                htmlType="submit"
                loading={write.isPending}
                data-testid="cave-depth-place-save"
              >
                {t('common.save')}
              </Button>
              <Button onClick={leaveForm}>{t('common.cancel')}</Button>
            </Flex>
          </Form.Item>
        </Form>
      )}
    </Card>
  );
}
