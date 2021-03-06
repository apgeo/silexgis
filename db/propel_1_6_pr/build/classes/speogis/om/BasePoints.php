<?php


/**
 * Base class that represents a row from the 'points' table.
 *
 *
 *
 * @package    propel.generator.speogis.om
 */
abstract class BasePoints extends BaseObject implements Persistent
{
    /**
     * Peer class name
     */
    const PEER = 'PointsPeer';

    /**
     * The Peer class.
     * Instance provides a convenient way of calling static methods on a class
     * that calling code may not be able to identify.
     * @var        PointsPeer
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
     * The value for the lat field.
     * @var        double
     */
    protected $lat;

    /**
     * The value for the long field.
     * @var        double
     */
    protected $long;

    /**
     * The value for the elevation field.
     * @var        int
     */
    protected $elevation;

    /**
     * The value for the gpx_name field.
     * @var        string
     */
    protected $gpx_name;

    /**
     * The value for the gpx_sym field.
     * @var        string
     */
    protected $gpx_sym;

    /**
     * The value for the gpx_type field.
     * @var        string
     */
    protected $gpx_type;

    /**
     * The value for the gpx_cmt field.
     * @var        string
     */
    protected $gpx_cmt;

    /**
     * The value for the gpx_sat field.
     * @var        int
     */
    protected $gpx_sat;

    /**
     * The value for the gpx_fix field.
     * @var        string
     */
    protected $gpx_fix;

    /**
     * The value for the gpx_time field.
     * @var        string
     */
    protected $gpx_time;

    /**
     * The value for the _type field.
     * @var        int
     */
    protected $_type;

    /**
     * The value for the _details field.
     * @var        string
     */
    protected $_details;

    /**
     * The value for the added_by_user_id field.
     * @var        string
     */
    protected $added_by_user_id;

    /**
     * The value for the add_time field.
     * @var        string
     */
    protected $add_time;

    /**
     * The value for the _id_point_type field.
     * @var        string
     */
    protected $_id_point_type;

    /**
     * The value for the spatial_geometry field.
     * @var        string
     */
    protected $spatial_geometry;

    /**
     * The value for the update_time field.
     * @var        string
     */
    protected $update_time;

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
     * Get the [lat] column value.
     *
     * @return double
     */
    public function getLat()
    {

        return $this->lat;
    }

    /**
     * Get the [long] column value.
     *
     * @return double
     */
    public function getLong()
    {

        return $this->long;
    }

    /**
     * Get the [elevation] column value.
     *
     * @return int
     */
    public function getElevation()
    {

        return $this->elevation;
    }

    /**
     * Get the [gpx_name] column value.
     *
     * @return string
     */
    public function getGpxName()
    {

        return $this->gpx_name;
    }

    /**
     * Get the [gpx_sym] column value.
     *
     * @return string
     */
    public function getGpxSym()
    {

        return $this->gpx_sym;
    }

    /**
     * Get the [gpx_type] column value.
     *
     * @return string
     */
    public function getGpxType()
    {

        return $this->gpx_type;
    }

    /**
     * Get the [gpx_cmt] column value.
     *
     * @return string
     */
    public function getGpxCmt()
    {

        return $this->gpx_cmt;
    }

    /**
     * Get the [gpx_sat] column value.
     *
     * @return int
     */
    public function getGpxSat()
    {

        return $this->gpx_sat;
    }

    /**
     * Get the [gpx_fix] column value.
     *
     * @return string
     */
    public function getGpxFix()
    {

        return $this->gpx_fix;
    }

    /**
     * Get the [optionally formatted] temporal [gpx_time] column value.
     *
     *
     * @param string $format The date/time format string (either date()-style or strftime()-style).
     *				 If format is null, then the raw DateTime object will be returned.
     * @return mixed Formatted date/time value as string or DateTime object (if format is null), null if column is null, and 0 if column value is 0000-00-00 00:00:00
     * @throws PropelException - if unable to parse/validate the date/time value.
     */
    public function getGpxTime($format = 'Y-m-d H:i:s')
    {
        if ($this->gpx_time === null) {
            return null;
        }

        if ($this->gpx_time === '0000-00-00 00:00:00') {
            // while technically this is not a default value of null,
            // this seems to be closest in meaning.
            return null;
        }

        try {
            $dt = new DateTime($this->gpx_time);
        } catch (Exception $x) {
            throw new PropelException("Internally stored date/time/timestamp value could not be converted to DateTime: " . var_export($this->gpx_time, true), $x);
        }

        if ($format === null) {
            // Because propel.useDateTimeClass is true, we return a DateTime object.
            return $dt;
        }

        if (strpos($format, '%') !== false) {
            return strftime($format, $dt->format('U'));
        }

        return $dt->format($format);

    }

