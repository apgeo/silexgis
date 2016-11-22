<?php


/**
 * Base class that represents a query for the 'team_members' table.
 *
 *
 *
 * @method TeamMembersQuery orderById($order = Criteria::ASC) Order by the id column
 * @method TeamMembersQuery orderByFirstName($order = Criteria::ASC) Order by the first_name column
 * @method TeamMembersQuery orderByLastName($order = Criteria::ASC) Order by the last_name column
 * @method TeamMembersQuery orderByNickname($order = Criteria::ASC) Order by the nickname column
 * @method TeamMembersQuery orderByGroupId($order = Criteria::ASC) Order by the group_id column
 * @method TeamMembersQuery orderByPictureFileName($order = Criteria::ASC) Order by the picture_file_name column
 * @method TeamMembersQuery orderByAddTime($order = Criteria::ASC) Order by the add_time column
 * @method TeamMembersQuery orderByDescription($order = Criteria::ASC) Order by the description column
 * @method TeamMembersQuery orderByEmail($order = Criteria::ASC) Order by the email column
 * @method TeamMembersQuery orderByPhoneNumber($order = Criteria::ASC) Order by the phone_number column
 * @method TeamMembersQuery orderByNotes($order = Criteria::ASC) Order by the notes column
 * @method TeamMembersQuery orderByConnectedUserId($order = Criteria::ASC) Order by the connected_user_id column
 *
 * @method TeamMembersQuery groupById() Group by the id column
 * @method TeamMembersQuery groupByFirstName() Group by the first_name column
 * @method TeamMembersQuery groupByLastName() Group by the last_name column
 * @method TeamMembersQuery groupByNickname() Group by the nickname column
 * @method TeamMembersQuery groupByGroupId() Group by the group_id column
 * @method TeamMembersQuery groupByPictureFileName() Group by the picture_file_name column
 * @method TeamMembersQuery groupByAddTime() Group by the add_time column
 * @method TeamMembersQuery groupByDescription() Group by the description column
 * @method TeamMembersQuery groupByEmail() Group by the email column
 * @method TeamMembersQuery groupByPhoneNumber() Group by the phone_number column
 * @method TeamMembersQuery groupByNotes() Group by the notes column
 * @method TeamMembersQuery groupByConnectedUserId() Group by the connected_user_id column
 *
 * @method TeamMembersQuery leftJoin($relation) Adds a LEFT JOIN clause to the query
 * @method TeamMembersQuery rightJoin($relation) Adds a RIGHT JOIN clause to the query
 * @method TeamMembersQuery innerJoin($relation) Adds a INNER JOIN clause to the query
 *
 * @method TeamMembers findOne(PropelPDO $con = null) Return the first TeamMembers matching the query
 * @method TeamMembers findOneOrCreate(PropelPDO $con = null) Return the first TeamMembers matching the query, or a new TeamMembers object populated from the query conditions when no match is found
 *
 * @method TeamMembers findOneByFirstName(string $first_name) Return the first TeamMembers filtered by the first_name column
 * @method TeamMembers findOneByLastName(string $last_name) Return the first TeamMembers filtered by the last_name column
 * @method TeamMembers findOneByNickname(string $nickname) Return the first TeamMembers filtered by the nickname column
 * @method TeamMembers findOneByGroupId(string $group_id) Return the first TeamMembers filtered by the group_id column
 * @method TeamMembers findOneByPictureFileName(string $picture_file_name) Return the first TeamMembers filtered by the picture_file_name column
 * @method TeamMembers findOneByAddTime(string $add_time) Return the first TeamMembers filtered by the add_time column
 * @method TeamMembers findOneByDescription(string $description) Return the first TeamMembers filtered by the description column
 * @method TeamMembers findOneByEmail(string $email) Return the first TeamMembers filtered by the email column
 * @method TeamMembers findOneByPhoneNumber(string $phone_number) Return the first TeamMembers filtered by the phone_number column
 * @method TeamMembers findOneByNotes(string $notes) Return the first TeamMembers filtered by the notes column
 * @method TeamMembers findOneByConnectedUserId(string $connected_user_id) Return the first TeamMembers filtered by the connected_user_id column
 *
 * @method array findById(string $id) Return TeamMembers objects filtered by the id column
 * @method array findByFirstName(string $first_name) Return TeamMembers objects filtered by the first_name column
 * @method array findByLastName(string $last_name) Return TeamMembers objects filtered by the last_name column
 * @method array findByNickname(string $nickname) Return TeamMembers objects filtered by the nickname column
 * @method array findByGroupId(string $group_id) Return TeamMembers objects filtered by the group_id column
 * @method array findByPictureFileName(string $picture_file_name) Return TeamMembers objects filtered by the picture_file_name column
 * @method array findByAddTime(string $add_time) Return TeamMembers objects filtered by the add_time column
 * @method array findByDescription(string $description) Return TeamMembers objects filtered by the description column
 * @method array findByEmail(string $email) Return TeamMembers objects filtered by the email column
 * @method array findByPhoneNumber(string $phone_number) Return TeamMembers objects filtered by the phone_number column
 * @method array findByNotes(string $notes) Return TeamMembers objects filtered by the notes column
 * @method array findByConnectedUserId(string $connected_user_id) Return TeamMembers objects filtered by the connected_user_id column
 *
 * @package    propel.generator.speogis.om
 */
