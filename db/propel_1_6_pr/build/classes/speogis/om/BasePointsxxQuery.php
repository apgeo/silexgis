<?php


/**
 * Base class that represents a query for the 'pointsxx' table.
 *
 *
 *
 * @method PointsxxQuery orderById($order = Criteria::ASC) Order by the id column
 * @method PointsxxQuery orderByLat($order = Criteria::ASC) Order by the lat column
 * @method PointsxxQuery orderByLong($order = Criteria::ASC) Order by the long column
 * @method PointsxxQuery orderByElevation($order = Criteria::ASC) Order by the elevation column
 * @method PointsxxQuery orderByCoords($order = Criteria::ASC) Order by the coords column
 * @method PointsxxQuery orderByGpxName($order = Criteria::ASC) Order by the gpx_name column
 * @method PointsxxQuery orderByGpxSym($order = Criteria::ASC) Order by the gpx_sym column
 * @method PointsxxQuery orderByGpxType($order = Criteria::ASC) Order by the gpx_type column
 * @method PointsxxQuery orderByGpxCmt($order = Criteria::ASC) Order by the gpx_cmt column
 * @method PointsxxQuery orderByGpxSat($order = Criteria::ASC) Order by the gpx_sat column
 * @method PointsxxQuery orderByGpxFix($order = Criteria::ASC) Order by the gpx_fix column
 * @method PointsxxQuery orderByGpxTime($order = Criteria::ASC) Order by the gpx_time column
 * @method PointsxxQuery orderByType($order = Criteria::ASC) Order by the _type column
 * @method PointsxxQuery orderByDetails($order = Criteria::ASC) Order by the _details column
 * @method PointsxxQuery orderByAddedByUserId($order = Criteria::ASC) Order by the added_by_user_id column
 * @method PointsxxQuery orderByAddTime($order = Criteria::ASC) Order by the add_time column
 * @method PointsxxQuery orderByIdPointType($order = Criteria::ASC) Order by the _id_point_type column
 * @method PointsxxQuery orderBySpatialGeometry($order = Criteria::ASC) Order by the spatial_geometry column
 *
 * @method PointsxxQuery groupById() Group by the id column
 * @method PointsxxQuery groupByLat() Group by the lat column
 * @method PointsxxQuery groupByLong() Group by the long column
 * @method PointsxxQuery groupByElevation() Group by the elevation column
 * @method PointsxxQuery groupByCoords() Group by the coords column
 * @method PointsxxQuery groupByGpxName() Group by the gpx_name column
 * @method PointsxxQuery groupByGpxSym() Group by the gpx_sym column
 * @method PointsxxQuery groupByGpxType() Group by the gpx_type column
 * @method PointsxxQuery groupByGpxCmt() Group by the gpx_cmt column
 * @method PointsxxQuery groupByGpxSat() Group by the gpx_sat column
 * @method PointsxxQuery groupByGpxFix() Group by the gpx_fix column
 * @method PointsxxQuery groupByGpxTime() Group by the gpx_time column
 * @method PointsxxQuery groupByType() Group by the _type column
 * @method PointsxxQuery groupByDetails() Group by the _details column
 * @method PointsxxQuery groupByAddedByUserId() Group by the added_by_user_id column
 * @method PointsxxQuery groupByAddTime() Group by the add_time column
 * @method PointsxxQuery groupByIdPointType() Group by the _id_point_type column
 * @method PointsxxQuery groupBySpatialGeometry() Group by the spatial_geometry column
 *
 * @method PointsxxQuery leftJoin($relation) Adds a LEFT JOIN clause to the query
 * @method PointsxxQuery rightJoin($relation) Adds a RIGHT JOIN clause to the query
 * @method PointsxxQuery innerJoin($relation) Adds a INNER JOIN clause to the query
 *
 * @method Pointsxx findOne(PropelPDO $con = null) Return the first Pointsxx matching the query
 * @method Pointsxx findOneOrCreate(PropelPDO $con = null) Return the first Pointsxx matching the query, or a new Pointsxx object populated from the query conditions when no match is found
 *
 * @method Pointsxx findOneByLat(double $lat) Return the first Pointsxx filtered by the lat column
 * @method Pointsxx findOneByLong(double $long) Return the first Pointsxx filtered by the long column
 * @method Pointsxx findOneByElevation(int $elevation) Return the first Pointsxx filtered by the elevation column
 * @method Pointsxx findOneByCoords(string $coords) Return the first Pointsxx filtered by the coords column
 * @method Pointsxx findOneByGpxName(string $gpx_name) Return the first Pointsxx filtered by the gpx_name column
 * @method Pointsxx findOneByGpxSym(string $gpx_sym) Return the first Pointsxx filtered by the gpx_sym column
 * @method Pointsxx findOneByGpxType(string $gpx_type) Return the first Pointsxx filtered by the gpx_type column
 * @method Pointsxx findOneByGpxCmt(string $gpx_cmt) Return the first Pointsxx filtered by the gpx_cmt column
 * @method Pointsxx findOneByGpxSat(int $gpx_sat) Return the first Pointsxx filtered by the gpx_sat column
 * @method Pointsxx findOneByGpxFix(string $gpx_fix) Return the first Pointsxx filtered by the gpx_fix column
 * @method Pointsxx findOneByGpxTime(string $gpx_time) Return the first Pointsxx filtered by the gpx_time column
 * @method Pointsxx findOneByType(int $_type) Return the first Pointsxx filtered by the _type column
 * @method Pointsxx findOneByDetails(string $_details) Return the first Pointsxx filtered by the _details column
 * @method Pointsxx findOneByAddedByUserId(string $added_by_user_id) Return the first Pointsxx filtered by the added_by_user_id column
 * @method Pointsxx findOneByAddTime(string $add_time) Return the first Pointsxx filtered by the add_time column
 * @method Pointsxx findOneByIdPointType(string $_id_point_type) Return the first Pointsxx filtered by the _id_point_type column
 * @method Pointsxx findOneBySpatialGeometry(string $spatial_geometry) Return the first Pointsxx filtered by the spatial_geometry column
 *
 * @method array findById(string $id) Return Pointsxx objects filtered by the id column
 * @method array findByLat(double $lat) Return Pointsxx objects filtered by the lat column
 * @method array findByLong(double $long) Return Pointsxx objects filtered by the long column
 * @method array findByElevation(int $elevation) Return Pointsxx objects filtered by the elevation column
 * @method array findByCoords(string $coords) Return Pointsxx objects filtered by the coords column
 * @method array findByGpxName(string $gpx_name) Return Pointsxx objects filtered by the gpx_name column
 * @method array findByGpxSym(string $gpx_sym) Return Pointsxx objects filtered by the gpx_sym column
 * @method array findByGpxType(string $gpx_type) Return Pointsxx objects filtered by the gpx_type column
 * @method array findByGpxCmt(string $gpx_cmt) Return Pointsxx objects filtered by the gpx_cmt column
 * @method array findByGpxSat(int $gpx_sat) Return Pointsxx objects filtered by the gpx_sat column
 * @method array findByGpxFix(string $gpx_fix) Return Pointsxx objects filtered by the gpx_fix column
 * @method array findByGpxTime(string $gpx_time) Return Pointsxx objects filtered by the gpx_time column
 * @method array findByType(int $_type) Return Pointsxx objects filtered by the _type column
 * @method array findByDetails(string $_details) Return Pointsxx objects filtered by the _details column
 * @method array findByAddedByUserId(string $added_by_user_id) Return Pointsxx objects filtered by the added_by_user_id column
 * @method array findByAddTime(string $add_time) Return Pointsxx objects filtered by the add_time column
 * @method array findByIdPointType(string $_id_point_type) Return Pointsxx objects filtered by the _id_point_type column
 * @method array findBySpatialGeometry(string $spatial_geometry) Return Pointsxx objects filtered by the spatial_geometry column
 *
 * @package    propel.generator.speogis.om
 */
