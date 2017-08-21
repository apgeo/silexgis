<?php


/**
 * Base class that represents a row from the 'caves' table.
 *
 *
 *
 * @package    propel.generator.speogis.om
 */
abstract class BaseCaves extends BaseObject implements Persistent
{
    /**
     * Peer class name
     */
    const PEER = 'CavesPeer';

    /**
     * The Peer class.
     * Instance provides a convenient way of calling static methods on a class
     * that calling code may not be able to identify.
     * @var        CavesPeer
     */
    protected static $peer;

    /**
     * The flag var to prevent infinite loop in deep copy
     * @var       boolean
     */
    protected $startCopy = false;

    /**
     * The value for the id field.
     * @var        string
     */
    protected $id;

    /**
     * The value for the name field.
     * @var        string
     */
    protected $name;

    /**
     * The value for the type_id field.
     * @var        string
     */
    protected $type_id;

    /**
     * The value for the identification_code field.
     * @var        string
     */
    protected $identification_code;

    /**
     * The value for the description field.
     * @var        string
     */
    protected $description;

    /**
     * The value for the user_id field.
     * @var        string
     */
    protected $user_id;

    /**
     * The value for the other_toponyms field.
     * @var        string
     */
    protected $other_toponyms;

    /**
     * The value for the rock_type_id field.
     * @var        string
     */
    protected $rock_type_id;

    /**
     * The value for the rock_age field.
     * @var        string
     */
    protected $rock_age;

    /**
     * The value for the hydrographic_basin field.
     * @var        string
     */
    protected $hydrographic_basin;

    /**
     * The value for the valley field.
     * @var        string
     */
    protected $valley;

    /**
     * The value for the tributary_river field.
     * @var        string
     */
    protected $tributary_river;

    /**
     * The value for the closest_address field.
     * @var        string
     */
    protected $closest_address;

    /**
     * The value for the is_show_cave field.
     * @var        boolean
     */
    protected $is_show_cave;

    /**
     * The value for the show_cave_length field.
     * @var        int
     */
    protected $show_cave_length;

    /**
     * The value for the website field.
     * @var        string
     */
    protected $website;

    /**
     * The value for the land_registry_number field.
     * @var        string
     */
    protected $land_registry_number;

    /**
     * The value for the region field.
     * @var        string
     */
    protected $region;

    /**
     * The value for the depth field.
     * @var        int
     */
    protected $depth;

    /**
     * The value for the surveyed_length field.
     * @var        int
     */
    protected $surveyed_length;

    /**
     * The value for the discovery_date field.
     * @var        string
     */
    protected $discovery_date;

    /**
     * The value for the discoverer field.
     * @var        string
     */
    protected $discoverer;

    /**
     * The value for the volume field.
     * @var        int
     */
    protected $volume;

    /**
     * The value for the area field.
     * @var        int
     */
    protected $area;

    /**
     * The value for the positive_depth field.
     * @var        int
     */
    protected $positive_depth;

    /**
     * The value for the negative_depth field.
     * @var        int
     */
    protected $negative_depth;

    /**
     * The value for the ramification_index field.
     * @var        int
     */
    protected $ramification_index;

    /**
     * The value for the real_extension field.
     * @var        int
     */
    protected $real_extension;

    /**
     * The value for the cave_age field.
     * @var        int
     */
    protected $cave_age;

    /**
     * The value for the projected_extension field.
     * @var        int
     */
    protected $projected_extension;

    /**
     * The value for the exploration_status field.
     * @var        string
     */
    protected $exploration_status;

    /**
     * The value for the protection_class field.
     * @var        string
     */
    protected $protection_class;

    /**
     * The value for the potential_depth field.
     * @var        int
     */
    protected $potential_depth;

    /**
     * The value for the estimated_length field.
     * @var        int
     */
    protected $estimated_length;

    /**
     * The value for the altitude field.
     * @var        int
     */
    protected $altitude;

    /**
     * Flag to prevent endless save loop, if this object is referenced
     * by another object which falls in this transaction.
     * @var        boolean
     */
    protected $alreadyInSave = false;

    /**
     * Flag to prevent endless validation loop, if this object is referenced
     * by another object which falls in this transaction.
     * @var        boolean
     */
    protected $alreadyInValidation = false;

    /**
     * Flag to prevent endless clearAllReferences($deep=true) loop, if this object is referenced
     * @var        boolean
     */
    protected $alreadyInClearAllReferencesDeep = false;

    /**
     * Get the [id] column value.
     *
     * @return string
     */
    public function getId()
    {

        return $this->id;
    }

    /**
     * Get the [name] column value.
     *
     * @return string
     */
    public function getName()
    {

        return $this->name;
    }

    /**
     * Get the [type_id] column value.
     *
     * @return string
     */
    public function getTypeId()
    {

        return $this->type_id;
    }

    /**
     * Get the [identification_code] column value.
     *
     * @return string
     */
    public function getIdentificationCode()
    {

        return $this->identification_code;
    }

    /**
     * Get the [description] column value.
     *
     * @return string
     */
    public function getDescription()
    {

        return $this->description;
    }

    /**
     * Get the [user_id] column value.
     *
     * @return string
     */
    public function getUserId()
    {

        return $this->user_id;
    }

    /**
     * Get the [other_toponyms] column value.
     *
     * @return string
     */
    public function getOtherToponyms()
    {

        return $this->other_toponyms;
    }

    /**
     * Get the [rock_type_id] column value.
     *
     * @return string
     */
    public function getRockTypeId()
    {

        return $this->rock_type_id;
    }

    /**
     * Get the [rock_age] column value.
     *
     * @return string
     */
    public function getRockAge()
    {

        return $this->rock_age;
    }

    /**
     * Get the [hydrographic_basin] column value.
     *
     * @return string
     */
    public function getHydrographicBasin()
    {

        return $this->hydrographic_basin;
    }

    /**
     * Get the [valley] column value.
     *
     * @return string
     */
    public function getValley()
    {

        return $this->valley;
    }

    /**
     * Get the [tributary_river] column value.
     *
     * @return string
     */
    public function getTributaryRiver()
    {

        return $this->tributary_river;
    }

    /**
     * Get the [closest_address] column value.
     *
     * @return string
     */
    public function getClosestAddress()
    {

        return $this->closest_address;
    }

    /**
     * Get the [is_show_cave] column value.
     *
     * @return boolean
     */
    public function getIsShowCave()
    {

        return $this->is_show_cave;
    }

    /**
     * Get the [show_cave_length] column value.
     *
     * @return int
     */
    public function getShowCaveLength()
    {

        return $this->show_cave_length;
    }

    /**
     * Get the [website] column value.
     *
     * @return string
     */
    public function getWebsite()
    {

        return $this->website;
    }

    /**
     * Get the [land_registry_number] column value.
     *
     * @return string
     */
    public function getLandRegistryNumber()
    {

        return $this->land_registry_number;
    }

    /**
     * Get the [region] column value.
     *
     * @return string
     */
    public function getRegion()
    {

        return $this->region;
    }

    /**
     * Get the [depth] column value.
     *
     * @return int
     */
    public function getDepth()
    {

        return $this->depth;
    }

    /**
     * Get the [surveyed_length] column value.
     *
     * @return int
     */
    public function getSurveyedLength()
    {

        return $this->surveyed_length;
    }

    /**
     * Get the [discovery_date] column value.
     *
     * @return string
     */
    public function getDiscoveryDate()
    {

        return $this->discovery_date;
    }

    /**
     * Get the [discoverer] column value.
     *
     * @return string
     */
    public function getDiscoverer()
    {

        return $this->discoverer;
    }

    /**
     * Get the [volume] column value.
     *
     * @return int
     */
    public function getVolume()
    {

        return $this->volume;
    }

    /**
     * Get the [area] column value.
     *
     * @return int
     */
    public function getArea()
    {

        return $this->area;
    }

    /**
     * Get the [positive_depth] column value.
     *
     * @return int
     */
    public function getPositiveDepth()
    {

        return $this->positive_depth;
    }

    /**
     * Get the [negative_depth] column value.
     *
     * @return int
     */
    public function getNegativeDepth()
    {

        return $this->negative_depth;
    }

    /**
     * Get the [ramification_index] column value.
     *
     * @return int
     */
    public function getRamificationIndex()
    {

        return $this->ramification_index;
    }

    /**
     * Get the [real_extension] column value.
     *
     * @return int
     */
    public function getRealExtension()
    {

        return $this->real_extension;
    }

    /**
     * Get the [cave_age] column value.
     *
     * @return int
     */
    public function getCaveAge()
    {

        return $this->cave_age;
    }

    /**
     * Get the [projected_extension] column value.
     *
     * @return int
     */
    public function getProjectedExtension()
    {

        return $this->projected_extension;
    }

    /**
     * Get the [exploration_status] column value.
     *
     * @return string
     */
    public function getExplorationStatus()
    {

        return $this->exploration_status;
    }

    /**
     * Get the [protection_class] column value.
     *
     * @return string
     */
    public function getProtectionClass()
    {

        return $this->protection_class;
    }

