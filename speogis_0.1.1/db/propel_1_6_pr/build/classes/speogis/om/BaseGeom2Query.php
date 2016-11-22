<?php


/**
 * Base class that represents a query for the 'geom2' table.
 *
 *
 *
 * @method Geom2Query orderByG($order = Criteria::ASC) Order by the g column
 * @method Geom2Query orderById($order = Criteria::ASC) Order by the id column
 *
 * @method Geom2Query groupByG() Group by the g column
 * @method Geom2Query groupById() Group by the id column
 *
 * @method Geom2Query leftJoin($relation) Adds a LEFT JOIN clause to the query
 * @method Geom2Query rightJoin($relation) Adds a RIGHT JOIN clause to the query
 * @method Geom2Query innerJoin($relation) Adds a INNER JOIN clause to the query
 *
 * @method Geom2 findOne(PropelPDO $con = null) Return the first Geom2 matching the query
 * @method Geom2 findOneOrCreate(PropelPDO $con = null) Return the first Geom2 matching the query, or a new Geom2 object populated from the query conditions when no match is found
 *
 * @method Geom2 findOneByG(string $g) Return the first Geom2 filtered by the g column
 *
 * @method array findByG(string $g) Return Geom2 objects filtered by the g column
 * @method array findById(string $id) Return Geom2 objects filtered by the id column
 *
 * @package    propel.generator.speogis.om
 */
abstract class BaseGeom2Query extends ModelCriteria
{
    /**
     * Initializes internal state of BaseGeom2Query object.
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
            $modelName = 'Geom2';
        }
        parent::__construct($dbName, $modelName, $modelAlias);
    }

    /**
     * Returns a new Geom2Query object.
     *
     * @param     string $modelAlias The alias of a model in the query
     * @param   Geom2Query|Criteria $criteria Optional Criteria to build the query from
     *
     * @return Geom2Query
     */
    public static function create($modelAlias = null, $criteria = null)
    {
        if ($criteria instanceof Geom2Query) {
            return $criteria;
        }
        $query = new Geom2Query(null, null, $modelAlias);

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
     * @return   Geom2|Geom2[]|mixed the result, formatted by the current formatter
     */
    public function findPk($key, $con = null)
    {
        if ($key === null) {
            return null;
        }
        if ((null !== ($obj = Geom2Peer::getInstanceFromPool((string) $key))) && !$this->formatter) {
            // the object is already in the instance pool
            return $obj;
        }
        if ($con === null) {
            $con = Propel::getConnection(Geom2Peer::DATABASE_NAME, Propel::CONNECTION_READ);
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
     * @return                 Geom2 A model object, or null if the key is not found
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
     * @return                 Geom2 A model object, or null if the key is not found
     * @throws PropelException
     */
    protected function findPkSimple($key, $con)
    {
        $sql = 'SELECT `g`, `id` FROM `geom2` WHERE `id` = :p0';
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
            $obj = new Geom2();
            $obj->hydrate($row);
            Geom2Peer::addInstanceToPool($obj, (string) $key);
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
     * @return Geom2|Geom2[]|mixed the result, formatted by the current formatter
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
     * @return PropelObjectCollection|Geom2[]|mixed the list of results, formatted by the current formatter
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
     * @return Geom2Query The current query, for fluid interface
     */
    public function filterByPrimaryKey($key)
    {

        return $this->addUsingAlias(Geom2Peer::ID, $key, Criteria::EQUAL);
    }

    /**
     * Filter the query by a list of primary keys
     *
     * @param     array $keys The list of primary key to use for the query
     *
     * @return Geom2Query The current query, for fluid interface
     */
    public function filterByPrimaryKeys($keys)
    {

        return $this->addUsingAlias(Geom2Peer::ID, $keys, Criteria::IN);
    }

    /**
     * Filter the query on the g column
     *
     * Example usage:
     * <code>
     * $query->filterByG('fooValue');   // WHERE g = 'fooValue'
     * $query->filterByG('%fooValue%'); // WHERE g LIKE '%fooValue%'
     * </code>
     *
     * @param     string $g The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return Geom2Query The current query, for fluid interface
     */
    public function filterByG($g = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($g)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $g)) {
                $g = str_replace('*', '%', $g);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(Geom2Peer::G, $g, $comparison);
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
     * @return Geom2Query The current query, for fluid interface
     */
    public function filterById($id = null, $comparison = null)
    {
        if (is_array($id)) {
            $useMinMax = false;
            if (isset($id['min'])) {
                $this->addUsingAlias(Geom2Peer::ID, $id['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($id['max'])) {
                $this->addUsingAlias(Geom2Peer::ID, $id['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(Geom2Peer::ID, $id, $comparison);
    }

    /**
     * Exclude object from result
     *
     * @param   Geom2 $geom2 Object to remove from the list of results
     *
     * @return Geom2Query The current query, for fluid interface
     */
    public function prune($geom2 = null)
    {
        if ($geom2) {
            $this->addUsingAlias(Geom2Peer::ID, $geom2->getId(), Criteria::NOT_EQUAL);
        }

        return $this;
    }

}