abstract class BaseTeamMembersQuery extends ModelCriteria
{
    /**
     * Initializes internal state of BaseTeamMembersQuery object.
     *
     * @param     string $dbName The dabase name
     * @param     string $modelName The phpName of a model, e.g. 'Book'
     * @param     string $modelAlias The alias for the model in this query, e.g. 'b'
     */
    public function __construct($dbName = null, $modelName = null, $modelAlias = null)
    {
        if (null === $dbName) {
            $dbName = 'speogis';
        }
        if (null === $modelName) {
            $modelName = 'TeamMembers';
        }
        parent::__construct($dbName, $modelName, $modelAlias);
    }

    /**
     * Returns a new TeamMembersQuery object.
     *
     * @param     string $modelAlias The alias of a model in the query
     * @param   TeamMembersQuery|Criteria $criteria Optional Criteria to build the query from
     *
     * @return TeamMembersQuery
     */
    public static function create($modelAlias = null, $criteria = null)
    {
        if ($criteria instanceof TeamMembersQuery) {
            return $criteria;
        }
        $query = new TeamMembersQuery(null, null, $modelAlias);

        if ($criteria instanceof Criteria) {
            $query->mergeWith($criteria);
        }

        return $query;
    }

    /**
     * Find object by primary key.
     * Propel uses the instance pool to skip the database if the object exists.
     * Go fast if the query is untouched.
     *
     * <code>
     * $obj  = $c->findPk(12, $con);
     * </code>
     *
     * @param mixed $key Primary key to use for the query
     * @param     PropelPDO $con an optional connection object
     *
     * @return   TeamMembers|TeamMembers[]|mixed the result, formatted by the current formatter
     */
    public function findPk($key, $con = null)
    {
        if ($key === null) {
            return null;
        }
        if ((null !== ($obj = TeamMembersPeer::getInstanceFromPool((string) $key))) && !$this->formatter) {
            // the object is already in the instance pool
            return $obj;
        }
        if ($con === null) {
            $con = Propel::getConnection(TeamMembersPeer::DATABASE_NAME, Propel::CONNECTION_READ);
        }
        $this->basePreSelect($con);
        if ($this->formatter || $this->modelAlias || $this->with || $this->select
         || $this->selectColumns || $this->asColumns || $this->selectModifiers
         || $this->map || $this->having || $this->joins) {
            return $this->findPkComplex($key, $con);
        } else {
            return $this->findPkSimple($key, $con);
        }
    }

    /**
     * Alias of findPk to use instance pooling
     *
     * @param     mixed $key Primary key to use for the query
     * @param     PropelPDO $con A connection object
     *
     * @return                 TeamMembers A model object, or null if the key is not found
     * @throws PropelException
     */
     public function findOneById($key, $con = null)
     {
        return $this->findPk($key, $con);
     }