    /**
     * Get the [potential_depth] column value.
     *
     * @return int
     */
    public function getPotentialDepth()
    {

        return $this->potential_depth;
    }

    /**
     * Get the [estimated_length] column value.
     *
     * @return int
     */
    public function getEstimatedLength()
    {

        return $this->estimated_length;
    }

    /**
     * Get the [altitude] column value.
     *
     * @return int
     */
    public function getAltitude()
    {

        return $this->altitude;
    }

    /**
     * Set the value of [id] column.
     *
     * @param  string $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setId($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (string) $v;
        }

        if ($this->id !== $v) {
            $this->id = $v;
            $this->modifiedColumns[] = CavesPeer::ID;
        }


        return $this;
    } // setId()

    /**
     * Set the value of [name] column.
     *
     * @param  string $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setName($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->name !== $v) {
            $this->name = $v;
            $this->modifiedColumns[] = CavesPeer::NAME;
        }


        return $this;
    } // setName()

    /**
     * Set the value of [type_id] column.
     *
     * @param  string $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setTypeId($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (string) $v;
        }

        if ($this->type_id !== $v) {
            $this->type_id = $v;
            $this->modifiedColumns[] = CavesPeer::TYPE_ID;
        }


        return $this;
    } // setTypeId()

    /**
     * Set the value of [identification_code] column.
     *
     * @param  string $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setIdentificationCode($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->identification_code !== $v) {
            $this->identification_code = $v;
            $this->modifiedColumns[] = CavesPeer::IDENTIFICATION_CODE;
        }


        return $this;
    } // setIdentificationCode()

    /**
     * Set the value of [description] column.
     *
     * @param  string $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setDescription($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->description !== $v) {
            $this->description = $v;
            $this->modifiedColumns[] = CavesPeer::DESCRIPTION;
        }


        return $this;
    } // setDescription()

    /**
     * Set the value of [user_id] column.
     *
     * @param  string $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setUserId($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (string) $v;
        }

        if ($this->user_id !== $v) {
            $this->user_id = $v;
            $this->modifiedColumns[] = CavesPeer::USER_ID;
        }


        return $this;
    } // setUserId()

    /**
     * Set the value of [other_toponyms] column.
     *
     * @param  string $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setOtherToponyms($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->other_toponyms !== $v) {
            $this->other_toponyms = $v;
            $this->modifiedColumns[] = CavesPeer::OTHER_TOPONYMS;
        }


        return $this;
    } // setOtherToponyms()

    /**
     * Set the value of [rock_type_id] column.
     *
     * @param  string $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setRockTypeId($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (string) $v;
        }

        if ($this->rock_type_id !== $v) {
            $this->rock_type_id = $v;
            $this->modifiedColumns[] = CavesPeer::ROCK_TYPE_ID;
        }


        return $this;
    } // setRockTypeId()

    /**
     * Set the value of [rock_age] column.
     *
     * @param  string $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setRockAge($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->rock_age !== $v) {
            $this->rock_age = $v;
            $this->modifiedColumns[] = CavesPeer::ROCK_AGE;
        }


        return $this;
    } // setRockAge()

    /**
     * Set the value of [hydrographic_basin] column.
     *
     * @param  string $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setHydrographicBasin($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->hydrographic_basin !== $v) {
            $this->hydrographic_basin = $v;
            $this->modifiedColumns[] = CavesPeer::HYDROGRAPHIC_BASIN;
        }


        return $this;
    } // setHydrographicBasin()

    /**
     * Set the value of [valley] column.
     *
     * @param  string $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setValley($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->valley !== $v) {
            $this->valley = $v;
            $this->modifiedColumns[] = CavesPeer::VALLEY;
        }


        return $this;
    } // setValley()

    /**
     * Set the value of [tributary_river] column.
     *
     * @param  string $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setTributaryRiver($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->tributary_river !== $v) {
            $this->tributary_river = $v;
            $this->modifiedColumns[] = CavesPeer::TRIBUTARY_RIVER;
        }


        return $this;
    } // setTributaryRiver()

    /**
     * Set the value of [closest_address] column.
     *
     * @param  string $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setClosestAddress($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->closest_address !== $v) {
            $this->closest_address = $v;
            $this->modifiedColumns[] = CavesPeer::CLOSEST_ADDRESS;
        }


        return $this;
    } // setClosestAddress()

    /**
     * Sets the value of the [is_show_cave] column.
     * Non-boolean arguments are converted using the following rules:
     *   * 1, '1', 'true',  'on',  and 'yes' are converted to boolean true
     *   * 0, '0', 'false', 'off', and 'no'  are converted to boolean false
     * Check on string values is case insensitive (so 'FaLsE' is seen as 'false').
     *
     * @param boolean|integer|string $v The new value
     * @return Caves The current object (for fluent API support)
     */
    public function setIsShowCave($v)
    {
        if ($v !== null) {
            if (is_string($v)) {
                $v = in_array(strtolower($v), array('false', 'off', '-', 'no', 'n', '0', '')) ? false : true;
            } else {
                $v = (boolean) $v;
            }
        }

        if ($this->is_show_cave !== $v) {
            $this->is_show_cave = $v;
            $this->modifiedColumns[] = CavesPeer::IS_SHOW_CAVE;
        }


        return $this;
    } // setIsShowCave()

    /**
     * Set the value of [show_cave_length] column.
     *
     * @param  int $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setShowCaveLength($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (int) $v;
        }

        if ($this->show_cave_length !== $v) {
            $this->show_cave_length = $v;
            $this->modifiedColumns[] = CavesPeer::SHOW_CAVE_LENGTH;
        }


        return $this;
    } // setShowCaveLength()

    /**
     * Set the value of [website] column.
     *
     * @param  string $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setWebsite($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->website !== $v) {
            $this->website = $v;
            $this->modifiedColumns[] = CavesPeer::WEBSITE;
        }


        return $this;
    } // setWebsite()

    /**
     * Set the value of [land_registry_number] column.
     *
     * @param  string $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setLandRegistryNumber($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->land_registry_number !== $v) {
            $this->land_registry_number = $v;
            $this->modifiedColumns[] = CavesPeer::LAND_REGISTRY_NUMBER;
        }


        return $this;
    } // setLandRegistryNumber()

    /**
     * Set the value of [region] column.
     *
     * @param  string $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setRegion($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->region !== $v) {
            $this->region = $v;
            $this->modifiedColumns[] = CavesPeer::REGION;
        }


        return $this;
    } // setRegion()

    /**
     * Set the value of [depth] column.
     *
     * @param  int $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setDepth($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (int) $v;
        }

        if ($this->depth !== $v) {
            $this->depth = $v;
            $this->modifiedColumns[] = CavesPeer::DEPTH;
        }


        return $this;
    } // setDepth()

    /**
     * Set the value of [surveyed_length] column.
     *
     * @param  int $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setSurveyedLength($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (int) $v;
        }

        if ($this->surveyed_length !== $v) {
            $this->surveyed_length = $v;
            $this->modifiedColumns[] = CavesPeer::SURVEYED_LENGTH;
        }


        return $this;
    } // setSurveyedLength()

    /**
     * Set the value of [discovery_date] column.
     *
     * @param  string $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setDiscoveryDate($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->discovery_date !== $v) {
            $this->discovery_date = $v;
            $this->modifiedColumns[] = CavesPeer::DISCOVERY_DATE;
        }


        return $this;
    } // setDiscoveryDate()

    /**
     * Set the value of [discoverer] column.
     *
     * @param  string $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setDiscoverer($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->discoverer !== $v) {
            $this->discoverer = $v;
            $this->modifiedColumns[] = CavesPeer::DISCOVERER;
        }


        return $this;
    } // setDiscoverer()

    /**
     * Set the value of [volume] column.
     *
     * @param  int $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setVolume($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (int) $v;
        }

        if ($this->volume !== $v) {
            $this->volume = $v;
            $this->modifiedColumns[] = CavesPeer::VOLUME;
        }


        return $this;
    } // setVolume()

    /**
     * Set the value of [area] column.
     *
     * @param  int $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setArea($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (int) $v;
        }

        if ($this->area !== $v) {
            $this->area = $v;
            $this->modifiedColumns[] = CavesPeer::AREA;
        }


        return $this;
    } // setArea()

    /**
     * Set the value of [positive_depth] column.
     *
     * @param  int $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setPositiveDepth($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (int) $v;
        }

        if ($this->positive_depth !== $v) {
            $this->positive_depth = $v;
            $this->modifiedColumns[] = CavesPeer::POSITIVE_DEPTH;
        }


        return $this;
    } // setPositiveDepth()

    /**
     * Set the value of [negative_depth] column.
     *
     * @param  int $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setNegativeDepth($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (int) $v;
        }

        if ($this->negative_depth !== $v) {
            $this->negative_depth = $v;
            $this->modifiedColumns[] = CavesPeer::NEGATIVE_DEPTH;
        }


        return $this;
    } // setNegativeDepth()

    /**
     * Set the value of [ramification_index] column.
     *
     * @param  int $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setRamificationIndex($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (int) $v;
        }

        if ($this->ramification_index !== $v) {
            $this->ramification_index = $v;
            $this->modifiedColumns[] = CavesPeer::RAMIFICATION_INDEX;
        }


        return $this;
    } // setRamificationIndex()

    /**
     * Set the value of [real_extension] column.
     *
     * @param  int $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setRealExtension($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (int) $v;
        }

        if ($this->real_extension !== $v) {
            $this->real_extension = $v;
            $this->modifiedColumns[] = CavesPeer::REAL_EXTENSION;
        }


        return $this;
    } // setRealExtension()

    /**
     * Set the value of [cave_age] column.
     *
     * @param  int $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setCaveAge($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (int) $v;
        }

        if ($this->cave_age !== $v) {
            $this->cave_age = $v;
            $this->modifiedColumns[] = CavesPeer::CAVE_AGE;
        }


        return $this;
    } // setCaveAge()

    /**
     * Set the value of [projected_extension] column.
     *
     * @param  int $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setProjectedExtension($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (int) $v;
        }

        if ($this->projected_extension !== $v) {
            $this->projected_extension = $v;
            $this->modifiedColumns[] = CavesPeer::PROJECTED_EXTENSION;
        }


        return $this;
    } // setProjectedExtension()

    /**
     * Set the value of [exploration_status] column.
     *
     * @param  string $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setExplorationStatus($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->exploration_status !== $v) {
            $this->exploration_status = $v;
            $this->modifiedColumns[] = CavesPeer::EXPLORATION_STATUS;
        }


        return $this;
    } // setExplorationStatus()

    /**
     * Set the value of [protection_class] column.
     *
     * @param  string $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setProtectionClass($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->protection_class !== $v) {
            $this->protection_class = $v;
            $this->modifiedColumns[] = CavesPeer::PROTECTION_CLASS;
        }


        return $this;
    } // setProtectionClass()

    /**
     * Set the value of [potential_depth] column.
     *
     * @param  int $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setPotentialDepth($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (int) $v;
        }

        if ($this->potential_depth !== $v) {
            $this->potential_depth = $v;
            $this->modifiedColumns[] = CavesPeer::POTENTIAL_DEPTH;
        }


        return $this;
    } // setPotentialDepth()

    /**
     * Set the value of [estimated_length] column.
     *
     * @param  int $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setEstimatedLength($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (int) $v;
        }

        if ($this->estimated_length !== $v) {
            $this->estimated_length = $v;
            $this->modifiedColumns[] = CavesPeer::ESTIMATED_LENGTH;
        }


        return $this;
    } // setEstimatedLength()

    /**
     * Set the value of [altitude] column.
     *
     * @param  int $v new value
     * @return Caves The current object (for fluent API support)
     */
    public function setAltitude($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (int) $v;
        }

