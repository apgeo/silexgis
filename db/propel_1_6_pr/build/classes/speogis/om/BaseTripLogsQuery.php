<?php


/**
 * Base class that represents a query for the 'trip_logs' table.
 *
 *
 *
 * @method TripLogsQuery orderById($order = Criteria::ASC) Order by the id column
 * @method TripLogsQuery orderByAddTime($order = Criteria::ASC) Order by the add_time column
 * @method TripLogsQuery orderByTripStartTime($order = Criteria::ASC) Order by the trip_start_time column
 * @method TripLogsQuery orderByTripEndTime($order = Criteria::ASC) Order by the trip_end_time column
 * @method TripLogsQuery orderByDetails($order = Criteria::ASC) Order by the details column
 * @method TripLogsQuery orderByTargetZone($order = Criteria::ASC) Order by the target_zone column
 * @method TripLogsQuery orderByType($order = Criteria::ASC) Order by the type column
 * @method TripLogsQuery orderByTemporary($order = Criteria::ASC) Order by the temporary column
 * @method TripLogsQuery orderBySummary($order = Criteria::ASC) Order by the summary column
 *
 * @method TripLogsQuery groupById() Group by the id column
 * @method TripLogsQuery groupByAddTime() Group by the add_time column
 * @method TripLogsQuery groupByTripStartTime() Group by the trip_start_time column
 * @method TripLogsQuery groupByTripEndTime() Group by the trip_end_time column
 * @method TripLogsQuery groupByDetails() Group by the details column
 * @method TripLogsQuery groupByTargetZone() Group by the target_zone column
 * @method TripLogsQuery groupByType() Group by the type column
 * @method TripLogsQuery groupByTemporary() Group by the temporary column
 * @method TripLogsQuery groupBySummary() Group by the summary column
 *
 * @method TripLogsQuery leftJoin($relation) Adds a LEFT JOIN clause to the query
 * @method TripLogsQuery rightJoin($relation) Adds a RIGHT JOIN clause to the query
 * @method TripLogsQuery innerJoin($relation) Adds a INNER JOIN clause to the query
 *
 * @method TripLogs findOne(PropelPDO $con = null) Return the first TripLogs matching the query
 * @method TripLogs findOneOrCreate(PropelPDO $con = null) Return the first TripLogs matching the query, or a new TripLogs object populated from the query conditions when no match is found
 *
 * @method TripLogs findOneByAddTime(string $add_time) Return the first TripLogs filtered by the add_time column
 * @method TripLogs findOneByTripStartTime(string $trip_start_time) Return the first TripLogs filtered by the trip_start_time column
 * @method TripLogs findOneByTripEndTime(string $trip_end_time) Return the first TripLogs filtered by the trip_end_time column
 * @method TripLogs findOneByDetails(string $details) Return the first TripLogs filtered by the details column
 * @method TripLogs findOneByTargetZone(string $target_zone) Return the first TripLogs filtered by the target_zone column
 * @method TripLogs findOneByType(string $type) Return the first TripLogs filtered by the type column
 * @method TripLogs findOneByTemporary(boolean $temporary) Return the first TripLogs filtered by the temporary column
 * @method TripLogs findOneBySummary(string $summary) Return the first TripLogs filtered by the summary column
 *
 * @method array findById(string $id) Return TripLogs objects filtered by the id column
 * @method array findByAddTime(string $add_time) Return TripLogs objects filtered by the add_time column
 * @method array findByTripStartTime(string $trip_start_time) Return TripLogs objects filtered by the trip_start_time column
 * @method array findByTripEndTime(string $trip_end_time) Return TripLogs objects filtered by the trip_end_time column
 * @method array findByDetails(string $details) Return TripLogs objects filtered by the details column
 * @method array findByTargetZone(string $target_zone) Return TripLogs objects filtered by the target_zone column
 * @method array findByType(string $type) Return TripLogs objects filtered by the type column
 * @method array findByTemporary(boolean $temporary) Return TripLogs objects filtered by the temporary column
 * @method array findBySummary(string $summary) Return TripLogs objects filtered by the summary column
 *
 * @package    propel.generator.speogis.om
 */
