<?php


/**
 * Base class that represents a query for the 'caves' table.
 *
 *
 *
 * @method CavesQuery orderById($order = Criteria::ASC) Order by the id column
 * @method CavesQuery orderByName($order = Criteria::ASC) Order by the name column
 * @method CavesQuery orderByTypeid($order = Criteria::ASC) Order by the typeId column
 * @method CavesQuery orderByLocationidentifier($order = Criteria::ASC) Order by the locationIdentifier column
 * @method CavesQuery orderByDescription($order = Criteria::ASC) Order by the description column
 *
 * @method CavesQuery groupById() Group by the id column
 * @method CavesQuery groupByName() Group by the name column
 * @method CavesQuery groupByTypeid() Group by the typeId column
 * @method CavesQuery groupByLocationidentifier() Group by the locationIdentifier column
 * @method CavesQuery groupByDescription() Group by the description column
 *
 * @method CavesQuery leftJoin($relation) Adds a LEFT JOIN clause to the query
 * @method CavesQuery rightJoin($relation) Adds a RIGHT JOIN clause to the query
 * @method CavesQuery innerJoin($relation) Adds a INNER JOIN clause to the query
 *
 * @method Caves findOne(PropelPDO $con = null) Return the first Caves matching the query
 * @method Caves findOneOrCreate(PropelPDO $con = null) Return the first Caves matching the query, or a new Caves object populated from the query conditions when no match is found
 *
 * @method Caves findOneByName(string $name) Return the first Caves filtered by the name column
 * @method Caves findOneByTypeid(string $typeId) Return the first Caves filtered by the typeId column
 * @method Caves findOneByLocationidentifier(string $locationIdentifier) Return the first Caves filtered by the locationIdentifier column
 * @method Caves findOneByDescription(string $description) Return the first Caves filtered by the description column
 *
 * @method array findById(string $id) Return Caves objects filtered by the id column
 * @method array findByName(string $name) Return Caves objects filtered by the name column
 * @method array findByTypeid(string $typeId) Return Caves objects filtered by the typeId column
 * @method array findByLocationidentifier(string $locationIdentifier) Return Caves objects filtered by the locationIdentifier column
 * @method array findByDescription(string $description) Return Caves objects filtered by the description column
 *
 * @package    propel.generator.speogis.om
 */
abstract class BaseCavesQuery extends ModelCriteria
{
    /**
     * Initializes internal state of BaseCavesQuery object.
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
            $modelName = 'Caves';
        }
        parent::__construct($dbName, $modelName, $modelAlias);
    }

    /**
     * Returns a new CavesQuery object.
     *
     * @param     string $modelAlias The alias of a model in the query
     * @param   CavesQuery|Criteria $criteria Optional Criteria to build the query from
     *
     * @return CavesQuery
     */
    public static function create($modelAlias = null, $criteria = null)
    {
        if ($criteria instanceof CavesQuery) {
            return $criteria;
        }
        $query = new CavesQuery(null, null, $modelAlias);

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
     * @return   Caves|Caves[]|mixed the result, formatted by the current formatter
     */
    public function findPk($key, $con = null)
    {
        if ($key === null) {
            return null;
        }
        if ((null !== ($obj = CavesPeer::getInstanceFromPool((string) $key))) && !$this->formatter) {
            // the object is already in the instance pool
            return $obj;
        }
        if ($con === null) {
            $con = Propel::getConnection(CavesPeer::DATABASE_NAME, Propel::CONNECTION_READ);
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
     * @return                 Caves A model object, or null if the key is not found
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
     * @return                 Caves A model object, or null if the key is not found
     * @throws PropelException
     */
    protected function findPkSimple($key, $con)
    {
        $sql = 'SELECT `id`, `name`, `typeId`, `locationIdentifier`, `description` FROM `caves` WHERE `id` = :p0';
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
            $obj = new Caves();
            $obj->hydrate($row);
            CavesPeer::addInstanceToPool($obj, (string) $key);
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
     * @return Caves|Caves[]|mixed the result, formatted by the current formatter
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
     * @return PropelObjectCollection|Caves[]|mixed the list of results, formatted by the current formatter
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
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByPrimaryKey($key)
    {

        return $this->addUsingAlias(CavesPeer::ID, $key, Criteria::EQUAL);
    }

    /**
     * Filter the query by a list of primary keys
     *
     * @param     array $keys The list of primary key to use for the query
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByPrimaryKeys($keys)
    {

        return $this->addUsingAlias(CavesPeer::ID, $keys, Criteria::IN);
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
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterById($id = null, $comparison = null)
    {
        if (is_array($id)) {
            $useMinMax = false;
            if (isset($id['min'])) {
                $this->addUsingAlias(CavesPeer::ID, $id['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($id['max'])) {
                $this->addUsingAlias(CavesPeer::ID, $id['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CavesPeer::ID, $id, $comparison);
    }

    /**
     * Filter the query on the name column
     *
     * Example usage:
     * <code>
     * $query->filterByName('fooValue');   // WHERE name = 'fooValue'
     * $query->filterByName('%fooValue%'); // WHERE name LIKE '%fooValue%'
     * </code>
     *
     * @param     string $name The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByName($name = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($name)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $name)) {
                $name = str_replace('*', '%', $name);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(CavesPeer::NAME, $name, $comparison);
    }

    /**
     * Filter the query on the typeId column
     *
     * Example usage:
     * <code>
     * $query->filterByTypeid(1234); // WHERE typeId = 1234
     * $query->filterByTypeid(array(12, 34)); // WHERE typeId IN (12, 34)
     * $query->filterByTypeid(array('min' => 12)); // WHERE typeId >= 12
     * $query->filterByTypeid(array('max' => 12)); // WHERE typeId <= 12
     * </code>
     *
     * @param     mixed $typeid The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByTypeid($typeid = null, $comparison = null)
    {
        if (is_array($typeid)) {
            $useMinMax = false;
            if (isset($typeid['min'])) {
                $this->addUsingAlias(CavesPeer::TYPEID, $typeid['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($typeid['max'])) {
                $this->addUsingAlias(CavesPeer::TYPEID, $typeid['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CavesPeer::TYPEID, $typeid, $comparison);
    }

    /**
     * Filter the query on the locationIdentifier column
     *
     * Example usage:
     * <code>
     * $query->filterByLocationidentifier('fooValue');   // WHERE locationIdentifier = 'fooValue'
     * $query->filterByLocationidentifier('%fooValue%'); // WHERE locationIdentifier LIKE '%fooValue%'
     * </code>
     *
     * @param     string $locationidentifier The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByLocationidentifier($locationidentifier = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($locationidentifier)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $locationidentifier)) {
                $locationidentifier = str_replace('*', '%', $locationidentifier);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(CavesPeer::LOCATIONIDENTIFIER, $locationidentifier, $comparison);
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
     * @return CavesQuery The current query, for fluid interface
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

        return $this->addUsingAlias(CavesPeer::DESCRIPTION, $description, $comparison);
    }

    /**
     * Exclude object from result
     *
     * @param   Caves $caves Object to remove from the list of results
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function prune($caves = null)
    {
        if ($caves) {
            $this->addUsingAlias(CavesPeer::ID, $caves->getId(), Criteria::NOT_EQUAL);
        }

        return $this;
    }

}
