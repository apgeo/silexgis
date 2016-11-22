<?php


/**
 * Base class that represents a row from the 'team_members' table.
 *
 *
 *
 * @package    propel.generator.speogis.om
 */
abstract class BaseTeamMembers extends BaseObject implements Persistent
{
    /**
     * Peer class name
     */
    const PEER = 'TeamMembersPeer';

    /**
     * The Peer class.
     * Instance provides a convenient way of calling static methods on a class
     * that calling code may not be able to identify.
     * @var        TeamMembersPeer
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
     * The value for the first_name field.
     * @var        string
     */
    protected $first_name;

    /**
     * The value for the last_name field.
     * @var        string
     */
    protected $last_name;

    /**
     * The value for the nickname field.
     * @var        string
     */
    protected $nickname;

    /**
     * The value for the group_id field.
     * @var        string
     */
    protected $group_id;

    /**
     * The value for the picture_file_name field.
     * @var        string
     */
    protected $picture_file_name;

    /**
     * The value for the add_time field.
     * @var        string
     */
    protected $add_time;

    /**
     * The value for the description field.
     * @var        string
     */
    protected $description;

    /**
     * The value for the email field.
     * @var        string
     */
    protected $email;

    /**
     * The value for the phone_number field.
     * @var        string
     */
    protected $phone_number;

    /**
     * The value for the notes field.
     * @var        string
     */
    protected $notes;

    /**
     * The value for the connected_user_id field.
     * @var        string
     */
    protected $connected_user_id;

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
     * Get the [first_name] column value.
     *
     * @return string
     */
    public function getFirstName()
    {

        return $this->first_name;
    }

    /**
     * Get the [last_name] column value.
     *
     * @return string
     */
    public function getLastName()
    {

        return $this->last_name;
    }

    /**
     * Get the [nickname] column value.
     *
     * @return string
     */
    public function getNickname()
    {

        return $this->nickname;
    }

    /**
     * Get the [group_id] column value.
     *
     * @return string
     */
    public function getGroupId()
    {

        return $this->group_id;
    }

    /**
     * Get the [picture_file_name] column value.
     *
     * @return string
     */
    public function getPictureFileName()
    {

        return $this->picture_file_name;
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
     * Get the [description] column value.
     *
     * @return string
     */
    public function getDescription()
    {

        return $this->description;
    }

    /**
     * Get the [email] column value.
     *
     * @return string
     */
    public function getEmail()
    {

        return $this->email;
    }

    /**
     * Get the [phone_number] column value.
     *
     * @return string
     */
    public function getPhoneNumber()
    {

        return $this->phone_number;
    }

    /**
     * Get the [notes] column value.
     *
     * @return string
     */
    public function getNotes()
    {

        return $this->notes;
    }

    /**
     * Get the [connected_user_id] column value.
     *
     * @return string
     */
    public function getConnectedUserId()
    {

        return $this->connected_user_id;
    }

    /**
     * Set the value of [id] column.
     *
     * @param  string $v new value
     * @return TeamMembers The current object (for fluent API support)
     */
    public function setId($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (string) $v;
        }

        if ($this->id !== $v) {
            $this->id = $v;
            $this->modifiedColumns[] = TeamMembersPeer::ID;
        }


        return $this;
    } // setId()

    /**
     * Set the value of [first_name] column.
     *
     * @param  string $v new value
     * @return TeamMembers The current object (for fluent API support)
     */
    public function setFirstName($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->first_name !== $v) {
            $this->first_name = $v;
            $this->modifiedColumns[] = TeamMembersPeer::FIRST_NAME;
        }