        if ($this->altitude !== $v) {
            $this->altitude = $v;
            $this->modifiedColumns[] = CavesPeer::ALTITUDE;
        }


        return $this;
    } // setAltitude()

    /**
     * Indicates whether the columns in this object are only set to default values.
     *
     * This method can be used in conjunction with isModified() to indicate whether an object is both
     * modified _and_ has some values set which are non-default.
     *
     * @return boolean Whether the columns in this object are only been set with default values.
     */
    public function hasOnlyDefaultValues()
    {
        // otherwise, everything was equal, so return true
        return true;
    } // hasOnlyDefaultValues()

    /**
     * Hydrates (populates) the object variables with values from the database resultset.
     *
     * An offset (0-based "start column") is specified so that objects can be hydrated
     * with a subset of the columns in the resultset rows.  This is needed, for example,
     * for results of JOIN queries where the resultset row includes columns from two or
     * more tables.
     *
     * @param array $row The row returned by PDOStatement->fetch(PDO::FETCH_NUM)
     * @param int $startcol 0-based offset column which indicates which resultset column to start with.
     * @param boolean $rehydrate Whether this object is being re-hydrated from the database.
     * @return int             next starting column
     * @throws PropelException - Any caught Exception will be rewrapped as a PropelException.
     */
    public function hydrate($row, $startcol = 0, $rehydrate = false)
    {
        try {

            $this->id = ($row[$startcol + 0] !== null) ? (string) $row[$startcol + 0] : null;
            $this->name = ($row[$startcol + 1] !== null) ? (string) $row[$startcol + 1] : null;
            $this->type_id = ($row[$startcol + 2] !== null) ? (string) $row[$startcol + 2] : null;
            $this->identification_code = ($row[$startcol + 3] !== null) ? (string) $row[$startcol + 3] : null;
            $this->description = ($row[$startcol + 4] !== null) ? (string) $row[$startcol + 4] : null;
            $this->user_id = ($row[$startcol + 5] !== null) ? (string) $row[$startcol + 5] : null;
            $this->other_toponyms = ($row[$startcol + 6] !== null) ? (string) $row[$startcol + 6] : null;
            $this->rock_type_id = ($row[$startcol + 7] !== null) ? (string) $row[$startcol + 7] : null;
            $this->rock_age = ($row[$startcol + 8] !== null) ? (string) $row[$startcol + 8] : null;
            $this->hydrographic_basin = ($row[$startcol + 9] !== null) ? (string) $row[$startcol + 9] : null;
            $this->valley = ($row[$startcol + 10] !== null) ? (string) $row[$startcol + 10] : null;
            $this->tributary_river = ($row[$startcol + 11] !== null) ? (string) $row[$startcol + 11] : null;
            $this->closest_address = ($row[$startcol + 12] !== null) ? (string) $row[$startcol + 12] : null;
            $this->is_show_cave = ($row[$startcol + 13] !== null) ? (boolean) $row[$startcol + 13] : null;
            $this->show_cave_length = ($row[$startcol + 14] !== null) ? (int) $row[$startcol + 14] : null;
            $this->website = ($row[$startcol + 15] !== null) ? (string) $row[$startcol + 15] : null;
            $this->land_registry_number = ($row[$startcol + 16] !== null) ? (string) $row[$startcol + 16] : null;
            $this->region = ($row[$startcol + 17] !== null) ? (string) $row[$startcol + 17] : null;
            $this->depth = ($row[$startcol + 18] !== null) ? (int) $row[$startcol + 18] : null;
            $this->surveyed_length = ($row[$startcol + 19] !== null) ? (int) $row[$startcol + 19] : null;
            $this->discovery_date = ($row[$startcol + 20] !== null) ? (string) $row[$startcol + 20] : null;
            $this->discoverer = ($row[$startcol + 21] !== null) ? (string) $row[$startcol + 21] : null;
            $this->volume = ($row[$startcol + 22] !== null) ? (int) $row[$startcol + 22] : null;
            $this->area = ($row[$startcol + 23] !== null) ? (int) $row[$startcol + 23] : null;
            $this->positive_depth = ($row[$startcol + 24] !== null) ? (int) $row[$startcol + 24] : null;
            $this->negative_depth = ($row[$startcol + 25] !== null) ? (int) $row[$startcol + 25] : null;
            $this->ramification_index = ($row[$startcol + 26] !== null) ? (int) $row[$startcol + 26] : null;
            $this->real_extension = ($row[$startcol + 27] !== null) ? (int) $row[$startcol + 27] : null;
            $this->cave_age = ($row[$startcol + 28] !== null) ? (int) $row[$startcol + 28] : null;
            $this->projected_extension = ($row[$startcol + 29] !== null) ? (int) $row[$startcol + 29] : null;
            $this->exploration_status = ($row[$startcol + 30] !== null) ? (string) $row[$startcol + 30] : null;
            $this->protection_class = ($row[$startcol + 31] !== null) ? (string) $row[$startcol + 31] : null;
            $this->potential_depth = ($row[$startcol + 32] !== null) ? (int) $row[$startcol + 32] : null;
            $this->estimated_length = ($row[$startcol + 33] !== null) ? (int) $row[$startcol + 33] : null;
            $this->altitude = ($row[$startcol + 34] !== null) ? (int) $row[$startcol + 34] : null;
            $this->resetModified();

            $this->setNew(false);

            if ($rehydrate) {
                $this->ensureConsistency();
            }
            $this->postHydrate($row, $startcol, $rehydrate);

            return $startcol + 35; // 35 = CavesPeer::NUM_HYDRATE_COLUMNS.

        } catch (Exception $e) {
            throw new PropelException("Error populating Caves object", $e);
        }
    }

    /**
     * Checks and repairs the internal consistency of the object.
     *
     * This method is executed after an already-instantiated object is re-hydrated
     * from the database.  It exists to check any foreign keys to make sure that
     * the objects related to the current object are correct based on foreign key.
     *
     * You can override this method in the stub class, but you should always invoke
     * the base method from the overridden method (i.e. parent::ensureConsistency()),
     * in case your model changes.
     *
     * @throws PropelException
     */
    public function ensureConsistency()
    {

    } // ensureConsistency

    /**
     * Reloads this object from datastore based on primary key and (optionally) resets all associated objects.
     *
     * This will only work if the object has been saved and has a valid primary key set.
     *
     * @param boolean $deep (optional) Whether to also de-associated any related objects.
     * @param PropelPDO $con (optional) The PropelPDO connection to use.
     * @return void
     * @throws PropelException - if this object is deleted, unsaved or doesn't have pk match in db
     */
    public function reload($deep = false, PropelPDO $con = null)
    {
        if ($this->isDeleted()) {
            throw new PropelException("Cannot reload a deleted object.");
        }

        if ($this->isNew()) {
            throw new PropelException("Cannot reload an unsaved object.");
        }

        if ($con === null) {
            $con = Propel::getConnection(CavesPeer::DATABASE_NAME, Propel::CONNECTION_READ);
        }

        // We don't need to alter the object instance pool; we're just modifying this instance
        // already in the pool.

        $stmt = CavesPeer::doSelectStmt($this->buildPkeyCriteria(), $con);
        $row = $stmt->fetch(PDO::FETCH_NUM);
        $stmt->closeCursor();
        if (!$row) {
            throw new PropelException('Cannot find matching row in the database to reload object values.');
        }
        $this->hydrate($row, 0, true); // rehydrate

        if ($deep) {  // also de-associate any related objects?

        } // if (deep)
    }

    /**
     * Removes this object from datastore and sets delete attribute.
     *
     * @param PropelPDO $con
     * @return void
     * @throws PropelException
     * @throws Exception
     * @see        BaseObject::setDeleted()
     * @see        BaseObject::isDeleted()
     */
    public function delete(PropelPDO $con = null)
    {
        if ($this->isDeleted()) {
            throw new PropelException("This object has already been deleted.");
        }

        if ($con === null) {
            $con = Propel::getConnection(CavesPeer::DATABASE_NAME, Propel::CONNECTION_WRITE);
        }

        $con->beginTransaction();
        try {
            $deleteQuery = CavesQuery::create()
                ->filterByPrimaryKey($this->getPrimaryKey());
            $ret = $this->preDelete($con);
            if ($ret) {
                $deleteQuery->delete($con);
                $this->postDelete($con);
                $con->commit();
                $this->setDeleted(true);
            } else {
                $con->commit();
            }
        } catch (Exception $e) {
            $con->rollBack();
            throw $e;
        }
    }

    /**
     * Persists this object to the database.
     *
     * If the object is new, it inserts it; otherwise an update is performed.
     * All modified related objects will also be persisted in the doSave()
     * method.  This method wraps all precipitate database operations in a
     * single transaction.
     *
     * @param PropelPDO $con
     * @return int             The number of rows affected by this insert/update and any referring fk objects' save() operations.
     * @throws PropelException
     * @throws Exception
     * @see        doSave()
     */
    public function save(PropelPDO $con = null)
    {
        if ($this->isDeleted()) {
            throw new PropelException("You cannot save an object that has been deleted.");
        }

        if ($con === null) {
            $con = Propel::getConnection(CavesPeer::DATABASE_NAME, Propel::CONNECTION_WRITE);
        }

        $con->beginTransaction();
        $isInsert = $this->isNew();
        try {
            $ret = $this->preSave($con);
            if ($isInsert) {
                $ret = $ret && $this->preInsert($con);
            } else {
                $ret = $ret && $this->preUpdate($con);
            }
            if ($ret) {
                $affectedRows = $this->doSave($con);
                if ($isInsert) {
                    $this->postInsert($con);
                } else {
                    $this->postUpdate($con);
                }
                $this->postSave($con);
                CavesPeer::addInstanceToPool($this);
            } else {
                $affectedRows = 0;
            }
            $con->commit();

            return $affectedRows;
        } catch (Exception $e) {
            $con->rollBack();
            throw $e;
        }
    }

    /**
     * Performs the work of inserting or updating the row in the database.
     *
     * If the object is new, it inserts it; otherwise an update is performed.
     * All related objects are also updated in this method.
     *
     * @param PropelPDO $con
     * @return int             The number of rows affected by this insert/update and any referring fk objects' save() operations.
     * @throws PropelException
     * @see        save()
     */
    protected function doSave(PropelPDO $con)
    {
        $affectedRows = 0; // initialize var to track total num of affected rows
        if (!$this->alreadyInSave) {
            $this->alreadyInSave = true;

            if ($this->isNew() || $this->isModified()) {
                // persist changes
                if ($this->isNew()) {
                    $this->doInsert($con);
                } else {
                    $this->doUpdate($con);
                }
                $affectedRows += 1;
                $this->resetModified();
            }

            $this->alreadyInSave = false;

        }

        return $affectedRows;
    } // doSave()

    /**
     * Insert the row in the database.
     *
     * @param PropelPDO $con
     *
     * @throws PropelException
     * @see        doSave()
     */
    protected function doInsert(PropelPDO $con)
    {
        $modifiedColumns = array();
        $index = 0;

        $this->modifiedColumns[] = CavesPeer::ID;
        if (null !== $this->id) {
            throw new PropelException('Cannot insert a value for auto-increment primary key (' . CavesPeer::ID . ')');
        }

         // check the columns in natural order for more readable SQL queries
        if ($this->isColumnModified(CavesPeer::ID)) {
            $modifiedColumns[':p' . $index++]  = '`id`';
        }
        if ($this->isColumnModified(CavesPeer::NAME)) {
            $modifiedColumns[':p' . $index++]  = '`name`';
        }
        if ($this->isColumnModified(CavesPeer::TYPE_ID)) {
            $modifiedColumns[':p' . $index++]  = '`type_id`';
        }
        if ($this->isColumnModified(CavesPeer::IDENTIFICATION_CODE)) {
            $modifiedColumns[':p' . $index++]  = '`identification_code`';
        }
        if ($this->isColumnModified(CavesPeer::DESCRIPTION)) {
            $modifiedColumns[':p' . $index++]  = '`description`';
        }
        if ($this->isColumnModified(CavesPeer::USER_ID)) {
            $modifiedColumns[':p' . $index++]  = '`user_id`';
        }
        if ($this->isColumnModified(CavesPeer::OTHER_TOPONYMS)) {
            $modifiedColumns[':p' . $index++]  = '`other_toponyms`';
        }
        if ($this->isColumnModified(CavesPeer::ROCK_TYPE_ID)) {
            $modifiedColumns[':p' . $index++]  = '`rock_type_id`';
        }
        if ($this->isColumnModified(CavesPeer::ROCK_AGE)) {
            $modifiedColumns[':p' . $index++]  = '`rock_age`';
        }
        if ($this->isColumnModified(CavesPeer::HYDROGRAPHIC_BASIN)) {
            $modifiedColumns[':p' . $index++]  = '`hydrographic_basin`';
        }
        if ($this->isColumnModified(CavesPeer::VALLEY)) {
            $modifiedColumns[':p' . $index++]  = '`valley`';
        }
        if ($this->isColumnModified(CavesPeer::TRIBUTARY_RIVER)) {
            $modifiedColumns[':p' . $index++]  = '`tributary_river`';
        }
        if ($this->isColumnModified(CavesPeer::CLOSEST_ADDRESS)) {
            $modifiedColumns[':p' . $index++]  = '`closest_address`';
        }
        if ($this->isColumnModified(CavesPeer::IS_SHOW_CAVE)) {
            $modifiedColumns[':p' . $index++]  = '`is_show_cave`';
        }
        if ($this->isColumnModified(CavesPeer::SHOW_CAVE_LENGTH)) {
            $modifiedColumns[':p' . $index++]  = '`show_cave_length`';
        }
        if ($this->isColumnModified(CavesPeer::WEBSITE)) {
            $modifiedColumns[':p' . $index++]  = '`website`';
        }
        if ($this->isColumnModified(CavesPeer::LAND_REGISTRY_NUMBER)) {
            $modifiedColumns[':p' . $index++]  = '`land_registry_number`';
        }
        if ($this->isColumnModified(CavesPeer::REGION)) {
            $modifiedColumns[':p' . $index++]  = '`region`';
        }
        if ($this->isColumnModified(CavesPeer::DEPTH)) {
            $modifiedColumns[':p' . $index++]  = '`depth`';
        }
        if ($this->isColumnModified(CavesPeer::SURVEYED_LENGTH)) {
            $modifiedColumns[':p' . $index++]  = '`surveyed_length`';
        }
        if ($this->isColumnModified(CavesPeer::DISCOVERY_DATE)) {
            $modifiedColumns[':p' . $index++]  = '`discovery_date`';
        }
        if ($this->isColumnModified(CavesPeer::DISCOVERER)) {
            $modifiedColumns[':p' . $index++]  = '`discoverer`';
        }
        if ($this->isColumnModified(CavesPeer::VOLUME)) {
            $modifiedColumns[':p' . $index++]  = '`volume`';
        }
        if ($this->isColumnModified(CavesPeer::AREA)) {
            $modifiedColumns[':p' . $index++]  = '`area`';
        }
        if ($this->isColumnModified(CavesPeer::POSITIVE_DEPTH)) {
            $modifiedColumns[':p' . $index++]  = '`positive_depth`';
        }
        if ($this->isColumnModified(CavesPeer::NEGATIVE_DEPTH)) {
            $modifiedColumns[':p' . $index++]  = '`negative_depth`';
        }
        if ($this->isColumnModified(CavesPeer::RAMIFICATION_INDEX)) {
            $modifiedColumns[':p' . $index++]  = '`ramification_index`';
        }
        if ($this->isColumnModified(CavesPeer::REAL_EXTENSION)) {
            $modifiedColumns[':p' . $index++]  = '`real_extension`';
        }
        if ($this->isColumnModified(CavesPeer::CAVE_AGE)) {
            $modifiedColumns[':p' . $index++]  = '`cave_age`';
        }
        if ($this->isColumnModified(CavesPeer::PROJECTED_EXTENSION)) {
            $modifiedColumns[':p' . $index++]  = '`projected_extension`';
        }
        if ($this->isColumnModified(CavesPeer::EXPLORATION_STATUS)) {
            $modifiedColumns[':p' . $index++]  = '`exploration_status`';
        }
        if ($this->isColumnModified(CavesPeer::PROTECTION_CLASS)) {
            $modifiedColumns[':p' . $index++]  = '`protection_class`';
        }
        if ($this->isColumnModified(CavesPeer::POTENTIAL_DEPTH)) {
            $modifiedColumns[':p' . $index++]  = '`potential_depth`';
        }
        if ($this->isColumnModified(CavesPeer::ESTIMATED_LENGTH)) {
            $modifiedColumns[':p' . $index++]  = '`estimated_length`';
        }
        if ($this->isColumnModified(CavesPeer::ALTITUDE)) {
            $modifiedColumns[':p' . $index++]  = '`altitude`';
        }

        $sql = sprintf(
            'INSERT INTO `caves` (%s) VALUES (%s)',
            implode(', ', $modifiedColumns),
            implode(', ', array_keys($modifiedColumns))
        );

        try {
            $stmt = $con->prepare($sql);
            foreach ($modifiedColumns as $identifier => $columnName) {
                switch ($columnName) {
                    case '`id`':
                        $stmt->bindValue($identifier, $this->id, PDO::PARAM_STR);
                        break;
                    case '`name`':
                        $stmt->bindValue($identifier, $this->name, PDO::PARAM_STR);
                        break;
                    case '`type_id`':
                        $stmt->bindValue($identifier, $this->type_id, PDO::PARAM_STR);
                        break;
                    case '`identification_code`':
                        $stmt->bindValue($identifier, $this->identification_code, PDO::PARAM_STR);
                        break;
                    case '`description`':
                        $stmt->bindValue($identifier, $this->description, PDO::PARAM_STR);
                        break;
                    case '`user_id`':
                        $stmt->bindValue($identifier, $this->user_id, PDO::PARAM_STR);
                        break;
                    case '`other_toponyms`':
                        $stmt->bindValue($identifier, $this->other_toponyms, PDO::PARAM_STR);
                        break;
                    case '`rock_type_id`':
                        $stmt->bindValue($identifier, $this->rock_type_id, PDO::PARAM_STR);
                        break;
                    case '`rock_age`':
                        $stmt->bindValue($identifier, $this->rock_age, PDO::PARAM_STR);
                        break;
                    case '`hydrographic_basin`':
                        $stmt->bindValue($identifier, $this->hydrographic_basin, PDO::PARAM_STR);
                        break;
                    case '`valley`':
                        $stmt->bindValue($identifier, $this->valley, PDO::PARAM_STR);
                        break;
                    case '`tributary_river`':
                        $stmt->bindValue($identifier, $this->tributary_river, PDO::PARAM_STR);
                        break;
                    case '`closest_address`':
                        $stmt->bindValue($identifier, $this->closest_address, PDO::PARAM_STR);
                        break;
                    case '`is_show_cave`':
                        $stmt->bindValue($identifier, (int) $this->is_show_cave, PDO::PARAM_INT);
                        break;
                    case '`show_cave_length`':
                        $stmt->bindValue($identifier, $this->show_cave_length, PDO::PARAM_INT);
                        break;
                    case '`website`':
                        $stmt->bindValue($identifier, $this->website, PDO::PARAM_STR);
                        break;
                    case '`land_registry_number`':
                        $stmt->bindValue($identifier, $this->land_registry_number, PDO::PARAM_STR);
                        break;
                    case '`region`':
                        $stmt->bindValue($identifier, $this->region, PDO::PARAM_STR);
                        break;
                    case '`depth`':
                        $stmt->bindValue($identifier, $this->depth, PDO::PARAM_INT);
                        break;
                    case '`surveyed_length`':
                        $stmt->bindValue($identifier, $this->surveyed_length, PDO::PARAM_INT);
                        break;
                    case '`discovery_date`':
                        $stmt->bindValue($identifier, $this->discovery_date, PDO::PARAM_STR);
                        break;
                    case '`discoverer`':
                        $stmt->bindValue($identifier, $this->discoverer, PDO::PARAM_STR);
                        break;
                    case '`volume`':
                        $stmt->bindValue($identifier, $this->volume, PDO::PARAM_INT);
                        break;
                    case '`area`':
                        $stmt->bindValue($identifier, $this->area, PDO::PARAM_INT);
                        break;
                    case '`positive_depth`':
                        $stmt->bindValue($identifier, $this->positive_depth, PDO::PARAM_INT);
                        break;
                    case '`negative_depth`':
                        $stmt->bindValue($identifier, $this->negative_depth, PDO::PARAM_INT);
                        break;
                    case '`ramification_index`':
                        $stmt->bindValue($identifier, $this->ramification_index, PDO::PARAM_INT);
                        break;
                    case '`real_extension`':
                        $stmt->bindValue($identifier, $this->real_extension, PDO::PARAM_INT);
                        break;
                    case '`cave_age`':
                        $stmt->bindValue($identifier, $this->cave_age, PDO::PARAM_INT);
                        break;
                    case '`projected_extension`':
                        $stmt->bindValue($identifier, $this->projected_extension, PDO::PARAM_INT);
                        break;
                    case '`exploration_status`':
                        $stmt->bindValue($identifier, $this->exploration_status, PDO::PARAM_STR);
                        break;
                    case '`protection_class`':
                        $stmt->bindValue($identifier, $this->protection_class, PDO::PARAM_STR);
                        break;
                    case '`potential_depth`':
                        $stmt->bindValue($identifier, $this->potential_depth, PDO::PARAM_INT);
                        break;
                    case '`estimated_length`':
                        $stmt->bindValue($identifier, $this->estimated_length, PDO::PARAM_INT);
                        break;
                    case '`altitude`':
                        $stmt->bindValue($identifier, $this->altitude, PDO::PARAM_INT);
                        break;
                }
            }
            $stmt->execute();
        } catch (Exception $e) {
            Propel::log($e->getMessage(), Propel::LOG_ERR);
            throw new PropelException(sprintf('Unable to execute INSERT statement [%s]', $sql), $e);
        }

        try {
            $pk = $con->lastInsertId();
        } catch (Exception $e) {
            throw new PropelException('Unable to get autoincrement id.', $e);
        }
        $this->setId($pk);

        $this->setNew(false);
    }

    /**
     * Update the row in the database.
     *
     * @param PropelPDO $con
     *
     * @see        doSave()
     */
    protected function doUpdate(PropelPDO $con)
    {
        $selectCriteria = $this->buildPkeyCriteria();
        $valuesCriteria = $this->buildCriteria();
        BasePeer::doUpdate($selectCriteria, $valuesCriteria, $con);
    }

    /**
     * Array of ValidationFailed objects.
     * @var        array ValidationFailed[]
     */
    protected $validationFailures = array();

    /**
     * Gets any ValidationFailed objects that resulted from last call to validate().
     *
     *
     * @return array ValidationFailed[]
     * @see        validate()
     */
    public function getValidationFailures()
    {
        return $this->validationFailures;
    }

    /**
     * Validates the objects modified field values and all objects related to this table.
     *
     * If $columns is either a column name or an array of column names
     * only those columns are validated.
     *
     * @param mixed $columns Column name or an array of column names.
     * @return boolean Whether all columns pass validation.
     * @see        doValidate()
     * @see        getValidationFailures()
     */
    public function validate($columns = null)
    {
        $res = $this->doValidate($columns);
        if ($res === true) {
            $this->validationFailures = array();

            return true;
        }

        $this->validationFailures = $res;

        return false;
    }

    /**
     * This function performs the validation work for complex object models.
     *
     * In addition to checking the current object, all related objects will
     * also be validated.  If all pass then <code>true</code> is returned; otherwise
     * an aggregated array of ValidationFailed objects will be returned.
     *
     * @param array $columns Array of column names to validate.
     * @return mixed <code>true</code> if all validations pass; array of <code>ValidationFailed</code> objects otherwise.
     */
    protected function doValidate($columns = null)
    {
        if (!$this->alreadyInValidation) {
            $this->alreadyInValidation = true;
            $retval = null;

            $failureMap = array();


            if (($retval = CavesPeer::doValidate($this, $columns)) !== true) {
                $failureMap = array_merge($failureMap, $retval);
            }



            $this->alreadyInValidation = false;
        }

        return (!empty($failureMap) ? $failureMap : true);
    }

    /**
     * Retrieves a field from the object by name passed in as a string.
     *
     * @param string $name name
     * @param string $type The type of fieldname the $name is of:
     *               one of the class type constants BasePeer::TYPE_PHPNAME, BasePeer::TYPE_STUDLYPHPNAME
     *               BasePeer::TYPE_COLNAME, BasePeer::TYPE_FIELDNAME, BasePeer::TYPE_NUM.
     *               Defaults to BasePeer::TYPE_PHPNAME
     * @return mixed Value of field.
     */
    public function getByName($name, $type = BasePeer::TYPE_PHPNAME)
    {
        $pos = CavesPeer::translateFieldName($name, $type, BasePeer::TYPE_NUM);
        $field = $this->getByPosition($pos);

        return $field;
    }

    /**
     * Retrieves a field from the object by Position as specified in the xml schema.
     * Zero-based.
     *
     * @param int $pos position in xml schema
     * @return mixed Value of field at $pos
     */
    public function getByPosition($pos)
    {
        switch ($pos) {
            case 0:
                return $this->getId();
                break;
            case 1:
                return $this->getName();
                break;
            case 2:
                return $this->getTypeId();
                break;
            case 3:
                return $this->getIdentificationCode();
                break;
            case 4:
                return $this->getDescription();
                break;
            case 5:
                return $this->getUserId();
                break;
            case 6:
                return $this->getOtherToponyms();
                break;
            case 7:
                return $this->getRockTypeId();
                break;
            case 8:
                return $this->getRockAge();
                break;
            case 9:
                return $this->getHydrographicBasin();
                break;
            case 10:
                return $this->getValley();
                break;
            case 11:
                return $this->getTributaryRiver();
                break;
            case 12:
                return $this->getClosestAddress();
                break;
            case 13:
                return $this->getIsShowCave();
                break;
            case 14:
                return $this->getShowCaveLength();
                break;
            case 15:
                return $this->getWebsite();
                break;
            case 16:
                return $this->getLandRegistryNumber();
                break;
            case 17:
                return $this->getRegion();
                break;
            case 18:
                return $this->getDepth();
                break;
            case 19:
                return $this->getSurveyedLength();
                break;
            case 20:
                return $this->getDiscoveryDate();
                break;
            case 21:
                return $this->getDiscoverer();
                break;
            case 22:
                return $this->getVolume();
                break;
            case 23:
                return $this->getArea();
                break;
            case 24:
                return $this->getPositiveDepth();
                break;
            case 25:
                return $this->getNegativeDepth();
                break;
            case 26:
                return $this->getRamificationIndex();
                break;
            case 27:
                return $this->getRealExtension();
                break;
            case 28:
                return $this->getCaveAge();
                break;
            case 29:
                return $this->getProjectedExtension();
                break;
            case 30:
                return $this->getExplorationStatus();
                break;
            case 31:
                return $this->getProtectionClass();
                break;
            case 32:
                return $this->getPotentialDepth();
                break;
            case 33:
                return $this->getEstimatedLength();
                break;
            case 34:
                return $this->getAltitude();
                break;
            default:
                return null;
                break;
        } // switch()
    }

    /**
     * Exports the object as an array.
     *
     * You can specify the key type of the array by passing one of the class
     * type constants.
     *
     * @param     string  $keyType (optional) One of the class type constants BasePeer::TYPE_PHPNAME, BasePeer::TYPE_STUDLYPHPNAME,
     *                    BasePeer::TYPE_COLNAME, BasePeer::TYPE_FIELDNAME, BasePeer::TYPE_NUM.
     *                    Defaults to BasePeer::TYPE_PHPNAME.
     * @param     boolean $includeLazyLoadColumns (optional) Whether to include lazy loaded columns. Defaults to true.
     * @param     array $alreadyDumpedObjects List of objects to skip to avoid recursion
     *
     * @return array an associative array containing the field names (as keys) and field values
     */
    public function toArray($keyType = BasePeer::TYPE_PHPNAME, $includeLazyLoadColumns = true, $alreadyDumpedObjects = array())
    {
        if (isset($alreadyDumpedObjects['Caves'][$this->getPrimaryKey()])) {
            return '*RECURSION*';
        }
        $alreadyDumpedObjects['Caves'][$this->getPrimaryKey()] = true;
        $keys = CavesPeer::getFieldNames($keyType);
        $result = array(
            $keys[0] => $this->getId(),
            $keys[1] => $this->getName(),
            $keys[2] => $this->getTypeId(),
            $keys[3] => $this->getIdentificationCode(),
            $keys[4] => $this->getDescription(),
            $keys[5] => $this->getUserId(),
            $keys[6] => $this->getOtherToponyms(),
            $keys[7] => $this->getRockTypeId(),
            $keys[8] => $this->getRockAge(),
            $keys[9] => $this->getHydrographicBasin(),
            $keys[10] => $this->getValley(),
            $keys[11] => $this->getTributaryRiver(),
            $keys[12] => $this->getClosestAddress(),
            $keys[13] => $this->getIsShowCave(),
            $keys[14] => $this->getShowCaveLength(),
            $keys[15] => $this->getWebsite(),
            $keys[16] => $this->getLandRegistryNumber(),
            $keys[17] => $this->getRegion(),
            $keys[18] => $this->getDepth(),
            $keys[19] => $this->getSurveyedLength(),
            $keys[20] => $this->getDiscoveryDate(),
            $keys[21] => $this->getDiscoverer(),
            $keys[22] => $this->getVolume(),
            $keys[23] => $this->getArea(),
            $keys[24] => $this->getPositiveDepth(),
            $keys[25] => $this->getNegativeDepth(),
            $keys[26] => $this->getRamificationIndex(),
            $keys[27] => $this->getRealExtension(),
            $keys[28] => $this->getCaveAge(),
            $keys[29] => $this->getProjectedExtension(),
            $keys[30] => $this->getExplorationStatus(),
            $keys[31] => $this->getProtectionClass(),
            $keys[32] => $this->getPotentialDepth(),
            $keys[33] => $this->getEstimatedLength(),
            $keys[34] => $this->getAltitude(),
        );
        $virtualColumns = $this->virtualColumns;
        foreach ($virtualColumns as $key => $virtualColumn) {
            $result[$key] = $virtualColumn;
        }


        return $result;
    }

    /**
     * Sets a field from the object by name passed in as a string.
     *
     * @param string $name peer name
     * @param mixed $value field value
     * @param string $type The type of fieldname the $name is of:
     *                     one of the class type constants BasePeer::TYPE_PHPNAME, BasePeer::TYPE_STUDLYPHPNAME
     *                     BasePeer::TYPE_COLNAME, BasePeer::TYPE_FIELDNAME, BasePeer::TYPE_NUM.
     *                     Defaults to BasePeer::TYPE_PHPNAME
     * @return void
     */
    public function setByName($name, $value, $type = BasePeer::TYPE_PHPNAME)
    {
        $pos = CavesPeer::translateFieldName($name, $type, BasePeer::TYPE_NUM);

        $this->setByPosition($pos, $value);
    }

    /**
     * Sets a field from the object by Position as specified in the xml schema.
     * Zero-based.
     *
     * @param int $pos position in xml schema
     * @param mixed $value field value
     * @return void
     */
    public function setByPosition($pos, $value)
    {
        switch ($pos) {
            case 0:
                $this->setId($value);
                break;
            case 1:
                $this->setName($value);
                break;
            case 2:
                $this->setTypeId($value);
                break;
            case 3:
                $this->setIdentificationCode($value);
                break;
            case 4:
                $this->setDescription($value);
                break;
            case 5:
                $this->setUserId($value);
                break;
            case 6:
                $this->setOtherToponyms($value);
                break;
            case 7:
                $this->setRockTypeId($value);
                break;
            case 8:
                $this->setRockAge($value);
                break;
            case 9:
                $this->setHydrographicBasin($value);
                break;
            case 10:
                $this->setValley($value);
                break;
            case 11:
                $this->setTributaryRiver($value);
                break;
            case 12:
                $this->setClosestAddress($value);
                break;
            case 13:
                $this->setIsShowCave($value);
                break;
            case 14:
                $this->setShowCaveLength($value);
                break;
            case 15:
                $this->setWebsite($value);
                break;
            case 16:
                $this->setLandRegistryNumber($value);
                break;
            case 17:
                $this->setRegion($value);
                break;
            case 18:
                $this->setDepth($value);
                break;
            case 19:
                $this->setSurveyedLength($value);
                break;
            case 20:
                $this->setDiscoveryDate($value);
                break;
            case 21:
                $this->setDiscoverer($value);
                break;
            case 22:
                $this->setVolume($value);
                break;
            case 23:
                $this->setArea($value);
                break;
            case 24:
                $this->setPositiveDepth($value);
                break;
            case 25:
                $this->setNegativeDepth($value);
                break;
            case 26:
                $this->setRamificationIndex($value);
                break;
            case 27:
                $this->setRealExtension($value);
                break;
            case 28:
                $this->setCaveAge($value);
                break;
            case 29:
                $this->setProjectedExtension($value);
                break;
            case 30:
                $this->setExplorationStatus($value);
                break;
            case 31:
                $this->setProtectionClass($value);
                break;
            case 32:
                $this->setPotentialDepth($value);
                break;
            case 33:
                $this->setEstimatedLength($value);
                break;
            case 34:
                $this->setAltitude($value);
                break;
        } // switch()
    }

    /**
     * Populates the object using an array.
     *
     * This is particularly useful when populating an object from one of the
     * request arrays (e.g. $_POST).  This method goes through the column
     * names, checking to see whether a matching key exists in populated
     * array. If so the setByName() method is called for that column.
     *
     * You can specify the key type of the array by additionally passing one
     * of the class type constants BasePeer::TYPE_PHPNAME, BasePeer::TYPE_STUDLYPHPNAME,
     * BasePeer::TYPE_COLNAME, BasePeer::TYPE_FIELDNAME, BasePeer::TYPE_NUM.
     * The default key type is the column's BasePeer::TYPE_PHPNAME
     *
     * @param array  $arr     An array to populate the object from.
     * @param string $keyType The type of keys the array uses.
     * @return void
     */
    public function fromArray($arr, $keyType = BasePeer::TYPE_PHPNAME)
    {
        $keys = CavesPeer::getFieldNames($keyType);

        if (array_key_exists($keys[0], $arr)) $this->setId($arr[$keys[0]]);
        if (array_key_exists($keys[1], $arr)) $this->setName($arr[$keys[1]]);
        if (array_key_exists($keys[2], $arr)) $this->setTypeId($arr[$keys[2]]);
        if (array_key_exists($keys[3], $arr)) $this->setIdentificationCode($arr[$keys[3]]);
        if (array_key_exists($keys[4], $arr)) $this->setDescription($arr[$keys[4]]);
        if (array_key_exists($keys[5], $arr)) $this->setUserId($arr[$keys[5]]);
        if (array_key_exists($keys[6], $arr)) $this->setOtherToponyms($arr[$keys[6]]);
        if (array_key_exists($keys[7], $arr)) $this->setRockTypeId($arr[$keys[7]]);
        if (array_key_exists($keys[8], $arr)) $this->setRockAge($arr[$keys[8]]);
        if (array_key_exists($keys[9], $arr)) $this->setHydrographicBasin($arr[$keys[9]]);
        if (array_key_exists($keys[10], $arr)) $this->setValley($arr[$keys[10]]);
        if (array_key_exists($keys[11], $arr)) $this->setTributaryRiver($arr[$keys[11]]);
        if (array_key_exists($keys[12], $arr)) $this->setClosestAddress($arr[$keys[12]]);
        if (array_key_exists($keys[13], $arr)) $this->setIsShowCave($arr[$keys[13]]);
        if (array_key_exists($keys[14], $arr)) $this->setShowCaveLength($arr[$keys[14]]);
        if (array_key_exists($keys[15], $arr)) $this->setWebsite($arr[$keys[15]]);
        if (array_key_exists($keys[16], $arr)) $this->setLandRegistryNumber($arr[$keys[16]]);
        if (array_key_exists($keys[17], $arr)) $this->setRegion($arr[$keys[17]]);
        if (array_key_exists($keys[18], $arr)) $this->setDepth($arr[$keys[18]]);
        if (array_key_exists($keys[19], $arr)) $this->setSurveyedLength($arr[$keys[19]]);
        if (array_key_exists($keys[20], $arr)) $this->setDiscoveryDate($arr[$keys[20]]);
        if (array_key_exists($keys[21], $arr)) $this->setDiscoverer($arr[$keys[21]]);
        if (array_key_exists($keys[22], $arr)) $this->setVolume($arr[$keys[22]]);
        if (array_key_exists($keys[23], $arr)) $this->setArea($arr[$keys[23]]);
        if (array_key_exists($keys[24], $arr)) $this->setPositiveDepth($arr[$keys[24]]);
        if (array_key_exists($keys[25], $arr)) $this->setNegativeDepth($arr[$keys[25]]);
        if (array_key_exists($keys[26], $arr)) $this->setRamificationIndex($arr[$keys[26]]);
        if (array_key_exists($keys[27], $arr)) $this->setRealExtension($arr[$keys[27]]);
        if (array_key_exists($keys[28], $arr)) $this->setCaveAge($arr[$keys[28]]);
        if (array_key_exists($keys[29], $arr)) $this->setProjectedExtension($arr[$keys[29]]);
        if (array_key_exists($keys[30], $arr)) $this->setExplorationStatus($arr[$keys[30]]);
        if (array_key_exists($keys[31], $arr)) $this->setProtectionClass($arr[$keys[31]]);
        if (array_key_exists($keys[32], $arr)) $this->setPotentialDepth($arr[$keys[32]]);
        if (array_key_exists($keys[33], $arr)) $this->setEstimatedLength($arr[$keys[33]]);
        if (array_key_exists($keys[34], $arr)) $this->setAltitude($arr[$keys[34]]);
    }

    /**
     * Build a Criteria object containing the values of all modified columns in this object.
     *
     * @return Criteria The Criteria object containing all modified values.
     */
    public function buildCriteria()
    {
        $criteria = new Criteria(CavesPeer::DATABASE_NAME);

        if ($this->isColumnModified(CavesPeer::ID)) $criteria->add(CavesPeer::ID, $this->id);
        if ($this->isColumnModified(CavesPeer::NAME)) $criteria->add(CavesPeer::NAME, $this->name);
        if ($this->isColumnModified(CavesPeer::TYPE_ID)) $criteria->add(CavesPeer::TYPE_ID, $this->type_id);
        if ($this->isColumnModified(CavesPeer::IDENTIFICATION_CODE)) $criteria->add(CavesPeer::IDENTIFICATION_CODE, $this->identification_code);
        if ($this->isColumnModified(CavesPeer::DESCRIPTION)) $criteria->add(CavesPeer::DESCRIPTION, $this->description);
        if ($this->isColumnModified(CavesPeer::USER_ID)) $criteria->add(CavesPeer::USER_ID, $this->user_id);
        if ($this->isColumnModified(CavesPeer::OTHER_TOPONYMS)) $criteria->add(CavesPeer::OTHER_TOPONYMS, $this->other_toponyms);
        if ($this->isColumnModified(CavesPeer::ROCK_TYPE_ID)) $criteria->add(CavesPeer::ROCK_TYPE_ID, $this->rock_type_id);
        if ($this->isColumnModified(CavesPeer::ROCK_AGE)) $criteria->add(CavesPeer::ROCK_AGE, $this->rock_age);
        if ($this->isColumnModified(CavesPeer::HYDROGRAPHIC_BASIN)) $criteria->add(CavesPeer::HYDROGRAPHIC_BASIN, $this->hydrographic_basin);
        if ($this->isColumnModified(CavesPeer::VALLEY)) $criteria->add(CavesPeer::VALLEY, $this->valley);
        if ($this->isColumnModified(CavesPeer::TRIBUTARY_RIVER)) $criteria->add(CavesPeer::TRIBUTARY_RIVER, $this->tributary_river);
        if ($this->isColumnModified(CavesPeer::CLOSEST_ADDRESS)) $criteria->add(CavesPeer::CLOSEST_ADDRESS, $this->closest_address);
        if ($this->isColumnModified(CavesPeer::IS_SHOW_CAVE)) $criteria->add(CavesPeer::IS_SHOW_CAVE, $this->is_show_cave);
        if ($this->isColumnModified(CavesPeer::SHOW_CAVE_LENGTH)) $criteria->add(CavesPeer::SHOW_CAVE_LENGTH, $this->show_cave_length);
        if ($this->isColumnModified(CavesPeer::WEBSITE)) $criteria->add(CavesPeer::WEBSITE, $this->website);
        if ($this->isColumnModified(CavesPeer::LAND_REGISTRY_NUMBER)) $criteria->add(CavesPeer::LAND_REGISTRY_NUMBER, $this->land_registry_number);
        if ($this->isColumnModified(CavesPeer::REGION)) $criteria->add(CavesPeer::REGION, $this->region);
        if ($this->isColumnModified(CavesPeer::DEPTH)) $criteria->add(CavesPeer::DEPTH, $this->depth);
        if ($this->isColumnModified(CavesPeer::SURVEYED_LENGTH)) $criteria->add(CavesPeer::SURVEYED_LENGTH, $this->surveyed_length);
        if ($this->isColumnModified(CavesPeer::DISCOVERY_DATE)) $criteria->add(CavesPeer::DISCOVERY_DATE, $this->discovery_date);
        if ($this->isColumnModified(CavesPeer::DISCOVERER)) $criteria->add(CavesPeer::DISCOVERER, $this->discoverer);
        if ($this->isColumnModified(CavesPeer::VOLUME)) $criteria->add(CavesPeer::VOLUME, $this->volume);
        if ($this->isColumnModified(CavesPeer::AREA)) $criteria->add(CavesPeer::AREA, $this->area);
        if ($this->isColumnModified(CavesPeer::POSITIVE_DEPTH)) $criteria->add(CavesPeer::POSITIVE_DEPTH, $this->positive_depth);
        if ($this->isColumnModified(CavesPeer::NEGATIVE_DEPTH)) $criteria->add(CavesPeer::NEGATIVE_DEPTH, $this->negative_depth);
        if ($this->isColumnModified(CavesPeer::RAMIFICATION_INDEX)) $criteria->add(CavesPeer::RAMIFICATION_INDEX, $this->ramification_index);
        if ($this->isColumnModified(CavesPeer::REAL_EXTENSION)) $criteria->add(CavesPeer::REAL_EXTENSION, $this->real_extension);
        if ($this->isColumnModified(CavesPeer::CAVE_AGE)) $criteria->add(CavesPeer::CAVE_AGE, $this->cave_age);
        if ($this->isColumnModified(CavesPeer::PROJECTED_EXTENSION)) $criteria->add(CavesPeer::PROJECTED_EXTENSION, $this->projected_extension);
        if ($this->isColumnModified(CavesPeer::EXPLORATION_STATUS)) $criteria->add(CavesPeer::EXPLORATION_STATUS, $this->exploration_status);
        if ($this->isColumnModified(CavesPeer::PROTECTION_CLASS)) $criteria->add(CavesPeer::PROTECTION_CLASS, $this->protection_class);
        if ($this->isColumnModified(CavesPeer::POTENTIAL_DEPTH)) $criteria->add(CavesPeer::POTENTIAL_DEPTH, $this->potential_depth);
        if ($this->isColumnModified(CavesPeer::ESTIMATED_LENGTH)) $criteria->add(CavesPeer::ESTIMATED_LENGTH, $this->estimated_length);
        if ($this->isColumnModified(CavesPeer::ALTITUDE)) $criteria->add(CavesPeer::ALTITUDE, $this->altitude);

        return $criteria;
    }

    /**
     * Builds a Criteria object containing the primary key for this object.
     *
     * Unlike buildCriteria() this method includes the primary key values regardless
     * of whether or not they have been modified.
     *
     * @return Criteria The Criteria object containing value(s) for primary key(s).
     */
    public function buildPkeyCriteria()
    {
        $criteria = new Criteria(CavesPeer::DATABASE_NAME);
        $criteria->add(CavesPeer::ID, $this->id);

        return $criteria;
    }

    /**
     * Returns the primary key for this object (row).
     * @return string
     */
    public function getPrimaryKey()
    {
        return $this->getId();
    }

    /**
     * Generic method to set the primary key (id column).
     *
     * @param  string $key Primary key.
     * @return void
     */
    public function setPrimaryKey($key)
    {
        $this->setId($key);
    }

    /**
     * Returns true if the primary key for this object is null.
     * @return boolean
     */
    public function isPrimaryKeyNull()
    {

        return null === $this->getId();
    }

    /**
     * Sets contents of passed object to values from current object.
     *
     * If desired, this method can also make copies of all associated (fkey referrers)
     * objects.
     *
     * @param object $copyObj An object of Caves (or compatible) type.
     * @param boolean $deepCopy Whether to also copy all rows that refer (by fkey) to the current row.
     * @param boolean $makeNew Whether to reset autoincrement PKs and make the object new.
     * @throws PropelException
     */
    public function copyInto($copyObj, $deepCopy = false, $makeNew = true)
    {
        $copyObj->setName($this->getName());
        $copyObj->setTypeId($this->getTypeId());
        $copyObj->setIdentificationCode($this->getIdentificationCode());
        $copyObj->setDescription($this->getDescription());
        $copyObj->setUserId($this->getUserId());
        $copyObj->setOtherToponyms($this->getOtherToponyms());
        $copyObj->setRockTypeId($this->getRockTypeId());
        $copyObj->setRockAge($this->getRockAge());
        $copyObj->setHydrographicBasin($this->getHydrographicBasin());
        $copyObj->setValley($this->getValley());
        $copyObj->setTributaryRiver($this->getTributaryRiver());
        $copyObj->setClosestAddress($this->getClosestAddress());
        $copyObj->setIsShowCave($this->getIsShowCave());
        $copyObj->setShowCaveLength($this->getShowCaveLength());
        $copyObj->setWebsite($this->getWebsite());
        $copyObj->setLandRegistryNumber($this->getLandRegistryNumber());
        $copyObj->setRegion($this->getRegion());
        $copyObj->setDepth($this->getDepth());
        $copyObj->setSurveyedLength($this->getSurveyedLength());
        $copyObj->setDiscoveryDate($this->getDiscoveryDate());
        $copyObj->setDiscoverer($this->getDiscoverer());
        $copyObj->setVolume($this->getVolume());
        $copyObj->setArea($this->getArea());
        $copyObj->setPositiveDepth($this->getPositiveDepth());
        $copyObj->setNegativeDepth($this->getNegativeDepth());
        $copyObj->setRamificationIndex($this->getRamificationIndex());
        $copyObj->setRealExtension($this->getRealExtension());
        $copyObj->setCaveAge($this->getCaveAge());
        $copyObj->setProjectedExtension($this->getProjectedExtension());
        $copyObj->setExplorationStatus($this->getExplorationStatus());
        $copyObj->setProtectionClass($this->getProtectionClass());
        $copyObj->setPotentialDepth($this->getPotentialDepth());
        $copyObj->setEstimatedLength($this->getEstimatedLength());
        $copyObj->setAltitude($this->getAltitude());
        if ($makeNew) {
            $copyObj->setNew(true);
            $copyObj->setId(NULL); // this is a auto-increment column, so set to default value
        }
    }

    /**
     * Makes a copy of this object that will be inserted as a new row in table when saved.
     * It creates a new object filling in the simple attributes, but skipping any primary
     * keys that are defined for the table.
     *
     * If desired, this method can also make copies of all associated (fkey referrers)
     * objects.
     *
     * @param boolean $deepCopy Whether to also copy all rows that refer (by fkey) to the current row.
     * @return Caves Clone of current object.
     * @throws PropelException
     */
    public function copy($deepCopy = false)
    {
        // we use get_class(), because this might be a subclass
        $clazz = get_class($this);
        $copyObj = new $clazz();
        $this->copyInto($copyObj, $deepCopy);

        return $copyObj;
    }

    /**
     * Returns a peer instance associated with this om.
     *
     * Since Peer classes are not to have any instance attributes, this method returns the
     * same instance for all member of this class. The method could therefore
     * be static, but this would prevent one from overriding the behavior.
     *
     * @return CavesPeer
     */
    public function getPeer()
    {
        if (self::$peer === null) {
            self::$peer = new CavesPeer();
        }

        return self::$peer;
    }

    /**
     * Clears the current object and sets all attributes to their default values
     */
    public function clear()
    {
        $this->id = null;
        $this->name = null;
        $this->type_id = null;
        $this->identification_code = null;
        $this->description = null;
        $this->user_id = null;
        $this->other_toponyms = null;
        $this->rock_type_id = null;
        $this->rock_age = null;
        $this->hydrographic_basin = null;
        $this->valley = null;
        $this->tributary_river = null;
        $this->closest_address = null;
        $this->is_show_cave = null;
        $this->show_cave_length = null;
        $this->website = null;
        $this->land_registry_number = null;
        $this->region = null;
        $this->depth = null;
        $this->surveyed_length = null;
        $this->discovery_date = null;
        $this->discoverer = null;
        $this->volume = null;
        $this->area = null;
        $this->positive_depth = null;
        $this->negative_depth = null;
        $this->ramification_index = null;
        $this->real_extension = null;
        $this->cave_age = null;
        $this->projected_extension = null;
        $this->exploration_status = null;
        $this->protection_class = null;
        $this->potential_depth = null;
        $this->estimated_length = null;
        $this->altitude = null;
        $this->alreadyInSave = false;
        $this->alreadyInValidation = false;
        $this->alreadyInClearAllReferencesDeep = false;
        $this->clearAllReferences();
        $this->resetModified();
        $this->setNew(true);
        $this->setDeleted(false);
    }

    /**
     * Resets all references to other model objects or collections of model objects.
     *
     * This method is a user-space workaround for PHP's inability to garbage collect
     * objects with circular references (even in PHP 5.3). This is currently necessary
     * when using Propel in certain daemon or large-volume/high-memory operations.
     *
     * @param boolean $deep Whether to also clear the references on all referrer objects.
     */
    public function clearAllReferences($deep = false)
    {
        if ($deep && !$this->alreadyInClearAllReferencesDeep) {
            $this->alreadyInClearAllReferencesDeep = true;

            $this->alreadyInClearAllReferencesDeep = false;
        } // if ($deep)

    }

    /**
     * return the string representation of this object
     *
     * @return string
     */
    public function __toString()
    {
        return (string) $this->exportTo(CavesPeer::DEFAULT_STRING_FORMAT);
    }

    /**
     * return true is the object is in saving state
     *
     * @return boolean
     */
    public function isAlreadyInSave()
    {
        return $this->alreadyInSave;
    }

}
