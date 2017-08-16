<?php


/**
 * Base class that represents a query for the 'trip_logs_to_team_members' table.
 *
 *
 *
 * @method TripLogsToTeamMembersQuery orderById($order = Criteria::ASC) Order by the id column
 * @method TripLogsToTeamMembersQuery orderByIdTeamMember($order = Criteria::ASC) Order by the id_team_member column
 * @method TripLogsToTeamMembersQuery orderByIdTripLog($order = Criteria::ASC) Order by the id_trip_log column
 *
 * @method TripLogsToTeamMembersQuery groupById() Group by the id column
 * @method TripLogsToTeamMembersQuery groupByIdTeamMember() Group by the id_team_member column
 * @method TripLogsToTeamMembersQuery groupByIdTripLog() Group by the id_trip_log column
 *
 * @method TripLogsToTeamMembersQuery leftJoin($relation) Adds a LEFT JOIN clause to the query
 * @method TripLogsToTeamMembersQuery rightJoin($relation) Adds a RIGHT JOIN clause to the query
 * @method TripLogsToTeamMembersQuery innerJoin($relation) Adds a INNER JOIN clause to the query
 *
 * @method TripLogsToTeamMembers findOne(PropelPDO $con = null) Return the first TripLogsToTeamMembers matching the query
 * @method TripLogsToTeamMembers findOneOrCreate(PropelPDO $con = null) Return the first TripLogsToTeamMembers matching the query, or a new TripLogsToTeamMembers object populated from the query conditions when no match is found
 *
 * @method TripLogsToTeamMembers findOneByIdTeamMember(string $id_team_member) Return the first TripLogsToTeamMembers filtered by the id_team_member column
 * @method TripLogsToTeamMembers findOneByIdTripLog(string $id_trip_log) Return the first TripLogsToTeamMembers filtered by the id_trip_log column
 *
 * @method array findById(string $id) Return TripLogsToTeamMembers objects filtered by the id column
 * @method array findByIdTeamMember(string $id_team_member) Return TripLogsToTeamMembers objects filtered by the id_team_member column
 * @method array findByIdTripLog(string $id_trip_log) Return TripLogsToTeamMembers objects filtered by the id_trip_log column
 *
 * @package    propel.generator.speogis.om
 */