    /**
     * Get the [_type] column value.
     *
     * @return int
     */
    public function getType()
    {

        return $this->_type;
    }

    /**
     * Get the [_details] column value.
     *
     * @return string
     */
    public function getDetails()
    {

        return $this->_details;
    }

    /**
     * Get the [added_by_user_id] column value.
     *
     * @return string
     */
    public function getAddedByUserId()
    {

        return $this->added_by_user_id;
    }

    /**
     * Get the [optionally formatted] temporal [add_time] column value.
     *
     *
     * @param string $format The date/time format string (either date()-style or strftime()-style).
     *				 If format is null, then the raw DateTime object will be returned.
     * @return mixed Formatted date/time value as string or DateTime object (if format is null), null if column is null, and 0 if column value is 0000-00-00 00:00:00
     * @throws PropelException - if unable to parse/validate the date/time value.
     */
    public function getAddTime($format = 'Y-m-d H:i:s')
    {
        if ($this->add_time === null) {
            return null;
        }

        if ($this->add_time === '0000-00-00 00:00:00') {
            // while technically this is not a default value of null,
            // this seems to be closest in meaning.
            return null;
        }

        try {
            $dt = new DateTime($this->add_time);
        } catch (Exception $x) {
            throw new PropelException("Internally stored date/time/timestamp value could not be converted to DateTime: " . var_export($this->add_time, true), $x);
        }

        if ($format === null) {
            // Because propel.useDateTimeClass is true, we return a DateTime object.
            return $dt;
        }

        if (strpos($format, '%') !== false) {
            return strftime($format, $dt->format('U'));
        }

        return $dt->format($format);

    }

    /**
     * Get the [_id_point_type] column value.
     *
     * @return string
     */
    public function getIdPointType()
    {

        return $this->_id_point_type;
    }

    /**
     * Get the [spatial_geometry] column value.
     *
     * @return string
     */
    public function getSpatialGeometry()
    {

        return $this->spatial_geometry;
    }

    /**
     * Get the [optionally formatted] temporal [update_time] column value.
     *
     *
     * @param string $format The date/time format string (either date()-style or strftime()-style).
     *				 If format is null, then the raw DateTime object will be returned.
     * @return mixed Formatted date/time value as string or DateTime object (if format is null), null if column is null, and 0 if column value is 0000-00-00 00:00:00
     * @throws PropelException - if unable to parse/validate the date/time value.
     */
    public function getUpdateTime($format = 'Y-m-d H:i:s')
    {
        if ($this->update_time === null) {
            return null;
        }

        if ($this->update_time === '0000-00-00 00:00:00') {
            // while technically this is not a default value of null,
            // this seems to be closest in meaning.
            return null;
        }

        try {
            $dt = new DateTime($this->update_time);
        } catch (Exception $x) {
            throw new PropelException("Internally stored date/time/timestamp value could not be converted to DateTime: " . var_export($this->update_time, true), $x);
        }

        if ($format === null) {
            // Because propel.useDateTimeClass is true, we return a DateTime object.
            return $dt;
        }

        if (strpos($format, '%') !== false) {
            return strftime($format, $dt->format('U'));
        }

        return $dt->format($format);

    }

    /**
     * Set the value of [id] column.
     *
     * @param  string $v new value
     * @return Points The current object (for fluent API support)
     */
    public function setId($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (string) $v;
        }

        if ($this->id !== $v) {
            $this->id = $v;
            $this->modifiedColumns[] = PointsPeer::ID;
        }


