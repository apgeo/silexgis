<?php


/**
 * Base class that represents a query for the 'georeferenced_maps' table.
 *
 *
 *
 * @method GeoreferencedMapsQuery orderById($order = Criteria::ASC) Order by the id column
 * @method GeoreferencedMapsQuery orderByDescription($order = Criteria::ASC) Order by the description column
 * @method GeoreferencedMapsQuery orderByBoundaryNorth($order = Criteria::ASC) Order by the boundary_north column
 * @method GeoreferencedMapsQuery orderByBoundaryEast($order = Criteria::ASC) Order by the boundary_east column
 * @method GeoreferencedMapsQuery orderByBoundarySouth($order = Criteria::ASC) Order by the boundary_south column
 * @method GeoreferencedMapsQuery orderByBoundaryWest($order = Criteria::ASC) Order by the boundary_west column
 * @method GeoreferencedMapsQuery orderByImageId($order = Criteria::ASC) Order by the image_id column
 * @method GeoreferencedMapsQuery orderByEnabled($order = Criteria::ASC) Order by the enabled column
 * @method GeoreferencedMapsQuery orderByTitle($order = Criteria::ASC) Order by the title column
 *
 * @method GeoreferencedMapsQuery groupById() Group by the id column
 * @method GeoreferencedMapsQuery groupByDescription() Group by the description column
 * @method GeoreferencedMapsQuery groupByBoundaryNorth() Group by the boundary_north column
 * @method GeoreferencedMapsQuery groupByBoundaryEast() Group by the boundary_east column
 * @method GeoreferencedMapsQuery groupByBoundarySouth() Group by the boundary_south column
 * @method GeoreferencedMapsQuery groupByBoundaryWest() Group by the boundary_west column
 * @method GeoreferencedMapsQuery groupByImageId() Group by the image_id column
 * @method GeoreferencedMapsQuery groupByEnabled() Group by the enabled column
 * @method GeoreferencedMapsQuery groupByTitle() Group by the title column
 *
 * @method GeoreferencedMapsQuery leftJoin($relation) Adds a LEFT JOIN clause to the query
 * @method GeoreferencedMapsQuery rightJoin($relation) Adds a RIGHT JOIN clause to the query
 * @method GeoreferencedMapsQuery innerJoin($relation) Adds a INNER JOIN clause to the query
 *
 * @method GeoreferencedMaps findOne(PropelPDO $con = null) Return the first GeoreferencedMaps matching the query
 * @method GeoreferencedMaps findOneOrCreate(PropelPDO $con = null) Return the first GeoreferencedMaps matching the query, or a new GeoreferencedMaps object populated from the query conditions when no match is found
 *
 * @method GeoreferencedMaps findOneByDescription(string $description) Return the first GeoreferencedMaps filtered by the description column
 * @method GeoreferencedMaps findOneByBoundaryNorth(string $boundary_north) Return the first GeoreferencedMaps filtered by the boundary_north column
 * @method GeoreferencedMaps findOneByBoundaryEast(string $boundary_east) Return the first GeoreferencedMaps filtered by the boundary_east column
 * @method GeoreferencedMaps findOneByBoundarySouth(string $boundary_south) Return the first GeoreferencedMaps filtered by the boundary_south column
 * @method GeoreferencedMaps findOneByBoundaryWest(string $boundary_west) Return the first GeoreferencedMaps filtered by the boundary_west column
 * @method GeoreferencedMaps findOneByImageId(string $image_id) Return the first GeoreferencedMaps filtered by the image_id column
 * @method GeoreferencedMaps findOneByEnabled(string $enabled) Return the first GeoreferencedMaps filtered by the enabled column
 * @method GeoreferencedMaps findOneByTitle(string $title) Return the first GeoreferencedMaps filtered by the title column
 *
 * @method array findById(string $id) Return GeoreferencedMaps objects filtered by the id column
 * @method array findByDescription(string $description) Return GeoreferencedMaps objects filtered by the description column
 * @method array findByBoundaryNorth(string $boundary_north) Return GeoreferencedMaps objects filtered by the boundary_north column
 * @method array findByBoundaryEast(string $boundary_east) Return GeoreferencedMaps objects filtered by the boundary_east column
 * @method array findByBoundarySouth(string $boundary_south) Return GeoreferencedMaps objects filtered by the boundary_south column
 * @method array findByBoundaryWest(string $boundary_west) Return GeoreferencedMaps objects filtered by the boundary_west column
 * @method array findByImageId(string $image_id) Return GeoreferencedMaps objects filtered by the image_id column
 * @method array findByEnabled(string $enabled) Return GeoreferencedMaps objects filtered by the enabled column
 * @method array findByTitle(string $title) Return GeoreferencedMaps objects filtered by the title column
 *
 * @package    propel.generator.speogis.om
 */