        return $this;
    } // setFirstName()

    /**
     * Set the value of [last_name] column.
     *
     * @param  string $v new value
     * @return TeamMembers The current object (for fluent API support)
     */
    public function setLastName($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->last_name !== $v) {
            $this->last_name = $v;
            $this->modifiedColumns[] = TeamMembersPeer::LAST_NAME;
        }


        return $this;
    } // setLastName()

    /**
     * Set the value of [nickname] column.
     *
     * @param  string $v new value
     * @return TeamMembers The current object (for fluent API support)
     */
    public function setNickname($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->nickname !== $v) {
            $this->nickname = $v;
            $this->modifiedColumns[] = TeamMembersPeer::NICKNAME;
        }


        return $this;
    } // setNickname()

    /**
     * Set the value of [group_id] column.
     *
     * @param  string $v new value
     * @return TeamMembers The current object (for fluent API support)
     */
    public function setGroupId($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (string) $v;
        }

        if ($this->group_id !== $v) {
            $this->group_id = $v;
            $this->modifiedColumns[] = TeamMembersPeer::GROUP_ID;
        }


        return $this;
    } // setGroupId()

    /**
     * Set the value of [picture_file_name] column.
     *
     * @param  string $v new value
     * @return TeamMembers The current object (for fluent API support)
     */
    public function setPictureFileName($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->picture_file_name !== $v) {
            $this->picture_file_name = $v;
            $this->modifiedColumns[] = TeamMembersPeer::PICTURE_FILE_NAME;
        }


        return $this;
    } // setPictureFileName()

    /**
     * Sets the value of [add_time] column to a normalized version of the date/time value specified.
     *
     * @param mixed $v string, integer (timestamp), or DateTime value.
     *               Empty strings are treated as null.
     * @return TeamMembers The current object (for fluent API support)
     */
    public function setAddTime($v)
    {
        $dt = PropelDateTime::newInstance($v, null, 'DateTime');
        if ($this->add_time !== null || $dt !== null) {
            $currentDateAsString = ($this->add_time !== null && $tmpDt = new DateTime($this->add_time)) ? $tmpDt->format('Y-m-d H:i:s') : null;
            $newDateAsString = $dt ? $dt->format('Y-m-d H:i:s') : null;
            if ($currentDateAsString !== $newDateAsString) {
                $this->add_time = $newDateAsString;
                $this->modifiedColumns[] = TeamMembersPeer::ADD_TIME;
            }
        } // if either are not null


        return $this;
    } // setAddTime()

    /**
     * Set the value of [description] column.
     *
     * @param  string $v new value
     * @return TeamMembers The current object (for fluent API support)
     */
    public function setDescription($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->description !== $v) {
            $this->description = $v;
            $this->modifiedColumns[] = TeamMembersPeer::DESCRIPTION;
        }


        return $this;
    } // setDescription()

    /**
     * Set the value of [email] column.
     *
     * @param  string $v new value
     * @return TeamMembers The current object (for fluent API support)
     */
    public function setEmail($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->email !== $v) {
            $this->email = $v;
            $this->modifiedColumns[] = TeamMembersPeer::EMAIL;
        }


        return $this;
    } // setEmail()

    /**
     * Set the value of [phone_number] column.
     *
     * @param  string $v new value
     * @return TeamMembers The current object (for fluent API support)
     */
    public function setPhoneNumber($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->phone_number !== $v) {
            $this->phone_number = $v;
            $this->modifiedColumns[] = TeamMembersPeer::PHONE_NUMBER;
        }


        return $this;
    } // setPhoneNumber()

    /**
     * Set the value of [notes] column.
     *
     * @param  string $v new value
     * @return TeamMembers The current object (for fluent API support)
     */
    public function setNotes($v)
    {
        if ($v !== null) {
            $v = (string) $v;
        }

        if ($this->notes !== $v) {
            $this->notes = $v;
            $this->modifiedColumns[] = TeamMembersPeer::NOTES;
        }


        return $this;
    } // setNotes()

    /**
     * Set the value of [connected_user_id] column.
     *
     * @param  string $v new value
     * @return TeamMembers The current object (for fluent API support)
     */
    public function setConnectedUserId($v)
    {
        if ($v !== null && is_numeric($v)) {
            $v = (string) $v;
        }

        if ($this->connected_user_id !== $v) {
            $this->connected_user_id = $v;
            $this->modifiedColumns[] = TeamMembersPeer::CONNECTED_USER_ID;
        }


        return $this;
    } // setConnectedUserId()

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
            $this->first_name = ($row[$startcol + 1] !== null) ? (string) $row[$startcol + 1] : null;
            $this->last_name = ($row[$startcol + 2] !== null) ? (string) $row[$startcol + 2] : null;
            $this->nickname = ($row[$startcol + 3] !== null) ? (string) $row[$startcol + 3] : null;
            $this->group_id = ($row[$startcol + 4] !== null) ? (string) $row[$startcol + 4] : null;
            $this->picture_file_name = ($row[$startcol + 5] !== null) ? (string) $row[$startcol + 5] : null;
            $this->add_time = ($row[$startcol + 6] !== null) ? (string) $row[$startcol + 6] : null;
            $this->description = ($row[$startcol + 7] !== null) ? (string) $row[$startcol + 7] : null;
            $this->email = ($row[$startcol + 8] !== null) ? (string) $row[$startcol + 8] : null;
            $this->phone_number = ($row[$startcol + 9] !== null) ? (string) $row[$startcol + 9] : null;
            $this->notes = ($row[$startcol + 10] !== null) ? (string) $row[$startcol + 10] : null;
            $this->connected_user_id = ($row[$startcol + 11] !== null) ? (string) $row[$startcol + 11] : null;
            $this->resetModified();

            $this->setNew(false);

            if ($rehydrate) {
                $this->ensureConsistency();
            }
            $this->postHydrate($row, $startcol, $rehydrate);

            return $startcol + 12; // 12 = TeamMembersPeer::NUM_HYDRATE_COLUMNS.

        } catch (Exception $e) {
            throw new PropelException("Error populating TeamMembers object", $e);
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
            $con = Propel::getConnection(TeamMembersPeer::DATABASE_NAME, Propel::CONNECTION_READ);
        }

        // We don't need to alter the object instance pool; we're just modifying this instance
        // already in the pool.

        $stmt = TeamMembersPeer::doSelectStmt($this->buildPkeyCriteria(), $con);
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
            $con = Propel::getConnection(TeamMembersPeer::DATABASE_NAME, Propel::CONNECTION_WRITE);
        }

        $con->beginTransaction();
        try {
            $deleteQuery = TeamMembersQuery::create()
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
            $con = Propel::getConnection(TeamMembersPeer::DATABASE_NAME, Propel::CONNECTION_WRITE);
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
                TeamMembersPeer::addInstanceToPool($this);
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

        $this->modifiedColumns[] = TeamMembersPeer::ID;
        if (null !== $this->id) {
            throw new PropelException('Cannot insert a value for auto-increment primary key (' . TeamMembersPeer::ID . ')');
        }

         // check the columns in natural order for more readable SQL queries
        if ($this->isColumnModified(TeamMembersPeer::ID)) {
            $modifiedColumns[':p' . $index++]  = '`id`';
        }
        if ($this->isColumnModified(TeamMembersPeer::FIRST_NAME)) {
            $modifiedColumns[':p' . $index++]  = '`first_name`';
        }
        if ($this->isColumnModified(TeamMembersPeer::LAST_NAME)) {
            $modifiedColumns[':p' . $index++]  = '`last_name`';
        }
        if ($this->isColumnModified(TeamMembersPeer::NICKNAME)) {
            $modifiedColumns[':p' . $index++]  = '`nickname`';
        }
        if ($this->isColumnModified(TeamMembersPeer::GROUP_ID)) {
            $modifiedColumns[':p' . $index++]  = '`group_id`';
        }
        if ($this->isColumnModified(TeamMembersPeer::PICTURE_FILE_NAME)) {
            $modifiedColumns[':p' . $index++]  = '`picture_file_name`';
        }
        if ($this->isColumnModified(TeamMembersPeer::ADD_TIME)) {
            $modifiedColumns[':p' . $index++]  = '`add_time`';
        }
        if ($this->isColumnModified(TeamMembersPeer::DESCRIPTION)) {
            $modifiedColumns[':p' . $index++]  = '`description`';
        }
        if ($this->isColumnModified(TeamMembersPeer::EMAIL)) {
            $modifiedColumns[':p' . $index++]  = '`email`';
        }
        if ($this->isColumnModified(TeamMembersPeer::PHONE_NUMBER)) {
            $modifiedColumns[':p' . $index++]  = '`phone_number`';
        }
        if ($this->isColumnModified(TeamMembersPeer::NOTES)) {
            $modifiedColumns[':p' . $index++]  = '`notes`';
        }
        if ($this->isColumnModified(TeamMembersPeer::CONNECTED_USER_ID)) {
            $modifiedColumns[':p' . $index++]  = '`connected_user_id`';
        }

        $sql = sprintf(
            'INSERT INTO `team_members` (%s) VALUES (%s)',
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
                    case '`first_name`':
                        $stmt->bindValue($identifier, $this->first_name, PDO::PARAM_STR);
                        break;
                    case '`last_name`':
                        $stmt->bindValue($identifier, $this->last_name, PDO::PARAM_STR);
                        break;
                    case '`nickname`':
                        $stmt->bindValue($identifier, $this->nickname, PDO::PARAM_STR);
                        break;
                    case '`group_id`':
                        $stmt->bindValue($identifier, $this->group_id, PDO::PARAM_STR);
                        break;
                    case '`picture_file_name`':
                        $stmt->bindValue($identifier, $this->picture_file_name, PDO::PARAM_STR);
                        break;
                    case '`add_time`':
                        $stmt->bindValue($identifier, $this->add_time, PDO::PARAM_STR);
                        break;
                    case '`description`':
                        $stmt->bindValue($identifier, $this->description, PDO::PARAM_STR);
                        break;
                    case '`email`':
                        $stmt->bindValue($identifier, $this->email, PDO::PARAM_STR);
                        break;
                    case '`phone_number`':
                        $stmt->bindValue($identifier, $this->phone_number, PDO::PARAM_STR);
                        break;
                    case '`notes`':
                        $stmt->bindValue($identifier, $this->notes, PDO::PARAM_STR);
                        break;
                    case '`connected_user_id`':
                        $stmt->bindValue($identifier, $this->connected_user_id, PDO::PARAM_STR);
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


            if (($retval = TeamMembersPeer::doValidate($this, $columns)) !== true) {
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
        $pos = TeamMembersPeer::translateFieldName($name, $type, BasePeer::TYPE_NUM);
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
                return $this->getFirstName();
                break;
            case 2:
                return $this->getLastName();
                break;
            case 3:
                return $this->getNickname();
                break;
            case 4:
                return $this->getGroupId();
                break;
            case 5:
                return $this->getPictureFileName();
                break;
            case 6:
                return $this->getAddTime();
                break;
            case 7:
                return $this->getDescription();
                break;
            case 8:
                return $this->getEmail();
                break;
            case 9:
                return $this->getPhoneNumber();
                break;
            case 10:
                return $this->getNotes();
                break;
            case 11:
                return $this->getConnectedUserId();
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
        if (isset($alreadyDumpedObjects['TeamMembers'][$this->getPrimaryKey()])) {
            return '*RECURSION*';
        }
        $alreadyDumpedObjects['TeamMembers'][$this->getPrimaryKey()] = true;
        $keys = TeamMembersPeer::getFieldNames($keyType);
        $result = array(
            $keys[0] => $this->getId(),
            $keys[1] => $this->getFirstName(),
            $keys[2] => $this->getLastName(),
            $keys[3] => $this->getNickname(),
            $keys[4] => $this->getGroupId(),
            $keys[5] => $this->getPictureFileName(),
            $keys[6] => $this->getAddTime(),
            $keys[7] => $this->getDescription(),
            $keys[8] => $this->getEmail(),
            $keys[9] => $this->getPhoneNumber(),
            $keys[10] => $this->getNotes(),
            $keys[11] => $this->getConnectedUserId(),
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
        $pos = TeamMembersPeer::translateFieldName($name, $type, BasePeer::TYPE_NUM);

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
                $this->setFirstName($value);
                break;
            case 2:
                $this->setLastName($value);
                break;
            case 3:
                $this->setNickname($value);
                break;
            case 4:
                $this->setGroupId($value);
                break;
            case 5:
                $this->setPictureFileName($value);
                break;
            case 6:
                $this->setAddTime($value);
                break;
            case 7:
                $this->setDescription($value);
                break;
            case 8:
                $this->setEmail($value);
                break;
            case 9:
                $this->setPhoneNumber($value);
                break;
            case 10:
                $this->setNotes($value);
                break;
            case 11:
                $this->setConnectedUserId($value);
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
        $keys = TeamMembersPeer::getFieldNames($keyType);

        if (array_key_exists($keys[0], $arr)) $this->setId($arr[$keys[0]]);
        if (array_key_exists($keys[1], $arr)) $this->setFirstName($arr[$keys[1]]);
        if (array_key_exists($keys[2], $arr)) $this->setLastName($arr[$keys[2]]);
        if (array_key_exists($keys[3], $arr)) $this->setNickname($arr[$keys[3]]);
        if (array_key_exists($keys[4], $arr)) $this->setGroupId($arr[$keys[4]]);
        if (array_key_exists($keys[5], $arr)) $this->setPictureFileName($arr[$keys[5]]);
        if (array_key_exists($keys[6], $arr)) $this->setAddTime($arr[$keys[6]]);
        if (array_key_exists($keys[7], $arr)) $this->setDescription($arr[$keys[7]]);
        if (array_key_exists($keys[8], $arr)) $this->setEmail($arr[$keys[8]]);
        if (array_key_exists($keys[9], $arr)) $this->setPhoneNumber($arr[$keys[9]]);
        if (array_key_exists($keys[10], $arr)) $this->setNotes($arr[$keys[10]]);
        if (array_key_exists($keys[11], $arr)) $this->setConnectedUserId($arr[$keys[11]]);
    }

    /**
     * Build a Criteria object containing the values of all modified columns in this object.
     *
     * @return Criteria The Criteria object containing all modified values.
     */
    public function buildCriteria()
    {
        $criteria = new Criteria(TeamMembersPeer::DATABASE_NAME);

        if ($this->isColumnModified(TeamMembersPeer::ID)) $criteria->add(TeamMembersPeer::ID, $this->id);
        if ($this->isColumnModified(TeamMembersPeer::FIRST_NAME)) $criteria->add(TeamMembersPeer::FIRST_NAME, $this->first_name);
        if ($this->isColumnModified(TeamMembersPeer::LAST_NAME)) $criteria->add(TeamMembersPeer::LAST_NAME, $this->last_name);
        if ($this->isColumnModified(TeamMembersPeer::NICKNAME)) $criteria->add(TeamMembersPeer::NICKNAME, $this->nickname);
        if ($this->isColumnModified(TeamMembersPeer::GROUP_ID)) $criteria->add(TeamMembersPeer::GROUP_ID, $this->group_id);
        if ($this->isColumnModified(TeamMembersPeer::PICTURE_FILE_NAME)) $criteria->add(TeamMembersPeer::PICTURE_FILE_NAME, $this->picture_file_name);
        if ($this->isColumnModified(TeamMembersPeer::ADD_TIME)) $criteria->add(TeamMembersPeer::ADD_TIME, $this->add_time);
        if ($this->isColumnModified(TeamMembersPeer::DESCRIPTION)) $criteria->add(TeamMembersPeer::DESCRIPTION, $this->description);
        if ($this->isColumnModified(TeamMembersPeer::EMAIL)) $criteria->add(TeamMembersPeer::EMAIL, $this->email);
        if ($this->isColumnModified(TeamMembersPeer::PHONE_NUMBER)) $criteria->add(TeamMembersPeer::PHONE_NUMBER, $this->phone_number);
        if ($this->isColumnModified(TeamMembersPeer::NOTES)) $criteria->add(TeamMembersPeer::NOTES, $this->notes);
        if ($this->isColumnModified(TeamMembersPeer::CONNECTED_USER_ID)) $criteria->add(TeamMembersPeer::CONNECTED_USER_ID, $this->connected_user_id);

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
        $criteria = new Criteria(TeamMembersPeer::DATABASE_NAME);
        $criteria->add(TeamMembersPeer::ID, $this->id);

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
     * @param object $copyObj An object of TeamMembers (or compatible) type.
     * @param boolean $deepCopy Whether to also copy all rows that refer (by fkey) to the current row.
     * @param boolean $makeNew Whether to reset autoincrement PKs and make the object new.
     * @throws PropelException
     */
    public function copyInto($copyObj, $deepCopy = false, $makeNew = true)
    {
        $copyObj->setFirstName($this->getFirstName());
        $copyObj->setLastName($this->getLastName());
        $copyObj->setNickname($this->getNickname());
        $copyObj->setGroupId($this->getGroupId());
        $copyObj->setPictureFileName($this->getPictureFileName());
        $copyObj->setAddTime($this->getAddTime());
        $copyObj->setDescription($this->getDescription());
        $copyObj->setEmail($this->getEmail());
        $copyObj->setPhoneNumber($this->getPhoneNumber());
        $copyObj->setNotes($this->getNotes());
        $copyObj->setConnectedUserId($this->getConnectedUserId());
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
     * @return TeamMembers Clone of current object.
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
     * @return TeamMembersPeer
     */
    public function getPeer()
    {
        if (self::$peer === null) {
            self::$peer = new TeamMembersPeer();
        }

        return self::$peer;
    }

    /**
     * Clears the current object and sets all attributes to their default values
     */
    public function clear()
    {
        $this->id = null;
        $this->first_name = null;
        $this->last_name = null;
        $this->nickname = null;
        $this->group_id = null;
        $this->picture_file_name = null;
        $this->add_time = null;
        $this->description = null;
        $this->email = null;
        $this->phone_number = null;
        $this->notes = null;
        $this->connected_user_id = null;
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
        return (string) $this->exportTo(TeamMembersPeer::DEFAULT_STRING_FORMAT);
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
