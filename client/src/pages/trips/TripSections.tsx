// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState } from 'react';
import {
  App,
  Alert,
  Button,
  Card,
  Checkbox,
  Collapse,
  Empty,
  Flex,
  InputNumber,
  Select,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  useCavers,
  useTripTypes,
  useUpdateTripLog,
  type TripLogInfo,
  type TripLogWrite,
} from '../../api/hooks.ts';
import {
  isCaverReferenceField,
  tripSectionEnumLabel,
  tripSectionFieldLabel,
  tripSectionValueText,
} from '../../components/trips/tripSectionFields.ts';
import TypedField from '../../components/typedProperties/TypedField.tsx';
import { parsePropertiesSchema, type SchemaField } from '../../components/typedProperties/propertiesSchema.ts';

/** The three sections a trip carries, in the order a report is written in. */
const SECTIONS = ['fieldData', 'logistics', 'safety'] as const;
type SectionKey = (typeof SECTIONS)[number];

// The server answers a section that does not fit its schema with a code naming which section it
// means, so a caller with three forms open is told which one to look at rather than being left
// to read prose.
const INVALID_CODES: Record<SectionKey, string> = {
  fieldData: 'trip_log.field_data_invalid',
  logistics: 'trip_log.logistics_invalid',
  safety: 'trip_log.safety_invalid',
};

/**
 * The counted facts: columns on the trip rather than a section, because a club counts them
 * across trips and a value buried in a free-form object cannot be counted. Metres throughout —
 * one unit is stored and the reader's locale decides how it is written, so no row has to be
 * trusted to say which unit it meant.
 */
const MEASURES = ['depthReachedM', 'lengthSurveyedM', 'surveyStations', 'ropeMetres'] as const;

interface Measured {
  depthReachedM: number | null;
  lengthSurveyedM: number | null;
  surveyStations: number | null;
  ropeMetres: number | null;
  hadIncident: boolean;
}

const measuredOf = (trip: TripLogInfo): Measured => ({
  depthReachedM: trip.depthReachedM,
  lengthSurveyedM: trip.lengthSurveyedM,
  surveyStations: trip.surveyStations,
  ropeMetres: trip.ropeMetres,
  hadIncident: trip.hadIncident,
});

const echoPerson = (person: TripLogInfo['participants'][number]) => ({
  caverId: person.caverId,
  newCaverName: null,
  roleId: person.roleId,
  entryTime: person.entryTime,
  exitTime: person.exitTime,
  note: person.note,
});

type Bag = Record<string, unknown>;

const asBag = (value: unknown): Bag =>
  typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Bag) : {};

/**
 * The three per-purpose sections of a trip report: what it found underground, what it took to
 * get in, and what went wrong.
 *
 * Each is a free-form object measured against a JSON schema its purpose carries, so what a
 * section asks for is an installation's decision and needs no client change. Each is written
 * on its own: a save sends one section and leaves the other two absent, which is what tells the
 * server not to touch them — echoing all three back would re-measure sections nobody opened
 * against a schema that may have moved since they were written.
 *
 * The safety section answers to a narrower audience than the rest of the trip. Whether
 * something went wrong is on the trip itself, for everyone who may read it; what went wrong
 * names identifiable people making mistakes, so the server sends it only to callers who may
 * change the trip and sends nothing at all — not an empty object — to everyone else. That
 * distinction is drawn here rather than hidden: an empty section on a record that does have an
 * incident would read as "nothing happened", which is the worst possible wrong answer.
 */