        return $this;
    } // setId()

    /**
     * Set the value of [lat] column.
     *
     * @param  double $v new value
     * @return Points The current object (for fluent API support)
     */
    public function setLat($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (double) $v;
        }

        if ($this->lat !== $v) {
            $this->lat = $v;
            $this->modifiedColumns[] = PointsPeer::LAT;
        }


        return $this;
    } // setLat()

    /**
     * Set the value of [long] column.
     *
     * @param  double $v new value
     * @return Points The current object (for fluent API support)
     */
    public function setLong($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (double) $v;
        }

        if ($this->long !== $v) {
            $this->long = $v;
            $this->modifiedColumns[] = PointsPeer::LONG;
        }


        return $this;
    } // setLong()

    /**
     * Set the value of [elevation] column.
     *
     * @param  int $v new value
     * @return Points The current object (for fluent API support)
     */
    public function setElevation($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (int) $v;
        }

        if ($this->elevation !== $v) {
            $this->elevation = $v;
            $this->modifiedColumns[] = PointsPeer::ELEVATION;
        }


        return $this;
    } // setElevation()

    /**
     * Set the value of [gpx_name] column.
     *
     * @param  string $v new value
     * @return Points The current object (for fluent API support)
     */
    public function setGpxName($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->gpx_name !== $v) {
            $this->gpx_name = $v;
            $this->modifiedColumns[] = PointsPeer::GPX_NAME;
        }


        return $this;
    } // setGpxName()

    /**
     * Set the value of [gpx_sym] column.
     *
     * @param  string $v new value
     * @return Points The current object (for fluent API support)
     */
    public function setGpxSym($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->gpx_sym !== $v) {
            $this->gpx_sym = $v;
            $this->modifiedColumns[] = PointsPeer::GPX_SYM;
        }


        return $this;
    } // setGpxSym()

    /**
     * Set the value of [gpx_type] column.
     *
     * @param  string $v new value
     * @return Points The current object (for fluent API support)
     */
    public function setGpxType($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->gpx_type !== $v) {
            $this->gpx_type = $v;
            $this->modifiedColumns[] = PointsPeer::GPX_TYPE;
        }


        return $this;
    } // setGpxType()

    /**
     * Set the value of [gpx_cmt] column.
     *
     * @param  string $v new value
     * @return Points The current object (for fluent API support)
     */
    public function setGpxCmt($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->gpx_cmt !== $v) {
            $this->gpx_cmt = $v;
            $this->modifiedColumns[] = PointsPeer::GPX_CMT;
        }


        return $this;
    } // setGpxCmt()

    /**
     * Set the value of [gpx_sat] column.
     *
     * @param  int $v new value
     * @return Points The current object (for fluent API support)
     */
    public function setGpxSat($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (int) $v;
        }

        if ($this->gpx_sat !== $v) {
            $this->gpx_sat = $v;
            $this->modifiedColumns[] = PointsPeer::GPX_SAT;
        }


        return $this;
    } // setGpxSat()

    /**
     * Set the value of [gpx_fix] column.
     *
     * @param  string $v new value
     * @return Points The current object (for fluent API support)
     */
    public function setGpxFix($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->gpx_fix !== $v) {
            $this->gpx_fix = $v;
            $this->modifiedColumns[] = PointsPeer::GPX_FIX;
        }


        return $this;
    } // setGpxFix()

    /**
     * Sets the value of [gpx_time] column to a normalized version of the date/time value specified.
     *
     * @param mixed $v string, integer (timestamp), or DateTime value.
     *               Empty strings are treated as null.
     * @return Points The current object (for fluent API support)
     */
    public function setGpxTime($v)
    {
        $dt = PropelDateTime::newInstance($v, null, 'DateTime');
        if ($this->gpx_time !== null || $dt !== null) {
            $currentDateAsString = ($this->gpx_time !== null && $tmpDt = new DateTime($this->gpx_time)) ? $tmpDt->format('Y-m-d H:i:s') : null;
            $newDateAsString = $dt ? $dt->format('Y-m-d H:i:s') : null;
            if ($currentDateAsString !== $newDateAsString) {
                $this->gpx_time = $newDateAsString;
                $this->modifiedColumns[] = PointsPeer::GPX_TIME;
            }
        } // if either are not null


        return $this;
    } // setGpxTime()

    /**
     * Set the value of [_type] column.
     *
     * @param  int $v new value
     * @return Points The current object (for fluent API support)
     */
    public function setType($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (int) $v;
        }

        if ($this->_type !== $v) {
            $this->_type = $v;
            $this->modifiedColumns[] = PointsPeer::_TYPE;
        }


        return $this;
    } // setType()

    /**
     * Set the value of [_details] column.
     *
     * @param  string $v new value
     * @return Points The current object (for fluent API support)
     */
    public function setDetails($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->_details !== $v) {
            $this->_details = $v;
            $this->modifiedColumns[] = PointsPeer::_DETAILS;
        }


        return $this;
    } // setDetails()

    /**
     * Set the value of [added_by_user_id] column.
     *
     * @param  string $v new value
     * @return Points The current object (for fluent API support)
     */
    public function setAddedByUserId($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (string) $v;
        }

        if ($this->added_by_user_id !== $v) {
            $this->added_by_user_id = $v;
            $this->modifiedColumns[] = PointsPeer::ADDED_BY_USER_ID;
        }


        return $this;
    } // setAddedByUserId()

    /**
     * Sets the value of [add_time] column to a normalized version of the date/time value specified.
     *
     * @param mixed $v string, integer (timestamp), or DateTime value.
     *               Empty strings are treated as null.
     * @return Points The current object (for fluent API support)
     */
    public function setAddTime($v)
    {
        $dt = PropelDateTime::newInstance($v, null, 'DateTime');
        if ($this->add_time !== null || $dt !== null) {
            $currentDateAsString = ($this->add_time !== null && $tmpDt = new DateTime($this->add_time)) ? $tmpDt->format('Y-m-d H:i:s') : null;
            $newDateAsString = $dt ? $dt->format('Y-m-d H:i:s') : null;
            if ($currentDateAsString !== $newDateAsString) {
                $this->add_time = $newDateAsString;
                $this->modifiedColumns[] = PointsPeer::ADD_TIME;
            }
        } // if either are not null


        return $this;
    } // setAddTime()

    /**
     * Set the value of [_id_point_type] column.
     *
     * @param  string $v new value
     * @return Points The current object (for fluent API support)
     */
    public function setIdPointType($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (string) $v;
        }

        if ($this->_id_point_type !== $v) {
            $this->_id_point_type = $v;
            $this->modifiedColumns[] = PointsPeer::_ID_POINT_TYPE;
        }


        return $this;
    } // setIdPointType()

    /**
     * Set the value of [spatial_geometry] column.
     *
     * @param  string $v new value
     * @return Points The current object (for fluent API support)
     */
    public function setSpatialGeometry($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->spatial_geometry !== $v) {
            $this->spatial_geometry = $v;
            $this->modifiedColumns[] = PointsPeer::SPATIAL_GEOMETRY;
        }


        return $this;
    } // setSpatialGeometry()

    /**
     * Sets the value of [update_time] column to a normalized version of the date/time value specified.
     *
     * @param mixed $v string, integer (timestamp), or DateTime value.
     *               Empty strings are treated as null.
     * @return Points The current object (for fluent API support)
     */
    public function setUpdateTime($v)
    {
        $dt = PropelDateTime::newInstance($v, null, 'DateTime');
        if ($this->update_time !== null || $dt !== null) {
            $currentDateAsString = ($this->update_time !== null && $tmpDt = new DateTime($this->update_time)) ? $tmpDt->format('Y-m-d H:i:s') : null;
            $newDateAsString = $dt ? $dt->format('Y-m-d H:i:s') : null;
            if ($currentDateAsString !== $newDateAsString) {
                $this->update_time = $newDateAsString;
                $this->modifiedColumns[] = PointsPeer::UPDATE_TIME;
            }
        } // if either are not null


        return $this;
    } // setUpdateTime()

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
            $this->lat = ($row[$startcol + 1] !== null) ? (double) $row[$startcol + 1] : null;
            $this->long = ($row[$startcol + 2] !== null) ? (double) $row[$startcol + 2] : null;
            $this->elevation = ($row[$startcol + 3] !== null) ? (int) $row[$startcol + 3] : null;
            $this->gpx_name = ($row[$startcol + 4] !== null) ? (string) $row[$startcol + 4] : null;
            $this->gpx_sym = ($row[$startcol + 5] !== null) ? (string) $row[$startcol + 5] : null;
            $this->gpx_type = ($row[$startcol + 6] !== null) ? (string) $row[$startcol + 6] : null;
            $this->gpx_cmt = ($row[$startcol + 7] !== null) ? (string) $row[$startcol + 7] : null;
            $this->gpx_sat = ($row[$startcol + 8] !== null) ? (int) $row[$startcol + 8] : null;
            $this->gpx_fix = ($row[$startcol + 9] !== null) ? (string) $row[$startcol + 9] : null;
            $this->gpx_time = ($row[$startcol + 10] !== null) ? (string) $row[$startcol + 10] : null;
            $this->_type = ($row[$startcol + 11] !== null) ? (int) $row[$startcol + 11] : null;
            $this->_details = ($row[$startcol + 12] !== null) ? (string) $row[$startcol + 12] : null;
            $this->added_by_user_id = ($row[$startcol + 13] !== null) ? (string) $row[$startcol + 13] : null;
            $this->add_time = ($row[$startcol + 14] !== null) ? (string) $row[$startcol + 14] : null;
            $this->_id_point_type = ($row[$startcol + 15] !== null) ? (string) $row[$startcol + 15] : null;
            $this->spatial_geometry = ($row[$startcol + 16] !== null) ? (string) $row[$startcol + 16] : null;
            $this->update_time = ($row[$startcol + 17] !== null) ? (string) $row[$startcol + 17] : null;
            $this->resetModified();

            $this->setNew(false);

            if ($rehydrate) {
                $this->ensureConsistency();
            }
            $this->postHydrate($row, $startcol, $rehydrate);

            return $startcol + 18; // 18 = PointsPeer::NUM_HYDRATE_COLUMNS.

        } catch (Exception $e) {
            throw new PropelException("Error populating Points object", $e);
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
            $con = Propel::getConnection(PointsPeer::DATABASE_NAME, Propel::CONNECTION_READ);
        }

        // We don't need to alter the object instance pool; we're just modifying this instance
        // already in the pool.

        $stmt = PointsPeer::doSelectStmt($this->buildPkeyCriteria(), $con);
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
            $con = Propel::getConnection(PointsPeer::DATABASE_NAME, Propel::CONNECTION_WRITE);
        }

        $con->beginTransaction();
        try {
            $deleteQuery = PointsQuery::create()
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
            $con = Propel::getConnection(PointsPeer::DATABASE_NAME, Propel::CONNECTION_WRITE);
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
                PointsPeer::addInstanceToPool($this);
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

        $this->modifiedColumns[] = PointsPeer::ID;
        if (null !== $this->id) {
            throw new PropelException('Cannot insert a value for auto-increment primary key (' . PointsPeer::ID . ')');
        }

         // check the columns in natural order for more readable SQL queries
        if ($this->isColumnModified(PointsPeer::ID)) {
            $modifiedColumns[':p' . $index++]  = '`id`';
        }
        if ($this->isColumnModified(PointsPeer::LAT)) {
            $modifiedColumns[':p' . $index++]  = '`lat`';
        }
        if ($this->isColumnModified(PointsPeer::LONG)) {
            $modifiedColumns[':p' . $index++]  = '`long`';
        }
        if ($this->isColumnModified(PointsPeer::ELEVATION)) {
            $modifiedColumns[':p' . $index++]  = '`elevation`';
        }
        if ($this->isColumnModified(PointsPeer::GPX_NAME)) {
            $modifiedColumns[':p' . $index++]  = '`gpx_name`';
        }
        if ($this->isColumnModified(PointsPeer::GPX_SYM)) {
            $modifiedColumns[':p' . $index++]  = '`gpx_sym`';
        }
        if ($this->isColumnModified(PointsPeer::GPX_TYPE)) {
            $modifiedColumns[':p' . $index++]  = '`gpx_type`';
        }
        if ($this->isColumnModified(PointsPeer::GPX_CMT)) {
            $modifiedColumns[':p' . $index++]  = '`gpx_cmt`';
        }
        if ($this->isColumnModified(PointsPeer::GPX_SAT)) {
            $modifiedColumns[':p' . $index++]  = '`gpx_sat`';
        }
        if ($this->isColumnModified(PointsPeer::GPX_FIX)) {
            $modifiedColumns[':p' . $index++]  = '`gpx_fix`';
        }
        if ($this->isColumnModified(PointsPeer::GPX_TIME)) {
            $modifiedColumns[':p' . $index++]  = '`gpx_time`';
        }
        if ($this->isColumnModified(PointsPeer::_TYPE)) {
            $modifiedColumns[':p' . $index++]  = '`_type`';
        }
        if ($this->isColumnModified(PointsPeer::_DETAILS)) {
            $modifiedColumns[':p' . $index++]  = '`_details`';
        }
        if ($this->isColumnModified(PointsPeer::ADDED_BY_USER_ID)) {
            $modifiedColumns[':p' . $index++]  = '`added_by_user_id`';
        }
        if ($this->isColumnModified(PointsPeer::ADD_TIME)) {
            $modifiedColumns[':p' . $index++]  = '`add_time`';
        }
        if ($this->isColumnModified(PointsPeer::_ID_POINT_TYPE)) {
            $modifiedColumns[':p' . $index++]  = '`_id_point_type`';
        }
        if ($this->isColumnModified(PointsPeer::SPATIAL_GEOMETRY)) {
            $modifiedColumns[':p' . $index++]  = '`spatial_geometry`';
        }
        if ($this->isColumnModified(PointsPeer::UPDATE_TIME)) {
            $modifiedColumns[':p' . $index++]  = '`update_time`';
        }

        $sql = sprintf(
            'INSERT INTO `points` (%s) VALUES (%s)',
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
                    case '`lat`':
                        $stmt->bindValue($identifier, $this->lat, PDO::PARAM_STR);
                        break;
                    case '`long`':
                        $stmt->bindValue($identifier, $this->long, PDO::PARAM_STR);
                        break;
                    case '`elevation`':
                        $stmt->bindValue($identifier, $this->elevation, PDO::PARAM_INT);
                        break;
                    case '`gpx_name`':
                        $stmt->bindValue($identifier, $this->gpx_name, PDO::PARAM_STR);
                        break;
                    case '`gpx_sym`':
                        $stmt->bindValue($identifier, $this->gpx_sym, PDO::PARAM_STR);
                        break;
                    case '`gpx_type`':
                        $stmt->bindValue($identifier, $this->gpx_type, PDO::PARAM_STR);
                        break;
                    case '`gpx_cmt`':
                        $stmt->bindValue($identifier, $this->gpx_cmt, PDO::PARAM_STR);
                        break;
                    case '`gpx_sat`':
                        $stmt->bindValue($identifier, $this->gpx_sat, PDO::PARAM_INT);
                        break;
                    case '`gpx_fix`':
                        $stmt->bindValue($identifier, $this->gpx_fix, PDO::PARAM_STR);
                        break;
                    case '`gpx_time`':
                        $stmt->bindValue($identifier, $this->gpx_time, PDO::PARAM_STR);
                        break;
                    case '`_type`':
                        $stmt->bindValue($identifier, $this->_type, PDO::PARAM_INT);
                        break;
                    case '`_details`':
                        $stmt->bindValue($identifier, $this->_details, PDO::PARAM_STR);
                        break;
                    case '`added_by_user_id`':
                        $stmt->bindValue($identifier, $this->added_by_user_id, PDO::PARAM_STR);
                        break;
                    case '`add_time`':
                        $stmt->bindValue($identifier, $this->add_time, PDO::PARAM_STR);
                        break;
                    case '`_id_point_type`':
                        $stmt->bindValue($identifier, $this->_id_point_type, PDO::PARAM_STR);
                        break;
                    case '`spatial_geometry`':
                        $stmt->bindValue($identifier, $this->spatial_geometry, PDO::PARAM_STR);
                        break;
                    case '`update_time`':
                        $stmt->bindValue($identifier, $this->update_time, PDO::PARAM_STR);
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


            if (($retval = PointsPeer::doValidate($this, $columns)) !== true) {
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
        $pos = PointsPeer::translateFieldName($name, $type, BasePeer::TYPE_NUM);
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
                return $this->getLat();
                break;
            case 2:
                return $this->getLong();
                break;
            case 3:
                return $this->getElevation();
                break;
            case 4:
                return $this->getGpxName();
                break;
            case 5:
                return $this->getGpxSym();
                break;
            case 6:
                return $this->getGpxType();
                break;
            case 7:
                return $this->getGpxCmt();
                break;
            case 8:
                return $this->getGpxSat();
                break;
            case 9:
                return $this->getGpxFix();
                break;
            case 10:
                return $this->getGpxTime();
                break;
            case 11:
                return $this->getType();
                break;
            case 12:
                return $this->getDetails();
                break;
            case 13:
                return $this->getAddedByUserId();
                break;
            case 14:
                return $this->getAddTime();
                break;
            case 15:
                return $this->getIdPointType();
                break;
            case 16:
                return $this->getSpatialGeometry();
                break;
            case 17:
                return $this->getUpdateTime();
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
        if (isset($alreadyDumpedObjects['Points'][$this->getPrimaryKey()])) {
            return '*RECURSION*';
        }
        $alreadyDumpedObjects['Points'][$this->getPrimaryKey()] = true;
        $keys = PointsPeer::getFieldNames($keyType);
        $result = array(
            $keys[0] => $this->getId(),
            $keys[1] => $this->getLat(),
            $keys[2] => $this->getLong(),
            $keys[3] => $this->getElevation(),
            $keys[4] => $this->getGpxName(),
            $keys[5] => $this->getGpxSym(),
            $keys[6] => $this->getGpxType(),
            $keys[7] => $this->getGpxCmt(),
            $keys[8] => $this->getGpxSat(),
            $keys[9] => $this->getGpxFix(),
            $keys[10] => $this->getGpxTime(),
            $keys[11] => $this->getType(),
            $keys[12] => $this->getDetails(),
            $keys[13] => $this->getAddedByUserId(),
            $keys[14] => $this->getAddTime(),
            $keys[15] => $this->getIdPointType(),
            $keys[16] => $this->getSpatialGeometry(),
            $keys[17] => $this->getUpdateTime(),
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
        $pos = PointsPeer::translateFieldName($name, $type, BasePeer::TYPE_NUM);

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
                $this->setLat($value);
                break;
            case 2:
                $this->setLong($value);
                break;
            case 3:
                $this->setElevation($value);
                break;
            case 4:
                $this->setGpxName($value);
                break;
            case 5:
                $this->setGpxSym($value);
                break;
            case 6:
                $this->setGpxType($value);
                break;
            case 7:
                $this->setGpxCmt($value);
                break;
            case 8:
                $this->setGpxSat($value);
                break;
            case 9:
                $this->setGpxFix($value);
                break;
            case 10:
                $this->setGpxTime($value);
                break;
            case 11:
                $this->setType($value);
                break;
            case 12:
                $this->setDetails($value);
                break;
            case 13:
                $this->setAddedByUserId($value);
                break;
            case 14:
                $this->setAddTime($value);
                break;
            case 15:
                $this->setIdPointType($value);
                break;
            case 16:
                $this->setSpatialGeometry($value);
                break;
            case 17:
                $this->setUpdateTime($value);
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
        $keys = PointsPeer::getFieldNames($keyType);

        if (array_key_exists($keys[0], $arr)) $this->setId($arr[$keys[0]]);
        if (array_key_exists($keys[1], $arr)) $this->setLat($arr[$keys[1]]);
        if (array_key_exists($keys[2], $arr)) $this->setLong($arr[$keys[2]]);
        if (array_key_exists($keys[3], $arr)) $this->setElevation($arr[$keys[3]]);
        if (array_key_exists($keys[4], $arr)) $this->setGpxName($arr[$keys[4]]);
        if (array_key_exists($keys[5], $arr)) $this->setGpxSym($arr[$keys[5]]);
        if (array_key_exists($keys[6], $arr)) $this->setGpxType($arr[$keys[6]]);
        if (array_key_exists($keys[7], $arr)) $this->setGpxCmt($arr[$keys[7]]);
        if (array_key_exists($keys[8], $arr)) $this->setGpxSat($arr[$keys[8]]);
        if (array_key_exists($keys[9], $arr)) $this->setGpxFix($arr[$keys[9]]);
        if (array_key_exists($keys[10], $arr)) $this->setGpxTime($arr[$keys[10]]);
        if (array_key_exists($keys[11], $arr)) $this->setType($arr[$keys[11]]);
        if (array_key_exists($keys[12], $arr)) $this->setDetails($arr[$keys[12]]);
        if (array_key_exists($keys[13], $arr)) $this->setAddedByUserId($arr[$keys[13]]);
        if (array_key_exists($keys[14], $arr)) $this->setAddTime($arr[$keys[14]]);
        if (array_key_exists($keys[15], $arr)) $this->setIdPointType($arr[$keys[15]]);
        if (array_key_exists($keys[16], $arr)) $this->setSpatialGeometry($arr[$keys[16]]);
        if (array_key_exists($keys[17], $arr)) $this->setUpdateTime($arr[$keys[17]]);
    }

    /**
     * Build a Criteria object containing the values of all modified columns in this object.
     *
     * @return Criteria The Criteria object containing all modified values.
     */
    public function buildCriteria()
    {
        $criteria = new Criteria(PointsPeer::DATABASE_NAME);

        if ($this->isColumnModified(PointsPeer::ID)) $criteria->add(PointsPeer::ID, $this->id);
        if ($this->isColumnModified(PointsPeer::LAT)) $criteria->add(PointsPeer::LAT, $this->lat);
        if ($this->isColumnModified(PointsPeer::LONG)) $criteria->add(PointsPeer::LONG, $this->long);
        if ($this->isColumnModified(PointsPeer::ELEVATION)) $criteria->add(PointsPeer::ELEVATION, $this->elevation);
        if ($this->isColumnModified(PointsPeer::GPX_NAME)) $criteria->add(PointsPeer::GPX_NAME, $this->gpx_name);
        if ($this->isColumnModified(PointsPeer::GPX_SYM)) $criteria->add(PointsPeer::GPX_SYM, $this->gpx_sym);
        if ($this->isColumnModified(PointsPeer::GPX_TYPE)) $criteria->add(PointsPeer::GPX_TYPE, $this->gpx_type);
        if ($this->isColumnModified(PointsPeer::GPX_CMT)) $criteria->add(PointsPeer::GPX_CMT, $this->gpx_cmt);
        if ($this->isColumnModified(PointsPeer::GPX_SAT)) $criteria->add(PointsPeer::GPX_SAT, $this->gpx_sat);
        if ($this->isColumnModified(PointsPeer::GPX_FIX)) $criteria->add(PointsPeer::GPX_FIX, $this->gpx_fix);
        if ($this->isColumnModified(PointsPeer::GPX_TIME)) $criteria->add(PointsPeer::GPX_TIME, $this->gpx_time);
        if ($this->isColumnModified(PointsPeer::_TYPE)) $criteria->add(PointsPeer::_TYPE, $this->_type);
        if ($this->isColumnModified(PointsPeer::_DETAILS)) $criteria->add(PointsPeer::_DETAILS, $this->_details);
        if ($this->isColumnModified(PointsPeer::ADDED_BY_USER_ID)) $criteria->add(PointsPeer::ADDED_BY_USER_ID, $this->added_by_user_id);
        if ($this->isColumnModified(PointsPeer::ADD_TIME)) $criteria->add(PointsPeer::ADD_TIME, $this->add_time);
        if ($this->isColumnModified(PointsPeer::_ID_POINT_TYPE)) $criteria->add(PointsPeer::_ID_POINT_TYPE, $this->_id_point_type);
        if ($this->isColumnModified(PointsPeer::SPATIAL_GEOMETRY)) $criteria->add(PointsPeer::SPATIAL_GEOMETRY, $this->spatial_geometry);
        if ($this->isColumnModified(PointsPeer::UPDATE_TIME)) $criteria->add(PointsPeer::UPDATE_TIME, $this->update_time);

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
        $criteria = new Criteria(PointsPeer::DATABASE_NAME);
        $criteria->add(PointsPeer::ID, $this->id);

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
     * @param object $copyObj An object of Points (or compatible) type.
     * @param boolean $deepCopy Whether to also copy all rows that refer (by fkey) to the current row.
     * @param boolean $makeNew Whether to reset autoincrement PKs and make the object new.
     * @throws PropelException
     */
    public function copyInto($copyObj, $deepCopy = false, $makeNew = true)
    {
        $copyObj->setLat($this->getLat());
        $copyObj->setLong($this->getLong());
        $copyObj->setElevation($this->getElevation());
        $copyObj->setGpxName($this->getGpxName());
        $copyObj->setGpxSym($this->getGpxSym());
        $copyObj->setGpxType($this->getGpxType());
        $copyObj->setGpxCmt($this->getGpxCmt());
        $copyObj->setGpxSat($this->getGpxSat());
        $copyObj->setGpxFix($this->getGpxFix());
        $copyObj->setGpxTime($this->getGpxTime());
        $copyObj->setType($this->getType());
        $copyObj->setDetails($this->getDetails());
        $copyObj->setAddedByUserId($this->getAddedByUserId());
        $copyObj->setAddTime($this->getAddTime());
        $copyObj->setIdPointType($this->getIdPointType());
        $copyObj->setSpatialGeometry($this->getSpatialGeometry());
        $copyObj->setUpdateTime($this->getUpdateTime());
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
     * @return Points Clone of current object.
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
     * @return PointsPeer
     */
    public function getPeer()
    {
        if (self::$peer === null) {
            self::$peer = new PointsPeer();
        }

        return self::$peer;
    }

    /**
     * Clears the current object and sets all attributes to their default values
     */
    public function clear()
    {
        $this->id = null;
        $this->lat = null;
        $this->long = null;
        $this->elevation = null;
        $this->gpx_name = null;
        $this->gpx_sym = null;
        $this->gpx_type = null;
        $this->gpx_cmt = null;
        $this->gpx_sat = null;
        $this->gpx_fix = null;
        $this->gpx_time = null;
        $this->_type = null;
        $this->_details = null;
        $this->added_by_user_id = null;
        $this->add_time = null;
        $this->_id_point_type = null;
        $this->spatial_geometry = null;
        $this->update_time = null;
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
        return (string) $this->exportTo(PointsPeer::DEFAULT_STRING_FORMAT);
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