    /**
     * Find object by primary key using raw SQL to go fast.
     * Bypass doSelect() and the object formatter by using generated code.
     *
     * @param     mixed $key Primary key to use for the query
     * @param     PropelPDO $con A connection object
     *
     * @return                 TeamMembers A model object, or null if the key is not found
     * @throws PropelException
     */
    protected function findPkSimple($key, $con)
    {
        $sql = 'SELECT `id`, `first_name`, `last_name`, `nickname`, `group_id`, `picture_file_name`, `add_time`, `description`, `email`, `phone_number`, `notes`, `connected_user_id` FROM `team_members` WHERE `id` = :p0';
        try {
            $stmt = $con->prepare($sql);
            $stmt->bindValue(':p0', $key, PDO::PARAM_STR);
            $stmt->execute();
        } catch (Exception $e) {
            Propel::log($e->getMessage(), Propel::LOG_ERR);
            throw new PropelException(sprintf('Unable to execute SELECT statement [%s]', $sql), $e);
        }
        $obj = null;
        if ($row = $stmt->fetch(PDO::FETCH_NUM)) {
            $obj = new TeamMembers();
            $obj->hydrate($row);
            TeamMembersPeer::addInstanceToPool($obj, (string) $key);
        }
        $stmt->closeCursor();

        return $obj;
    }

    /**
     * Find object by primary key.
     *
     * @param     mixed $key Primary key to use for the query
     * @param     PropelPDO $con A connection object
     *
     * @return TeamMembers|TeamMembers[]|mixed the result, formatted by the current formatter
     */
    protected function findPkComplex($key, $con)
    {
        // As the query uses a PK condition, no limit(1) is necessary.
        $criteria = $this->isKeepQuery() ? clone $this : $this;
        $stmt = $criteria
            ->filterByPrimaryKey($key)
            ->doSelect($con);

        return $criteria->getFormatter()->init($criteria)->formatOne($stmt);
    }

    /**
     * Find objects by primary key
     * <code>
     * $objs = $c->findPks(array(12, 56, 832), $con);
     * </code>
     * @param     array $keys Primary keys to use for the query
     * @param     PropelPDO $con an optional connection object
     *
     * @return PropelObjectCollection|TeamMembers[]|mixed the list of results, formatted by the current formatter
     */
    public function findPks($keys, $con = null)
    {
        if ($con === null) {
            $con = Propel::getConnection($this->getDbName(), Propel::CONNECTION_READ);
        }
        $this->basePreSelect($con);
        $criteria = $this->isKeepQuery() ? clone $this : $this;
        $stmt = $criteria
            ->filterByPrimaryKeys($keys)
            ->doSelect($con);

        return $criteria->getFormatter()->init($criteria)->format($stmt);
    }

    /**
     * Filter the query by primary key
     *
     * @param     mixed $key Primary key to use for the query
     *
     * @return TeamMembersQuery The current query, for fluid interface
     */
    public function filterByPrimaryKey($key)
    {

        return $this->addUsingAlias(TeamMembersPeer::ID, $key, Criteria::EQUAL);
    }

    /**
     * Filter the query by a list of primary keys
     *
     * @param     array $keys The list of primary key to use for the query
     *
     * @return TeamMembersQuery The current query, for fluid interface
     */
    public function filterByPrimaryKeys($keys)
    {

        return $this->addUsingAlias(TeamMembersPeer::ID, $keys, Criteria::IN);
    }