export default function TripSections({ trip, canEdit }: { trip: TripLogInfo; canEdit: boolean }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: tripTypes } = useTripTypes();
  const updateTrip = useUpdateTripLog();

  const tripType = tripTypes?.find((row) => row.id === trip.tripTypeId);
  const schemas = useMemo(
    () => ({
      fieldData: parsePropertiesSchema(tripType?.fieldDataSchema),
      logistics: parsePropertiesSchema(tripType?.logisticsSchema),
      safety: parsePropertiesSchema(tripType?.safetySchema),
    }),
    [tripType],
  );

  // The stored objects, held whole rather than field by field, so keys the current schema does
  // not know about survive a write. Re-synced whenever the trip is re-read: another edit may
  // have landed, and a stale bag saved over it would put the old values back.
  const [values, setValues] = useState<Record<SectionKey, Bag>>({
    fieldData: {},
    logistics: {},
    safety: {},
  });
  const [measured, setMeasured] = useState<Measured>(measuredOf(trip));
  useEffect(() => {
    setValues({
      fieldData: asBag(trip.fieldData),
      logistics: asBag(trip.logistics),
      safety: asBag(trip.safety),
    });
    setMeasured(measuredOf(trip));
  }, [trip]);

  /**
   * Writes one section, or — with no section named — only the counted facts. The counted facts
   * travel on every save from this card because they are edited on it: they are columns on the
   * trip rather than a section, so leaving them out of a save made from the same card would
   * silently drop what somebody had just typed a few rows higher.
   */
  const save = async (section?: SectionKey) => {
    // Merge the schema-driven values over what is stored so keys the current schema does not
    // know about survive the round trip; an emptied field is removed rather than written as a
    // blank, so "not filled in" and "filled in with nothing" stay distinct.
    const bag: Bag = section ? { ...values[section] } : {};
    for (const field of section ? schemas[section] : []) {
      const value = values[section!][field.key];
      if (value === undefined || value === null || value === '') {
        delete bag[field.key];
      } else {
        bag[field.key] = value;
      }
    }

    const body: TripLogWrite = {
      title: trip.title,
      tripTypeId: trip.tripTypeId,
      tripDate: trip.tripDate,
      tripDateEnd: trip.tripDateEnd,
      entryTime: trip.entryTime,
      exitTime: trip.exitTime,
      description: trip.description,
      results: trip.results,
      weatherConditions: trip.weatherConditions,
      locationText: trip.locationText,
      organizingCavingGroupId: trip.organizingCavingGroupId,
      geom: trip.geom,
      // No list at all, not a cleared one: which caves this trip is about is recorded through
      // its roles, and saving a section must not be able to undo that.
      caveIds: null,
      // The roster back exactly as it stands, jobs and times and notes included: a write replaces
      // every row the trip has, so echoing only the people would strip what was recorded about
      // them for the sake of saving one section.
      participants: trip.participants.map(echoPerson),
      proposers: trip.proposers.map(echoPerson),
      cavingGroupId: trip.cavingGroupId,
      visibility: trip.visibility,
      depthReachedM: measured.depthReachedM,
      lengthSurveyedM: measured.lengthSurveyedM,
      surveyStations: measured.surveyStations,
      ropeMetres: measured.ropeMetres,
      hadIncident: measured.hadIncident,
      fieldData: section === 'fieldData' ? bag : null,
      logistics: section === 'logistics' ? bag : null,
      safety: section === 'safety' ? bag : null,
    };

    try {
      await updateTrip.mutateAsync({ id: trip.id, body });
      message.success(t('common.saved'));
    } catch (error) {
      // Worth naming rather than folding into "save failed": a section the purpose's schema
      // rejects leaves somebody guessing which of the values they typed was at fault.
      message.error(
        error instanceof ApiError && section && error.code === INVALID_CODES[section]
          ? t('trips.sections.invalid', { section: t(`trips.sections.${section}`) })
          : t('common.saveFailed'),
      );
    }
  };

  const body = (section: SectionKey) => {
    // Null and an empty object are different answers, and only the safety section can give the
    // first: the server withholds it whole from a reader who may not change the trip.
    if (section === 'safety' && trip.safety === null) {
      return (
        <Alert
          type="info"
          showIcon
          title={t('trips.sections.safetyWithheld')}
          description={t('trips.sections.safetyWithheldDetail')}
          data-testid="trip-safety-withheld"
        />
      );
    }

    const fields = schemas[section];
    if (fields.length === 0) {
      return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('trips.sections.noFields')} />;
    }

    return (
      <Flex vertical gap={12}>
        {fields.map((field) => (
          <SectionField
            key={field.key}
            field={field}
            label={tripSectionFieldLabel(field, t)}
            value={values[section][field.key]}
            canEdit={canEdit}
            onChange={(value) =>
              setValues((current) => ({
                ...current,
                [section]: { ...current[section], [field.key]: value },
              }))
            }
          />
        ))}
        {canEdit && (
          <Flex justify="flex-end">
            <Button
              type="primary"
              size="small"
              loading={updateTrip.isPending}
              onClick={() => void save(section)}
              data-testid={`trip-section-save-${section}`}
            >
              {t('common.save')}
            </Button>
          </Flex>
        )}
      </Flex>
    );
  };

  const measuredBody = (
    <Flex vertical gap={12}>
      {MEASURES.map((measure) => (
        <Flex vertical gap={2} key={measure}>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {t(`trips.${measure}`)}
          </Typography.Text>
          {canEdit ? (
            <InputNumber
              value={measured[measure]}
              onChange={(next) => setMeasured((current) => ({ ...current, [measure]: next }))}
              min={0}
              precision={measure === 'surveyStations' ? 0 : undefined}
              style={{ width: '100%' }}
              data-testid={`trip-measure-${measure}`}
            />
          ) : (
            <Typography.Text>{measured[measure] ?? '—'}</Typography.Text>
          )}
        </Flex>
      ))}
      {/* A plain fact of the row, told to everyone who may read the trip: a club counts
          incidents and notices a run of them, and a fact nobody can count is a fact nobody
          reviews. What happened is the safety section, and answers to a narrower audience. */}
      {canEdit ? (
        <Checkbox
          checked={measured.hadIncident}
          onChange={(e) => setMeasured((current) => ({ ...current, hadIncident: e.target.checked }))}
          data-testid="trip-measure-hadIncident"
        >
          {t('trips.hadIncidentYes')}
        </Checkbox>
      ) : (
        <Typography.Text>
          {t(measured.hadIncident ? 'trips.hadIncidentYes' : 'trips.sections.no')}
        </Typography.Text>
      )}
      {canEdit && (
        <Flex justify="flex-end">
          <Button
            type="primary"
            size="small"
            loading={updateTrip.isPending}
            onClick={() => void save()}
            data-testid="trip-section-save-measured"
          >
            {t('common.save')}
          </Button>
        </Flex>
      )}
    </Flex>
  );

  return (
    <Card size="small" title={t('trips.sections.title')} style={{ marginTop: 16 }}>
      <Collapse
        ghost
        items={[
          {
            key: 'measured',
            label: t('trips.sections.measured'),
            children: measuredBody,
          },
          ...SECTIONS.map((section) => ({
            key: section,
            label: t(`trips.sections.${section}`),
            children: body(section),
          })),
        ]}
      />
    </Card>
  );
}