abstract class BaseTripLogsToTeamMembersQuery extends ModelCriteria
{
    /**
     * Initializes internal state of BaseTripLogsToTeamMembersQuery object.
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
            $modelName = 'TripLogsToTeamMembers';
        }
        parent::__construct($dbName, $modelName, $modelAlias);
    }

    /**
     * Returns a new TripLogsToTeamMembersQuery object.
     *
     * @param     string $modelAlias The alias of a model in the query
     * @param   TripLogsToTeamMembersQuery|Criteria $criteria Optional Criteria to build the query from
     *
     * @return TripLogsToTeamMembersQuery
     */
    public static function create($modelAlias = null, $criteria = null)
    {
        if ($criteria instanceof TripLogsToTeamMembersQuery) {
            return $criteria;
        }
        $query = new TripLogsToTeamMembersQuery(null, null, $modelAlias);

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
     * @return   TripLogsToTeamMembers|TripLogsToTeamMembers[]|mixed the result, formatted by the current formatter
     */
    public function findPk($key, $con = null)
    {
        if ($key === null) {
            return null;
        }
        if ((null !== ($obj = TripLogsToTeamMembersPeer::getInstanceFromPool((string) $key))) && !$this->formatter) {
            // the object is already in the instance pool
            return $obj;
        }
        if ($con === null) {
            $con = Propel::getConnection(TripLogsToTeamMembersPeer::DATABASE_NAME, Propel::CONNECTION_READ);
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
     * @return                 TripLogsToTeamMembers A model object, or null if the key is not found
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
     * @return                 TripLogsToTeamMembers A model object, or null if the key is not found
     * @throws PropelException
     */
    protected function findPkSimple($key, $con)
    {
        $sql = 'SELECT `id`, `id_team_member`, `id_trip_log` FROM `trip_logs_to_team_members` WHERE `id` = :p0';
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
            $obj = new TripLogsToTeamMembers();
            $obj->hydrate($row);
            TripLogsToTeamMembersPeer::addInstanceToPool($obj, (string) $key);
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
     * @return TripLogsToTeamMembers|TripLogsToTeamMembers[]|mixed the result, formatted by the current formatter
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
     * @return PropelObjectCollection|TripLogsToTeamMembers[]|mixed the list of results, formatted by the current formatter
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
     * @return TripLogsToTeamMembersQuery The current query, for fluid interface
     */
    public function filterByPrimaryKey($key)
    {

        return $this->addUsingAlias(TripLogsToTeamMembersPeer::ID, $key, Criteria::EQUAL);
    }

    /**
     * Filter the query by a list of primary keys
     *
     * @param     array $keys The list of primary key to use for the query
     *
     * @return TripLogsToTeamMembersQuery The current query, for fluid interface
     */
    public function filterByPrimaryKeys($keys)
    {

        return $this->addUsingAlias(TripLogsToTeamMembersPeer::ID, $keys, Criteria::IN);
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
     * @return TripLogsToTeamMembersQuery The current query, for fluid interface
     */
    public function filterById($id = null, $comparison = null)
    {
        if (is_array($id)) {
            $useMinMax = false;
            if (isset($id['min'])) {
                $this->addUsingAlias(TripLogsToTeamMembersPeer::ID, $id['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($id['max'])) {
                $this->addUsingAlias(TripLogsToTeamMembersPeer::ID, $id['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(TripLogsToTeamMembersPeer::ID, $id, $comparison);
    }

    /**
     * Filter the query on the id_team_member column
     *
     * Example usage:
     * <code>
     * $query->filterByIdTeamMember(1234); // WHERE id_team_member = 1234
     * $query->filterByIdTeamMember(array(12, 34)); // WHERE id_team_member IN (12, 34)
     * $query->filterByIdTeamMember(array('min' => 12)); // WHERE id_team_member >= 12
     * $query->filterByIdTeamMember(array('max' => 12)); // WHERE id_team_member <= 12
     * </code>
     *
     * @param     mixed $idTeamMember The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TripLogsToTeamMembersQuery The current query, for fluid interface
     */
    public function filterByIdTeamMember($idTeamMember = null, $comparison = null)
    {
        if (is_array($idTeamMember)) {
            $useMinMax = false;
            if (isset($idTeamMember['min'])) {
                $this->addUsingAlias(TripLogsToTeamMembersPeer::ID_TEAM_MEMBER, $idTeamMember['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($idTeamMember['max'])) {
                $this->addUsingAlias(TripLogsToTeamMembersPeer::ID_TEAM_MEMBER, $idTeamMember['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(TripLogsToTeamMembersPeer::ID_TEAM_MEMBER, $idTeamMember, $comparison);
    }

    /**
     * Filter the query on the id_trip_log column
     *
     * Example usage:
     * <code>
     * $query->filterByIdTripLog(1234); // WHERE id_trip_log = 1234
     * $query->filterByIdTripLog(array(12, 34)); // WHERE id_trip_log IN (12, 34)
     * $query->filterByIdTripLog(array('min' => 12)); // WHERE id_trip_log >= 12
     * $query->filterByIdTripLog(array('max' => 12)); // WHERE id_trip_log <= 12
     * </code>
     *
     * @param     mixed $idTripLog The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TripLogsToTeamMembersQuery The current query, for fluid interface
     */
    public function filterByIdTripLog($idTripLog = null, $comparison = null)
    {
        if (is_array($idTripLog)) {
            $useMinMax = false;
            if (isset($idTripLog['min'])) {
                $this->addUsingAlias(TripLogsToTeamMembersPeer::ID_TRIP_LOG, $idTripLog['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($idTripLog['max'])) {
                $this->addUsingAlias(TripLogsToTeamMembersPeer::ID_TRIP_LOG, $idTripLog['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(TripLogsToTeamMembersPeer::ID_TRIP_LOG, $idTripLog, $comparison);
    }

    /**
     * Exclude object from result
     *
     * @param   TripLogsToTeamMembers $tripLogsToTeamMembers Object to remove from the list of results
     *
     * @return TripLogsToTeamMembersQuery The current query, for fluid interface
     */
    public function prune($tripLogsToTeamMembers = null)
    {
        if ($tripLogsToTeamMembers) {
            $this->addUsingAlias(TripLogsToTeamMembersPeer::ID, $tripLogsToTeamMembers->getId(), Criteria::NOT_EQUAL);
        }

        return $this;
    }

}