    /**
     * Filter the query on the id column
     *
     * Example usage:
     * <code>
     * $query->filterById(1234); // WHERE id = 1234
     * $query->filterById(array(12, 34)); // WHERE id IN (12, 34)
     * $query->filterById(array('min' => 12)); // WHERE id >= 12
     * $query->filterById(array('max' => 12)); // WHERE id <= 12
     * </code>
     *
     * @param     mixed $id The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TeamMembersQuery The current query, for fluid interface
     */
    public function filterById($id = null, $comparison = null)
    {
        if (is_array($id)) {
            $useMinMax = false;
            if (isset($id['min'])) {
                $this->addUsingAlias(TeamMembersPeer::ID, $id['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($id['max'])) {
                $this->addUsingAlias(TeamMembersPeer::ID, $id['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(TeamMembersPeer::ID, $id, $comparison);
    }

    /**
     * Filter the query on the first_name column
     *
     * Example usage:
     * <code>
     * $query->filterByFirstName('fooValue');   // WHERE first_name = 'fooValue'
     * $query->filterByFirstName('%fooValue%'); // WHERE first_name LIKE '%fooValue%'
     * </code>
     *
     * @param     string $firstName The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TeamMembersQuery The current query, for fluid interface
     */
    public function filterByFirstName($firstName = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($firstName)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $firstName)) {
                $firstName = str_replace('*', '%', $firstName);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(TeamMembersPeer::FIRST_NAME, $firstName, $comparison);
    }

    /**
     * Filter the query on the last_name column
     *
     * Example usage:
     * <code>
     * $query->filterByLastName('fooValue');   // WHERE last_name = 'fooValue'
     * $query->filterByLastName('%fooValue%'); // WHERE last_name LIKE '%fooValue%'
     * </code>
     *
     * @param     string $lastName The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TeamMembersQuery The current query, for fluid interface
     */
    public function filterByLastName($lastName = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($lastName)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $lastName)) {
                $lastName = str_replace('*', '%', $lastName);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(TeamMembersPeer::LAST_NAME, $lastName, $comparison);
    }

    /**
     * Filter the query on the nickname column
     *
     * Example usage:
     * <code>
     * $query->filterByNickname('fooValue');   // WHERE nickname = 'fooValue'
     * $query->filterByNickname('%fooValue%'); // WHERE nickname LIKE '%fooValue%'
     * </code>
     *
     * @param     string $nickname The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TeamMembersQuery The current query, for fluid interface
     */
    public function filterByNickname($nickname = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($nickname)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $nickname)) {
                $nickname = str_replace('*', '%', $nickname);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(TeamMembersPeer::NICKNAME, $nickname, $comparison);
    }

    /**
     * Filter the query on the group_id column
     *
     * Example usage:
     * <code>
     * $query->filterByGroupId(1234); // WHERE group_id = 1234
     * $query->filterByGroupId(array(12, 34)); // WHERE group_id IN (12, 34)
     * $query->filterByGroupId(array('min' => 12)); // WHERE group_id >= 12
     * $query->filterByGroupId(array('max' => 12)); // WHERE group_id <= 12
     * </code>
     *
     * @param     mixed $groupId The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TeamMembersQuery The current query, for fluid interface
     */
    public function filterByGroupId($groupId = null, $comparison = null)
    {
        if (is_array($groupId)) {
            $useMinMax = false;
            if (isset($groupId['min'])) {
                $this->addUsingAlias(TeamMembersPeer::GROUP_ID, $groupId['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($groupId['max'])) {
                $this->addUsingAlias(TeamMembersPeer::GROUP_ID, $groupId['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(TeamMembersPeer::GROUP_ID, $groupId, $comparison);
    }

    /**
     * Filter the query on the picture_file_name column
     *
     * Example usage:
     * <code>
     * $query->filterByPictureFileName('fooValue');   // WHERE picture_file_name = 'fooValue'
     * $query->filterByPictureFileName('%fooValue%'); // WHERE picture_file_name LIKE '%fooValue%'
     * </code>
     *
     * @param     string $pictureFileName The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TeamMembersQuery The current query, for fluid interface
     */
    public function filterByPictureFileName($pictureFileName = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($pictureFileName)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $pictureFileName)) {
                $pictureFileName = str_replace('*', '%', $pictureFileName);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(TeamMembersPeer::PICTURE_FILE_NAME, $pictureFileName, $comparison);
    }

    /**
     * Filter the query on the add_time column
     *
     * Example usage:
     * <code>
     * $query->filterByAddTime('2011-03-14'); // WHERE add_time = '2011-03-14'
     * $query->filterByAddTime('now'); // WHERE add_time = '2011-03-14'
     * $query->filterByAddTime(array('max' => 'yesterday')); // WHERE add_time < '2011-03-13'
     * </code>
     *
     * @param     mixed $addTime The value to use as filter.
     *              Values can be integers (unix timestamps), DateTime objects, or strings.
     *              Empty strings are treated as NULL.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TeamMembersQuery The current query, for fluid interface
     */
    public function filterByAddTime($addTime = null, $comparison = null)
    {
        if (is_array($addTime)) {
            $useMinMax = false;
            if (isset($addTime['min'])) {
                $this->addUsingAlias(TeamMembersPeer::ADD_TIME, $addTime['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($addTime['max'])) {
                $this->addUsingAlias(TeamMembersPeer::ADD_TIME, $addTime['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(TeamMembersPeer::ADD_TIME, $addTime, $comparison);
    }

    /**
     * Filter the query on the description column
     *
     * Example usage:
     * <code>
     * $query->filterByDescription('fooValue');   // WHERE description = 'fooValue'
     * $query->filterByDescription('%fooValue%'); // WHERE description LIKE '%fooValue%'
     * </code>
     *
     * @param     string $description The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TeamMembersQuery The current query, for fluid interface
     */
    public function filterByDescription($description = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($description)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $description)) {
                $description = str_replace('*', '%', $description);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(TeamMembersPeer::DESCRIPTION, $description, $comparison);
    }

    /**
     * Filter the query on the email column
     *
     * Example usage:
     * <code>
     * $query->filterByEmail('fooValue');   // WHERE email = 'fooValue'
     * $query->filterByEmail('%fooValue%'); // WHERE email LIKE '%fooValue%'
     * </code>
     *
     * @param     string $email The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TeamMembersQuery The current query, for fluid interface
     */
    public function filterByEmail($email = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($email)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $email)) {
                $email = str_replace('*', '%', $email);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(TeamMembersPeer::EMAIL, $email, $comparison);
    }

    /**
     * Filter the query on the phone_number column
     *
     * Example usage:
     * <code>
     * $query->filterByPhoneNumber('fooValue');   // WHERE phone_number = 'fooValue'
     * $query->filterByPhoneNumber('%fooValue%'); // WHERE phone_number LIKE '%fooValue%'
     * </code>
     *
     * @param     string $phoneNumber The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TeamMembersQuery The current query, for fluid interface
     */
    public function filterByPhoneNumber($phoneNumber = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($phoneNumber)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $phoneNumber)) {
                $phoneNumber = str_replace('*', '%', $phoneNumber);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(TeamMembersPeer::PHONE_NUMBER, $phoneNumber, $comparison);
    }

    /**
     * Filter the query on the notes column
     *
     * Example usage:
     * <code>
     * $query->filterByNotes('fooValue');   // WHERE notes = 'fooValue'
     * $query->filterByNotes('%fooValue%'); // WHERE notes LIKE '%fooValue%'
     * </code>
     *
     * @param     string $notes The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TeamMembersQuery The current query, for fluid interface
     */
    public function filterByNotes($notes = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($notes)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $notes)) {
                $notes = str_replace('*', '%', $notes);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(TeamMembersPeer::NOTES, $notes, $comparison);
    }

    /**
     * Filter the query on the connected_user_id column
     *
     * Example usage:
     * <code>
     * $query->filterByConnectedUserId(1234); // WHERE connected_user_id = 1234
     * $query->filterByConnectedUserId(array(12, 34)); // WHERE connected_user_id IN (12, 34)
     * $query->filterByConnectedUserId(array('min' => 12)); // WHERE connected_user_id >= 12
     * $query->filterByConnectedUserId(array('max' => 12)); // WHERE connected_user_id <= 12
     * </code>
     *
     * @param     mixed $connectedUserId The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TeamMembersQuery The current query, for fluid interface
     */
    public function filterByConnectedUserId($connectedUserId = null, $comparison = null)
    {
        if (is_array($connectedUserId)) {
            $useMinMax = false;
            if (isset($connectedUserId['min'])) {
                $this->addUsingAlias(TeamMembersPeer::CONNECTED_USER_ID, $connectedUserId['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($connectedUserId['max'])) {
                $this->addUsingAlias(TeamMembersPeer::CONNECTED_USER_ID, $connectedUserId['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(TeamMembersPeer::CONNECTED_USER_ID, $connectedUserId, $comparison);
    }

    /**
     * Exclude object from result
     *
     * @param   TeamMembers $teamMembers Object to remove from the list of results
     *
     * @return TeamMembersQuery The current query, for fluid interface
     */
    public function prune($teamMembers = null)
    {
        if ($teamMembers) {
            $this->addUsingAlias(TeamMembersPeer::ID, $teamMembers->getId(), Criteria::NOT_EQUAL);
        }

        return $this;
    }

}
