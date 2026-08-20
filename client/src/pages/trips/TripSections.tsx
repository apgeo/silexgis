// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useRef, useState } from 'react';
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

  /**
   * Which blocks of this card hold something not yet sent, and which trip they were typed on.
   *
   * Every write to the trip re-reads it, and the re-read arrives as a new object seconds later —
   * long enough to type a line into a section in the meantime. Re-syncing on the object alone
   * would throw those keystrokes away and, worse, leave the next save serialising the copy that
   * came back, so a section somebody was part way through writing would be written out empty.
   *
   * Per block rather than per card, because a save carries one section and leaves the other two
   * absent: a card-wide flag cleared by saving field data would let the re-read that save
   * triggers overwrite a half-written safety section, which is the same loss one step sideways.
   * The counted facts travel on every save from this card, so they are cleared by any of them.
   *
   * Beside the trip it belongs to, because this card is not remounted when the page moves to
   * another trip — the route element is the same and a cached trip arrives without a loading
   * pass, so only the prop changes. Unsent text belongs to the trip it was typed on and must not
   * be held back from, or worse written onto, a different one.
   *
   * A ref rather than a state: this is a condition the re-sync is read under, not something the
   * card draws, and keeping it out of the effect's dependencies is what stops the effect running
   * again the moment a block is cleared — which would re-sync from the copy the save was made
   * against and undo on screen the save that had just been sent.
   *
   * Cleared when a save is sent rather than when it is answered: what was typed is on its way to
   * the server at that point, so a re-read landing afterwards is welcome, and anything typed
   * after that moment marks the block again and is protected in its turn. A save that fails
   * marks it back, because then this card holds the only copy of that work.
   */
  const dirty = useRef<{ tripId: string; blocks: Set<SectionKey | 'measured'> }>({
    tripId: trip.id,
    blocks: new Set(),
  });

  const markDirty = (block: SectionKey | 'measured') => {
    if (dirty.current.tripId !== trip.id) {
      dirty.current = { tripId: trip.id, blocks: new Set() };
    }
    dirty.current.blocks.add(block);
  };

  useEffect(() => {
    // A different trip: nothing held here belongs to it, so everything is taken from the new one.
    if (dirty.current.tripId !== trip.id) {
      dirty.current = { tripId: trip.id, blocks: new Set() };
    }
    const held = dirty.current.blocks;
    setValues((current) => {
      const next = { ...current };
      for (const section of SECTIONS) {
        if (!held.has(section)) {
          next[section] = asBag(trip[section]);
        }
      }
      return next;
    });
    if (!held.has('measured')) {
      setMeasured(measuredOf(trip));
    }
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
      // Back as it stands: a write sets every field, so saving one section without it would take
      // the trip's limit off and put everybody who was waiting on the trip.
      maxParticipants: trip.maxParticipants,
    };

    // Only what this request carries: the named section, and the counted facts, which travel on
    // every save from this card. A section not in the body is untouched by the server and its
    // unsent text is still the only copy there is.
    const sent: (SectionKey | 'measured')[] = section ? [section, 'measured'] : ['measured'];
    for (const block of sent) {
      dirty.current.blocks.delete(block);
    }
    try {
      await updateTrip.mutateAsync({ id: trip.id, body });
      message.success(t('common.saved'));
    } catch (error) {
      // Nothing reached the server, so this card holds the only copy of what was typed and must
      // not be overwritten by the next re-read of the trip.
      for (const block of sent) {
        markDirty(block);
      }
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
            onChange={(value) => {
              markDirty(section);
              setValues((current) => ({
                ...current,
                [section]: { ...current[section], [field.key]: value },
              }));
            }}
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
              onChange={(next) => {
                markDirty('measured');
                setMeasured((current) => ({ ...current, [measure]: next }));
              }}
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
          onChange={(e) => {
            markDirty('measured');
            setMeasured((current) => ({ ...current, hadIncident: e.target.checked }));
          }}
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