abstract class BaseTripLogsQuery extends ModelCriteria
{
    /**
     * Initializes internal state of BaseTripLogsQuery object.
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
            $modelName = 'TripLogs';
        }
        parent::__construct($dbName, $modelName, $modelAlias);
    }

    /**
     * Returns a new TripLogsQuery object.
     *
     * @param     string $modelAlias The alias of a model in the query
     * @param   TripLogsQuery|Criteria $criteria Optional Criteria to build the query from
     *
     * @return TripLogsQuery
     */
    public static function create($modelAlias = null, $criteria = null)
    {
        if ($criteria instanceof TripLogsQuery) {
            return $criteria;
        }
        $query = new TripLogsQuery(null, null, $modelAlias);

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
     * @return   TripLogs|TripLogs[]|mixed the result, formatted by the current formatter
     */
    public function findPk($key, $con = null)
    {
        if ($key === null) {
            return null;
        }
        if ((null !== ($obj = TripLogsPeer::getInstanceFromPool((string) $key))) && !$this->formatter) {
            // the object is already in the instance pool
            return $obj;
        }
        if ($con === null) {
            $con = Propel::getConnection(TripLogsPeer::DATABASE_NAME, Propel::CONNECTION_READ);
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
     * @return                 TripLogs A model object, or null if the key is not found
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
     * @return                 TripLogs A model object, or null if the key is not found
     * @throws PropelException
     */
    protected function findPkSimple($key, $con)
    {
        $sql = 'SELECT `id`, `add_time`, `trip_start_time`, `trip_end_time`, `details`, `target_zone`, `type`, `temporary`, `summary` FROM `trip_logs` WHERE `id` = :p0';
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
            $obj = new TripLogs();
            $obj->hydrate($row);
            TripLogsPeer::addInstanceToPool($obj, (string) $key);
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
     * @return TripLogs|TripLogs[]|mixed the result, formatted by the current formatter
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
     * @return PropelObjectCollection|TripLogs[]|mixed the list of results, formatted by the current formatter
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
     * @return TripLogsQuery The current query, for fluid interface
     */
    public function filterByPrimaryKey($key)
    {

        return $this->addUsingAlias(TripLogsPeer::ID, $key, Criteria::EQUAL);
    }

    /**
     * Filter the query by a list of primary keys
     *
     * @param     array $keys The list of primary key to use for the query
     *
     * @return TripLogsQuery The current query, for fluid interface
     */
    public function filterByPrimaryKeys($keys)
    {

        return $this->addUsingAlias(TripLogsPeer::ID, $keys, Criteria::IN);
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
     * @return TripLogsQuery The current query, for fluid interface
     */
    public function filterById($id = null, $comparison = null)
    {
        if (is_array($id)) {
            $useMinMax = false;
            if (isset($id['min'])) {
                $this->addUsingAlias(TripLogsPeer::ID, $id['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($id['max'])) {
                $this->addUsingAlias(TripLogsPeer::ID, $id['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(TripLogsPeer::ID, $id, $comparison);
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
     * @return TripLogsQuery The current query, for fluid interface
     */
    public function filterByAddTime($addTime = null, $comparison = null)
    {
        if (is_array($addTime)) {
            $useMinMax = false;
            if (isset($addTime['min'])) {
                $this->addUsingAlias(TripLogsPeer::ADD_TIME, $addTime['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($addTime['max'])) {
                $this->addUsingAlias(TripLogsPeer::ADD_TIME, $addTime['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(TripLogsPeer::ADD_TIME, $addTime, $comparison);
    }

    /**
     * Filter the query on the trip_start_time column
     *
     * Example usage:
     * <code>
     * $query->filterByTripStartTime('2011-03-14'); // WHERE trip_start_time = '2011-03-14'
     * $query->filterByTripStartTime('now'); // WHERE trip_start_time = '2011-03-14'
     * $query->filterByTripStartTime(array('max' => 'yesterday')); // WHERE trip_start_time < '2011-03-13'
     * </code>
     *
     * @param     mixed $tripStartTime The value to use as filter.
     *              Values can be integers (unix timestamps), DateTime objects, or strings.
     *              Empty strings are treated as NULL.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TripLogsQuery The current query, for fluid interface
     */
    public function filterByTripStartTime($tripStartTime = null, $comparison = null)
    {
        if (is_array($tripStartTime)) {
            $useMinMax = false;
            if (isset($tripStartTime['min'])) {
                $this->addUsingAlias(TripLogsPeer::TRIP_START_TIME, $tripStartTime['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($tripStartTime['max'])) {
                $this->addUsingAlias(TripLogsPeer::TRIP_START_TIME, $tripStartTime['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(TripLogsPeer::TRIP_START_TIME, $tripStartTime, $comparison);
    }

    /**
     * Filter the query on the trip_end_time column
     *
     * Example usage:
     * <code>
     * $query->filterByTripEndTime('2011-03-14'); // WHERE trip_end_time = '2011-03-14'
     * $query->filterByTripEndTime('now'); // WHERE trip_end_time = '2011-03-14'
     * $query->filterByTripEndTime(array('max' => 'yesterday')); // WHERE trip_end_time < '2011-03-13'
     * </code>
     *
     * @param     mixed $tripEndTime The value to use as filter.
     *              Values can be integers (unix timestamps), DateTime objects, or strings.
     *              Empty strings are treated as NULL.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TripLogsQuery The current query, for fluid interface
     */
    public function filterByTripEndTime($tripEndTime = null, $comparison = null)
    {
        if (is_array($tripEndTime)) {
            $useMinMax = false;
            if (isset($tripEndTime['min'])) {
                $this->addUsingAlias(TripLogsPeer::TRIP_END_TIME, $tripEndTime['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($tripEndTime['max'])) {
                $this->addUsingAlias(TripLogsPeer::TRIP_END_TIME, $tripEndTime['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(TripLogsPeer::TRIP_END_TIME, $tripEndTime, $comparison);
    }

    /**
     * Filter the query on the details column
     *
     * Example usage:
     * <code>
     * $query->filterByDetails('fooValue');   // WHERE details = 'fooValue'
     * $query->filterByDetails('%fooValue%'); // WHERE details LIKE '%fooValue%'
     * </code>
     *
     * @param     string $details The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TripLogsQuery The current query, for fluid interface
     */
    public function filterByDetails($details = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($details)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $details)) {
                $details = str_replace('*', '%', $details);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(TripLogsPeer::DETAILS, $details, $comparison);
    }

    /**
     * Filter the query on the target_zone column
     *
     * Example usage:
     * <code>
     * $query->filterByTargetZone('fooValue');   // WHERE target_zone = 'fooValue'
     * $query->filterByTargetZone('%fooValue%'); // WHERE target_zone LIKE '%fooValue%'
     * </code>
     *
     * @param     string $targetZone The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TripLogsQuery The current query, for fluid interface
     */
    public function filterByTargetZone($targetZone = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($targetZone)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $targetZone)) {
                $targetZone = str_replace('*', '%', $targetZone);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(TripLogsPeer::TARGET_ZONE, $targetZone, $comparison);
    }

    /**
     * Filter the query on the type column
     *
     * Example usage:
     * <code>
     * $query->filterByType('fooValue');   // WHERE type = 'fooValue'
     * $query->filterByType('%fooValue%'); // WHERE type LIKE '%fooValue%'
     * </code>
     *
     * @param     string $type The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TripLogsQuery The current query, for fluid interface
     */
    public function filterByType($type = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($type)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $type)) {
                $type = str_replace('*', '%', $type);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(TripLogsPeer::TYPE, $type, $comparison);
    }

    /**
     * Filter the query on the temporary column
     *
     * Example usage:
     * <code>
     * $query->filterByTemporary(true); // WHERE temporary = true
     * $query->filterByTemporary('yes'); // WHERE temporary = true
     * </code>
     *
     * @param     boolean|string $temporary The value to use as filter.
     *              Non-boolean arguments are converted using the following rules:
     *                * 1, '1', 'true',  'on',  and 'yes' are converted to boolean true
     *                * 0, '0', 'false', 'off', and 'no'  are converted to boolean false
     *              Check on string values is case insensitive (so 'FaLsE' is seen as 'false').
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TripLogsQuery The current query, for fluid interface
     */
    public function filterByTemporary($temporary = null, $comparison = null)
    {
        if (is_string($temporary)) {
            $temporary = in_array(strtolower($temporary), array('false', 'off', '-', 'no', 'n', '0', '')) ? false : true;
        }

        return $this->addUsingAlias(TripLogsPeer::TEMPORARY, $temporary, $comparison);
    }

    /**
     * Filter the query on the summary column
     *
     * Example usage:
     * <code>
     * $query->filterBySummary('fooValue');   // WHERE summary = 'fooValue'
     * $query->filterBySummary('%fooValue%'); // WHERE summary LIKE '%fooValue%'
     * </code>
     *
     * @param     string $summary The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TripLogsQuery The current query, for fluid interface
     */
    public function filterBySummary($summary = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($summary)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $summary)) {
                $summary = str_replace('*', '%', $summary);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(TripLogsPeer::SUMMARY, $summary, $comparison);
    }

    /**
     * Exclude object from result
     *
     * @param   TripLogs $tripLogs Object to remove from the list of results
     *
     * @return TripLogsQuery The current query, for fluid interface
     */
    public function prune($tripLogs = null)
    {
        if ($tripLogs) {
            $this->addUsingAlias(TripLogsPeer::ID, $tripLogs->getId(), Criteria::NOT_EQUAL);
        }

        return $this;
    }

}