abstract class BasePointsxxQuery extends ModelCriteria
{
    /**
     * Initializes internal state of BasePointsxxQuery object.
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
            $modelName = 'Pointsxx';
        }
        parent::__construct($dbName, $modelName, $modelAlias);
    }

    /**
     * Returns a new PointsxxQuery object.
     *
     * @param     string $modelAlias The alias of a model in the query
     * @param   PointsxxQuery|Criteria $criteria Optional Criteria to build the query from
     *
     * @return PointsxxQuery
     */
    public static function create($modelAlias = null, $criteria = null)
    {
        if ($criteria instanceof PointsxxQuery) {
            return $criteria;
        }
        $query = new PointsxxQuery(null, null, $modelAlias);

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
     * @return   Pointsxx|Pointsxx[]|mixed the result, formatted by the current formatter
     */
    public function findPk($key, $con = null)
    {
        if ($key === null) {
            return null;
        }
        if ((null !== ($obj = PointsxxPeer::getInstanceFromPool((string) $key))) && !$this->formatter) {
            // the object is already in the instance pool
            return $obj;
        }
        if ($con === null) {
            $con = Propel::getConnection(PointsxxPeer::DATABASE_NAME, Propel::CONNECTION_READ);
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
     * @return                 Pointsxx A model object, or null if the key is not found
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
     * @return                 Pointsxx A model object, or null if the key is not found
     * @throws PropelException
     */
    protected function findPkSimple($key, $con)
    {
        $sql = 'SELECT `id`, `lat`, `long`, `elevation`, `coords`, `gpx_name`, `gpx_sym`, `gpx_type`, `gpx_cmt`, `gpx_sat`, `gpx_fix`, `gpx_time`, `_type`, `_details`, `added_by_user_id`, `add_time`, `_id_point_type`, `spatial_geometry` FROM `pointsxx` WHERE `id` = :p0';
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
            $obj = new Pointsxx();
            $obj->hydrate($row);
            PointsxxPeer::addInstanceToPool($obj, (string) $key);
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
     * @return Pointsxx|Pointsxx[]|mixed the result, formatted by the current formatter
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
     * @return PropelObjectCollection|Pointsxx[]|mixed the list of results, formatted by the current formatter
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
     * @return PointsxxQuery The current query, for fluid interface
     */
    public function filterByPrimaryKey($key)
    {

        return $this->addUsingAlias(PointsxxPeer::ID, $key, Criteria::EQUAL);
    }

    /**
     * Filter the query by a list of primary keys
     *
     * @param     array $keys The list of primary key to use for the query
     *
     * @return PointsxxQuery The current query, for fluid interface
     */
    public function filterByPrimaryKeys($keys)
    {

        return $this->addUsingAlias(PointsxxPeer::ID, $keys, Criteria::IN);
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
     * @return PointsxxQuery The current query, for fluid interface
     */
    public function filterById($id = null, $comparison = null)
    {
        if (is_array($id)) {
            $useMinMax = false;
            if (isset($id['min'])) {
                $this->addUsingAlias(PointsxxPeer::ID, $id['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($id['max'])) {
                $this->addUsingAlias(PointsxxPeer::ID, $id['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(PointsxxPeer::ID, $id, $comparison);
    }

    /**
     * Filter the query on the lat column
     *
     * Example usage:
     * <code>
     * $query->filterByLat(1234); // WHERE lat = 1234
     * $query->filterByLat(array(12, 34)); // WHERE lat IN (12, 34)
     * $query->filterByLat(array('min' => 12)); // WHERE lat >= 12
     * $query->filterByLat(array('max' => 12)); // WHERE lat <= 12
     * </code>
     *
     * @param     mixed $lat The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return PointsxxQuery The current query, for fluid interface
     */
    public function filterByLat($lat = null, $comparison = null)
    {
        if (is_array($lat)) {
            $useMinMax = false;
            if (isset($lat['min'])) {
                $this->addUsingAlias(PointsxxPeer::LAT, $lat['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($lat['max'])) {
                $this->addUsingAlias(PointsxxPeer::LAT, $lat['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(PointsxxPeer::LAT, $lat, $comparison);
    }

    /**
     * Filter the query on the long column
     *
     * Example usage:
     * <code>
     * $query->filterByLong(1234); // WHERE long = 1234
     * $query->filterByLong(array(12, 34)); // WHERE long IN (12, 34)
     * $query->filterByLong(array('min' => 12)); // WHERE long >= 12
     * $query->filterByLong(array('max' => 12)); // WHERE long <= 12
     * </code>
     *
     * @param     mixed $long The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return PointsxxQuery The current query, for fluid interface
     */
    public function filterByLong($long = null, $comparison = null)
    {
        if (is_array($long)) {
            $useMinMax = false;
            if (isset($long['min'])) {
                $this->addUsingAlias(PointsxxPeer::LONG, $long['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($long['max'])) {
                $this->addUsingAlias(PointsxxPeer::LONG, $long['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(PointsxxPeer::LONG, $long, $comparison);
    }

    /**
     * Filter the query on the elevation column
     *
     * Example usage:
     * <code>
     * $query->filterByElevation(1234); // WHERE elevation = 1234
     * $query->filterByElevation(array(12, 34)); // WHERE elevation IN (12, 34)
     * $query->filterByElevation(array('min' => 12)); // WHERE elevation >= 12
     * $query->filterByElevation(array('max' => 12)); // WHERE elevation <= 12
     * </code>
     *
     * @param     mixed $elevation The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return PointsxxQuery The current query, for fluid interface
     */
    public function filterByElevation($elevation = null, $comparison = null)
    {
        if (is_array($elevation)) {
            $useMinMax = false;
            if (isset($elevation['min'])) {
                $this->addUsingAlias(PointsxxPeer::ELEVATION, $elevation['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($elevation['max'])) {
                $this->addUsingAlias(PointsxxPeer::ELEVATION, $elevation['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(PointsxxPeer::ELEVATION, $elevation, $comparison);
    }

    /**
     * Filter the query on the coords column
     *
     * Example usage:
     * <code>
     * $query->filterByCoords('fooValue');   // WHERE coords = 'fooValue'
     * $query->filterByCoords('%fooValue%'); // WHERE coords LIKE '%fooValue%'
     * </code>
     *
     * @param     string $coords The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return PointsxxQuery The current query, for fluid interface
     */
    public function filterByCoords($coords = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($coords)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $coords)) {
                $coords = str_replace('*', '%', $coords);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(PointsxxPeer::COORDS, $coords, $comparison);
    }

    /**
     * Filter the query on the gpx_name column
     *
     * Example usage:
     * <code>
     * $query->filterByGpxName('fooValue');   // WHERE gpx_name = 'fooValue'
     * $query->filterByGpxName('%fooValue%'); // WHERE gpx_name LIKE '%fooValue%'
     * </code>
     *
     * @param     string $gpxName The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return PointsxxQuery The current query, for fluid interface
     */
    public function filterByGpxName($gpxName = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($gpxName)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $gpxName)) {
                $gpxName = str_replace('*', '%', $gpxName);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(PointsxxPeer::GPX_NAME, $gpxName, $comparison);
    }

    /**
     * Filter the query on the gpx_sym column
     *
     * Example usage:
     * <code>
     * $query->filterByGpxSym('fooValue');   // WHERE gpx_sym = 'fooValue'
     * $query->filterByGpxSym('%fooValue%'); // WHERE gpx_sym LIKE '%fooValue%'
     * </code>
     *
     * @param     string $gpxSym The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return PointsxxQuery The current query, for fluid interface
     */
    public function filterByGpxSym($gpxSym = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($gpxSym)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $gpxSym)) {
                $gpxSym = str_replace('*', '%', $gpxSym);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(PointsxxPeer::GPX_SYM, $gpxSym, $comparison);
    }

    /**
     * Filter the query on the gpx_type column
     *
     * Example usage:
     * <code>
     * $query->filterByGpxType('fooValue');   // WHERE gpx_type = 'fooValue'
     * $query->filterByGpxType('%fooValue%'); // WHERE gpx_type LIKE '%fooValue%'
     * </code>
     *
     * @param     string $gpxType The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return PointsxxQuery The current query, for fluid interface
     */
    public function filterByGpxType($gpxType = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($gpxType)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $gpxType)) {
                $gpxType = str_replace('*', '%', $gpxType);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(PointsxxPeer::GPX_TYPE, $gpxType, $comparison);
    }

    /**
     * Filter the query on the gpx_cmt column
     *
     * Example usage:
     * <code>
     * $query->filterByGpxCmt('fooValue');   // WHERE gpx_cmt = 'fooValue'
     * $query->filterByGpxCmt('%fooValue%'); // WHERE gpx_cmt LIKE '%fooValue%'
     * </code>
     *
     * @param     string $gpxCmt The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return PointsxxQuery The current query, for fluid interface
     */
    public function filterByGpxCmt($gpxCmt = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($gpxCmt)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $gpxCmt)) {
                $gpxCmt = str_replace('*', '%', $gpxCmt);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(PointsxxPeer::GPX_CMT, $gpxCmt, $comparison);
    }

    /**
     * Filter the query on the gpx_sat column
     *
     * Example usage:
     * <code>
     * $query->filterByGpxSat(1234); // WHERE gpx_sat = 1234
     * $query->filterByGpxSat(array(12, 34)); // WHERE gpx_sat IN (12, 34)
     * $query->filterByGpxSat(array('min' => 12)); // WHERE gpx_sat >= 12
     * $query->filterByGpxSat(array('max' => 12)); // WHERE gpx_sat <= 12
     * </code>
     *
     * @param     mixed $gpxSat The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return PointsxxQuery The current query, for fluid interface
     */
    public function filterByGpxSat($gpxSat = null, $comparison = null)
    {
        if (is_array($gpxSat)) {
            $useMinMax = false;
            if (isset($gpxSat['min'])) {
                $this->addUsingAlias(PointsxxPeer::GPX_SAT, $gpxSat['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($gpxSat['max'])) {
                $this->addUsingAlias(PointsxxPeer::GPX_SAT, $gpxSat['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(PointsxxPeer::GPX_SAT, $gpxSat, $comparison);
    }

    /**
     * Filter the query on the gpx_fix column
     *
     * Example usage:
     * <code>
     * $query->filterByGpxFix('fooValue');   // WHERE gpx_fix = 'fooValue'
     * $query->filterByGpxFix('%fooValue%'); // WHERE gpx_fix LIKE '%fooValue%'
     * </code>
     *
     * @param     string $gpxFix The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return PointsxxQuery The current query, for fluid interface
     */
    public function filterByGpxFix($gpxFix = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($gpxFix)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $gpxFix)) {
                $gpxFix = str_replace('*', '%', $gpxFix);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(PointsxxPeer::GPX_FIX, $gpxFix, $comparison);
    }

    /**
     * Filter the query on the gpx_time column
     *
     * Example usage:
     * <code>
     * $query->filterByGpxTime('2011-03-14'); // WHERE gpx_time = '2011-03-14'
     * $query->filterByGpxTime('now'); // WHERE gpx_time = '2011-03-14'
     * $query->filterByGpxTime(array('max' => 'yesterday')); // WHERE gpx_time < '2011-03-13'
     * </code>
     *
     * @param     mixed $gpxTime The value to use as filter.
     *              Values can be integers (unix timestamps), DateTime objects, or strings.
     *              Empty strings are treated as NULL.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return PointsxxQuery The current query, for fluid interface
     */
    public function filterByGpxTime($gpxTime = null, $comparison = null)
    {
        if (is_array($gpxTime)) {
            $useMinMax = false;
            if (isset($gpxTime['min'])) {
                $this->addUsingAlias(PointsxxPeer::GPX_TIME, $gpxTime['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($gpxTime['max'])) {
                $this->addUsingAlias(PointsxxPeer::GPX_TIME, $gpxTime['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(PointsxxPeer::GPX_TIME, $gpxTime, $comparison);
    }

    /**
     * Filter the query on the _type column
     *
     * Example usage:
     * <code>
     * $query->filterByType(1234); // WHERE _type = 1234
     * $query->filterByType(array(12, 34)); // WHERE _type IN (12, 34)
     * $query->filterByType(array('min' => 12)); // WHERE _type >= 12
     * $query->filterByType(array('max' => 12)); // WHERE _type <= 12
     * </code>
     *
     * @param     mixed $type The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return PointsxxQuery The current query, for fluid interface
     */
    public function filterByType($type = null, $comparison = null)
    {
        if (is_array($type)) {
            $useMinMax = false;
            if (isset($type['min'])) {
                $this->addUsingAlias(PointsxxPeer::_TYPE, $type['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($type['max'])) {
                $this->addUsingAlias(PointsxxPeer::_TYPE, $type['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(PointsxxPeer::_TYPE, $type, $comparison);
    }

    /**
     * Filter the query on the _details column
     *
     * Example usage:
     * <code>
     * $query->filterByDetails('fooValue');   // WHERE _details = 'fooValue'
     * $query->filterByDetails('%fooValue%'); // WHERE _details LIKE '%fooValue%'
     * </code>
     *
     * @param     string $details The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return PointsxxQuery The current query, for fluid interface
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

        return $this->addUsingAlias(PointsxxPeer::_DETAILS, $details, $comparison);
    }

    /**
     * Filter the query on the added_by_user_id column
     *
     * Example usage:
     * <code>
     * $query->filterByAddedByUserId(1234); // WHERE added_by_user_id = 1234
     * $query->filterByAddedByUserId(array(12, 34)); // WHERE added_by_user_id IN (12, 34)
     * $query->filterByAddedByUserId(array('min' => 12)); // WHERE added_by_user_id >= 12
     * $query->filterByAddedByUserId(array('max' => 12)); // WHERE added_by_user_id <= 12
     * </code>
     *
     * @param     mixed $addedByUserId The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return PointsxxQuery The current query, for fluid interface
     */
    public function filterByAddedByUserId($addedByUserId = null, $comparison = null)
    {
        if (is_array($addedByUserId)) {
            $useMinMax = false;
            if (isset($addedByUserId['min'])) {
                $this->addUsingAlias(PointsxxPeer::ADDED_BY_USER_ID, $addedByUserId['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($addedByUserId['max'])) {
                $this->addUsingAlias(PointsxxPeer::ADDED_BY_USER_ID, $addedByUserId['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(PointsxxPeer::ADDED_BY_USER_ID, $addedByUserId, $comparison);
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
     * @return PointsxxQuery The current query, for fluid interface
     */
    public function filterByAddTime($addTime = null, $comparison = null)
    {
        if (is_array($addTime)) {
            $useMinMax = false;
            if (isset($addTime['min'])) {
                $this->addUsingAlias(PointsxxPeer::ADD_TIME, $addTime['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($addTime['max'])) {
                $this->addUsingAlias(PointsxxPeer::ADD_TIME, $addTime['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(PointsxxPeer::ADD_TIME, $addTime, $comparison);
    }

    /**
     * Filter the query on the _id_point_type column
     *
     * Example usage:
     * <code>
     * $query->filterByIdPointType(1234); // WHERE _id_point_type = 1234
     * $query->filterByIdPointType(array(12, 34)); // WHERE _id_point_type IN (12, 34)
     * $query->filterByIdPointType(array('min' => 12)); // WHERE _id_point_type >= 12
     * $query->filterByIdPointType(array('max' => 12)); // WHERE _id_point_type <= 12
     * </code>
     *
     * @param     mixed $idPointType The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return PointsxxQuery The current query, for fluid interface
     */
    public function filterByIdPointType($idPointType = null, $comparison = null)
    {
        if (is_array($idPointType)) {
            $useMinMax = false;
            if (isset($idPointType['min'])) {
                $this->addUsingAlias(PointsxxPeer::_ID_POINT_TYPE, $idPointType['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($idPointType['max'])) {
                $this->addUsingAlias(PointsxxPeer::_ID_POINT_TYPE, $idPointType['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(PointsxxPeer::_ID_POINT_TYPE, $idPointType, $comparison);
    }

    /**
     * Filter the query on the spatial_geometry column
     *
     * Example usage:
     * <code>
     * $query->filterBySpatialGeometry('fooValue');   // WHERE spatial_geometry = 'fooValue'
     * $query->filterBySpatialGeometry('%fooValue%'); // WHERE spatial_geometry LIKE '%fooValue%'
     * </code>
     *
     * @param     string $spatialGeometry The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return PointsxxQuery The current query, for fluid interface
     */
    public function filterBySpatialGeometry($spatialGeometry = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($spatialGeometry)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $spatialGeometry)) {
                $spatialGeometry = str_replace('*', '%', $spatialGeometry);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(PointsxxPeer::SPATIAL_GEOMETRY, $spatialGeometry, $comparison);
    }

    /**
     * Exclude object from result
     *
     * @param   Pointsxx $pointsxx Object to remove from the list of results
     *
     * @return PointsxxQuery The current query, for fluid interface
     */
    public function prune($pointsxx = null)
    {
        if ($pointsxx) {
            $this->addUsingAlias(PointsxxPeer::ID, $pointsxx->getId(), Criteria::NOT_EQUAL);
        }

        return $this;
    }

}