abstract class BaseGeoreferencedMapsQuery extends ModelCriteria
{
    /**
     * Initializes internal state of BaseGeoreferencedMapsQuery object.
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
            $modelName = 'GeoreferencedMaps';
        }
        parent::__construct($dbName, $modelName, $modelAlias);
    }

    /**
     * Returns a new GeoreferencedMapsQuery object.
     *
     * @param     string $modelAlias The alias of a model in the query
     * @param   GeoreferencedMapsQuery|Criteria $criteria Optional Criteria to build the query from
     *
     * @return GeoreferencedMapsQuery
     */
    public static function create($modelAlias = null, $criteria = null)
    {
        if ($criteria instanceof GeoreferencedMapsQuery) {
            return $criteria;
        }
        $query = new GeoreferencedMapsQuery(null, null, $modelAlias);

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
     * @return   GeoreferencedMaps|GeoreferencedMaps[]|mixed the result, formatted by the current formatter
     */
    public function findPk($key, $con = null)
    {
        if ($key === null) {
            return null;
        }
        if ((null !== ($obj = GeoreferencedMapsPeer::getInstanceFromPool((string) $key))) && !$this->formatter) {
            // the object is already in the instance pool
            return $obj;
        }
        if ($con === null) {
            $con = Propel::getConnection(GeoreferencedMapsPeer::DATABASE_NAME, Propel::CONNECTION_READ);
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
     * @return                 GeoreferencedMaps A model object, or null if the key is not found
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
     * @return                 GeoreferencedMaps A model object, or null if the key is not found
     * @throws PropelException
     */
    protected function findPkSimple($key, $con)
    {
        $sql = 'SELECT `id`, `description`, `boundary_north`, `boundary_east`, `boundary_south`, `boundary_west`, `image_id`, `enabled`, `title` FROM `georeferenced_maps` WHERE `id` = :p0';
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
            $obj = new GeoreferencedMaps();
            $obj->hydrate($row);
            GeoreferencedMapsPeer::addInstanceToPool($obj, (string) $key);
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
     * @return GeoreferencedMaps|GeoreferencedMaps[]|mixed the result, formatted by the current formatter
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
     * @return PropelObjectCollection|GeoreferencedMaps[]|mixed the list of results, formatted by the current formatter
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
     * @return GeoreferencedMapsQuery The current query, for fluid interface
     */
    public function filterByPrimaryKey($key)
    {

        return $this->addUsingAlias(GeoreferencedMapsPeer::ID, $key, Criteria::EQUAL);
    }

    /**
     * Filter the query by a list of primary keys
     *
     * @param     array $keys The list of primary key to use for the query
     *
     * @return GeoreferencedMapsQuery The current query, for fluid interface
     */
    public function filterByPrimaryKeys($keys)
    {

        return $this->addUsingAlias(GeoreferencedMapsPeer::ID, $keys, Criteria::IN);
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
     * @return GeoreferencedMapsQuery The current query, for fluid interface
     */
    public function filterById($id = null, $comparison = null)
    {
        if (is_array($id)) {
            $useMinMax = false;
            if (isset($id['min'])) {
                $this->addUsingAlias(GeoreferencedMapsPeer::ID, $id['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($id['max'])) {
                $this->addUsingAlias(GeoreferencedMapsPeer::ID, $id['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(GeoreferencedMapsPeer::ID, $id, $comparison);
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
     * @return GeoreferencedMapsQuery The current query, for fluid interface
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

        return $this->addUsingAlias(GeoreferencedMapsPeer::DESCRIPTION, $description, $comparison);
    }

    /**
     * Filter the query on the boundary_north column
     *
     * Example usage:
     * <code>
     * $query->filterByBoundaryNorth(1234); // WHERE boundary_north = 1234
     * $query->filterByBoundaryNorth(array(12, 34)); // WHERE boundary_north IN (12, 34)
     * $query->filterByBoundaryNorth(array('min' => 12)); // WHERE boundary_north >= 12
     * $query->filterByBoundaryNorth(array('max' => 12)); // WHERE boundary_north <= 12
     * </code>
     *
     * @param     mixed $boundaryNorth The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return GeoreferencedMapsQuery The current query, for fluid interface
     */
    public function filterByBoundaryNorth($boundaryNorth = null, $comparison = null)
    {
        if (is_array($boundaryNorth)) {
            $useMinMax = false;
            if (isset($boundaryNorth['min'])) {
                $this->addUsingAlias(GeoreferencedMapsPeer::BOUNDARY_NORTH, $boundaryNorth['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($boundaryNorth['max'])) {
                $this->addUsingAlias(GeoreferencedMapsPeer::BOUNDARY_NORTH, $boundaryNorth['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(GeoreferencedMapsPeer::BOUNDARY_NORTH, $boundaryNorth, $comparison);
    }

    /**
     * Filter the query on the boundary_east column
     *
     * Example usage:
     * <code>
     * $query->filterByBoundaryEast(1234); // WHERE boundary_east = 1234
     * $query->filterByBoundaryEast(array(12, 34)); // WHERE boundary_east IN (12, 34)
     * $query->filterByBoundaryEast(array('min' => 12)); // WHERE boundary_east >= 12
     * $query->filterByBoundaryEast(array('max' => 12)); // WHERE boundary_east <= 12
     * </code>
     *
     * @param     mixed $boundaryEast The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return GeoreferencedMapsQuery The current query, for fluid interface
     */
    public function filterByBoundaryEast($boundaryEast = null, $comparison = null)
    {
        if (is_array($boundaryEast)) {
            $useMinMax = false;
            if (isset($boundaryEast['min'])) {
                $this->addUsingAlias(GeoreferencedMapsPeer::BOUNDARY_EAST, $boundaryEast['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($boundaryEast['max'])) {
                $this->addUsingAlias(GeoreferencedMapsPeer::BOUNDARY_EAST, $boundaryEast['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(GeoreferencedMapsPeer::BOUNDARY_EAST, $boundaryEast, $comparison);
    }

    /**
     * Filter the query on the boundary_south column
     *
     * Example usage:
     * <code>
     * $query->filterByBoundarySouth(1234); // WHERE boundary_south = 1234
     * $query->filterByBoundarySouth(array(12, 34)); // WHERE boundary_south IN (12, 34)
     * $query->filterByBoundarySouth(array('min' => 12)); // WHERE boundary_south >= 12
     * $query->filterByBoundarySouth(array('max' => 12)); // WHERE boundary_south <= 12
     * </code>
     *
     * @param     mixed $boundarySouth The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return GeoreferencedMapsQuery The current query, for fluid interface
     */
    public function filterByBoundarySouth($boundarySouth = null, $comparison = null)
    {
        if (is_array($boundarySouth)) {
            $useMinMax = false;
            if (isset($boundarySouth['min'])) {
                $this->addUsingAlias(GeoreferencedMapsPeer::BOUNDARY_SOUTH, $boundarySouth['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($boundarySouth['max'])) {
                $this->addUsingAlias(GeoreferencedMapsPeer::BOUNDARY_SOUTH, $boundarySouth['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(GeoreferencedMapsPeer::BOUNDARY_SOUTH, $boundarySouth, $comparison);
    }

    /**
     * Filter the query on the boundary_west column
     *
     * Example usage:
     * <code>
     * $query->filterByBoundaryWest(1234); // WHERE boundary_west = 1234
     * $query->filterByBoundaryWest(array(12, 34)); // WHERE boundary_west IN (12, 34)
     * $query->filterByBoundaryWest(array('min' => 12)); // WHERE boundary_west >= 12
     * $query->filterByBoundaryWest(array('max' => 12)); // WHERE boundary_west <= 12
     * </code>
     *
     * @param     mixed $boundaryWest The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return GeoreferencedMapsQuery The current query, for fluid interface
     */
    public function filterByBoundaryWest($boundaryWest = null, $comparison = null)
    {
        if (is_array($boundaryWest)) {
            $useMinMax = false;
            if (isset($boundaryWest['min'])) {
                $this->addUsingAlias(GeoreferencedMapsPeer::BOUNDARY_WEST, $boundaryWest['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($boundaryWest['max'])) {
                $this->addUsingAlias(GeoreferencedMapsPeer::BOUNDARY_WEST, $boundaryWest['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(GeoreferencedMapsPeer::BOUNDARY_WEST, $boundaryWest, $comparison);
    }

    /**
     * Filter the query on the image_id column
     *
     * Example usage:
     * <code>
     * $query->filterByImageId(1234); // WHERE image_id = 1234
     * $query->filterByImageId(array(12, 34)); // WHERE image_id IN (12, 34)
     * $query->filterByImageId(array('min' => 12)); // WHERE image_id >= 12
     * $query->filterByImageId(array('max' => 12)); // WHERE image_id <= 12
     * </code>
     *
     * @param     mixed $imageId The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return GeoreferencedMapsQuery The current query, for fluid interface
     */
    public function filterByImageId($imageId = null, $comparison = null)
    {
        if (is_array($imageId)) {
            $useMinMax = false;
            if (isset($imageId['min'])) {
                $this->addUsingAlias(GeoreferencedMapsPeer::IMAGE_ID, $imageId['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($imageId['max'])) {
                $this->addUsingAlias(GeoreferencedMapsPeer::IMAGE_ID, $imageId['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(GeoreferencedMapsPeer::IMAGE_ID, $imageId, $comparison);
    }

    /**
     * Filter the query on the enabled column
     *
     * Example usage:
     * <code>
     * $query->filterByEnabled('fooValue');   // WHERE enabled = 'fooValue'
     * $query->filterByEnabled('%fooValue%'); // WHERE enabled LIKE '%fooValue%'
     * </code>
     *
     * @param     string $enabled The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return GeoreferencedMapsQuery The current query, for fluid interface
     */
    public function filterByEnabled($enabled = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($enabled)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $enabled)) {
                $enabled = str_replace('*', '%', $enabled);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(GeoreferencedMapsPeer::ENABLED, $enabled, $comparison);
    }

    /**
     * Filter the query on the title column
     *
     * Example usage:
     * <code>
     * $query->filterByTitle('fooValue');   // WHERE title = 'fooValue'
     * $query->filterByTitle('%fooValue%'); // WHERE title LIKE '%fooValue%'
     * </code>
     *
     * @param     string $title The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return GeoreferencedMapsQuery The current query, for fluid interface
     */
    public function filterByTitle($title = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($title)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $title)) {
                $title = str_replace('*', '%', $title);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(GeoreferencedMapsPeer::TITLE, $title, $comparison);
    }

    /**
     * Exclude object from result
     *
     * @param   GeoreferencedMaps $georeferencedMaps Object to remove from the list of results
     *
     * @return GeoreferencedMapsQuery The current query, for fluid interface
     */
    public function prune($georeferencedMaps = null)
    {
        if ($georeferencedMaps) {
            $this->addUsingAlias(GeoreferencedMapsPeer::ID, $georeferencedMaps->getId(), Criteria::NOT_EQUAL);
        }

        return $this;
    }

}