/** One field of a section: its control when the reader may write, its value when they may not. */
function SectionField({
  field,
  label,
  value,
  canEdit,
  onChange,
}: {
  field: SchemaField;
  label: string;
  value: unknown;
  canEdit: boolean;
  onChange: (value: unknown) => void;
}) {
  const { t } = useTranslation();
  return (
    <Flex vertical gap={2}>
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        {field.required ? `${label} *` : label}
      </Typography.Text>
      {/* A person is picked from the roster rather than typed: the field holds an identity, and
          an identifier is neither typeable nor readable. Somebody the roster does not know is
          written in the free-text field beside it, which is what that field is for. */}
      {isCaverReferenceField(field) ? (
        <CaverReferenceField field={field} value={value} canEdit={canEdit} onChange={onChange} />
      ) : canEdit ? (
        <TypedField
          field={field}
          value={value}
          onChange={onChange}
          optionLabel={(option) => tripSectionEnumLabel(field.key, option, t)}
          data-testid={`trip-section-field-${field.key}`}
        />
      ) : (
        <Typography.Text data-testid={`trip-section-value-${field.key}`}>
          {tripSectionValueText(field, value, t)}
        </Typography.Text>
      )}
    </Flex>
  );
}

/**
 * A field holding somebody from the roster: offered as a person to choose, shown as that
 * person's name.
 *
 * The roster is only asked for where such a field exists, so a trip whose purpose declares none
 * costs nothing — and a caller who may not read the roster simply sees the identity as stored
 * rather than an error, since the field is a note about access rather than the record itself.
 */
function CaverReferenceField({
  field,
  value,
  canEdit,
  onChange,
}: {
  field: SchemaField;
  value: unknown;
  canEdit: boolean;
  onChange: (value: unknown) => void;
}) {
  const { t } = useTranslation();
  const { data: cavers } = useCavers();
  const id = typeof value === 'string' && value !== '' ? value : undefined;
  const name = cavers?.find((caver) => caver.id === id)?.name;

  if (!canEdit) {
    return (
      <Typography.Text data-testid={`trip-section-value-${field.key}`}>
        {name ?? id ?? '—'}
      </Typography.Text>
    );
  }

  return (
    <Select
      showSearch
      allowClear
      optionFilterProp="label"
      value={id}
      placeholder={t('trips.sections.pickPerson')}
      onChange={(next: string | undefined) => onChange(next ?? undefined)}
      options={(cavers ?? []).map((caver) => ({ value: caver.id, label: caver.name }))}
      data-testid={`trip-section-field-${field.key}`}
    />
  );
}
