<?php


/**
 * Base class that represents a query for the 'images' table.
 *
 *
 *
 * @method ImagesQuery orderById($order = Criteria::ASC) Order by the id column
 * @method ImagesQuery orderByFilePath($order = Criteria::ASC) Order by the file_path column
 * @method ImagesQuery orderByUserId($order = Criteria::ASC) Order by the user_id column
 * @method ImagesQuery orderByAddTime($order = Criteria::ASC) Order by the add_time column
 * @method ImagesQuery orderByPointId($order = Criteria::ASC) Order by the point_id column
 * @method ImagesQuery orderByDescription($order = Criteria::ASC) Order by the description column
 * @method ImagesQuery orderByThumbFilePath($order = Criteria::ASC) Order by the thumb_file_path column
 *
 * @method ImagesQuery groupById() Group by the id column
 * @method ImagesQuery groupByFilePath() Group by the file_path column
 * @method ImagesQuery groupByUserId() Group by the user_id column
 * @method ImagesQuery groupByAddTime() Group by the add_time column
 * @method ImagesQuery groupByPointId() Group by the point_id column
 * @method ImagesQuery groupByDescription() Group by the description column
 * @method ImagesQuery groupByThumbFilePath() Group by the thumb_file_path column
 *
 * @method ImagesQuery leftJoin($relation) Adds a LEFT JOIN clause to the query
 * @method ImagesQuery rightJoin($relation) Adds a RIGHT JOIN clause to the query
 * @method ImagesQuery innerJoin($relation) Adds a INNER JOIN clause to the query
 *
 * @method Images findOne(PropelPDO $con = null) Return the first Images matching the query
 * @method Images findOneOrCreate(PropelPDO $con = null) Return the first Images matching the query, or a new Images object populated from the query conditions when no match is found
 *
 * @method Images findOneByFilePath(string $file_path) Return the first Images filtered by the file_path column
 * @method Images findOneByUserId(string $user_id) Return the first Images filtered by the user_id column
 * @method Images findOneByAddTime(string $add_time) Return the first Images filtered by the add_time column
 * @method Images findOneByPointId(string $point_id) Return the first Images filtered by the point_id column
 * @method Images findOneByDescription(string $description) Return the first Images filtered by the description column
 * @method Images findOneByThumbFilePath(string $thumb_file_path) Return the first Images filtered by the thumb_file_path column
 *
 * @method array findById(string $id) Return Images objects filtered by the id column
 * @method array findByFilePath(string $file_path) Return Images objects filtered by the file_path column
 * @method array findByUserId(string $user_id) Return Images objects filtered by the user_id column
 * @method array findByAddTime(string $add_time) Return Images objects filtered by the add_time column
 * @method array findByPointId(string $point_id) Return Images objects filtered by the point_id column
 * @method array findByDescription(string $description) Return Images objects filtered by the description column
 * @method array findByThumbFilePath(string $thumb_file_path) Return Images objects filtered by the thumb_file_path column
 *
 * @package    propel.generator.speogis.om
 */
abstract class BaseImagesQuery extends ModelCriteria
{
    /**
     * Initializes internal state of BaseImagesQuery object.
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
            $modelName = 'Images';
        }
        parent::__construct($dbName, $modelName, $modelAlias);
    }

    /**
     * Returns a new ImagesQuery object.
     *
     * @param     string $modelAlias The alias of a model in the query
     * @param   ImagesQuery|Criteria $criteria Optional Criteria to build the query from
     *
     * @return ImagesQuery
     */
    public static function create($modelAlias = null, $criteria = null)
    {
        if ($criteria instanceof ImagesQuery) {
            return $criteria;
        }
        $query = new ImagesQuery(null, null, $modelAlias);

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
     * @return   Images|Images[]|mixed the result, formatted by the current formatter
     */
    public function findPk($key, $con = null)
    {
        if ($key === null) {
            return null;
        }
        if ((null !== ($obj = ImagesPeer::getInstanceFromPool((string) $key))) && !$this->formatter) {
            // the object is already in the instance pool
            return $obj;
        }
        if ($con === null) {
            $con = Propel::getConnection(ImagesPeer::DATABASE_NAME, Propel::CONNECTION_READ);
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
     * @return                 Images A model object, or null if the key is not found
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
     * @return                 Images A model object, or null if the key is not found
     * @throws PropelException
     */
    protected function findPkSimple($key, $con)
    {
        $sql = 'SELECT `id`, `file_path`, `user_id`, `add_time`, `point_id`, `description`, `thumb_file_path` FROM `images` WHERE `id` = :p0';
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
            $obj = new Images();
            $obj->hydrate($row);
            ImagesPeer::addInstanceToPool($obj, (string) $key);
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
     * @return Images|Images[]|mixed the result, formatted by the current formatter
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
     * @return PropelObjectCollection|Images[]|mixed the list of results, formatted by the current formatter
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
     * @return ImagesQuery The current query, for fluid interface
     */
    public function filterByPrimaryKey($key)
    {

        return $this->addUsingAlias(ImagesPeer::ID, $key, Criteria::EQUAL);
    }

    /**
     * Filter the query by a list of primary keys
     *
     * @param     array $keys The list of primary key to use for the query
     *
     * @return ImagesQuery The current query, for fluid interface
     */
    public function filterByPrimaryKeys($keys)
    {

        return $this->addUsingAlias(ImagesPeer::ID, $keys, Criteria::IN);
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
     * @return ImagesQuery The current query, for fluid interface
     */
    public function filterById($id = null, $comparison = null)
    {
        if (is_array($id)) {
            $useMinMax = false;
            if (isset($id['min'])) {
                $this->addUsingAlias(ImagesPeer::ID, $id['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($id['max'])) {
                $this->addUsingAlias(ImagesPeer::ID, $id['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(ImagesPeer::ID, $id, $comparison);
    }

    /**
     * Filter the query on the file_path column
     *
     * Example usage:
     * <code>
     * $query->filterByFilePath('fooValue');   // WHERE file_path = 'fooValue'
     * $query->filterByFilePath('%fooValue%'); // WHERE file_path LIKE '%fooValue%'
     * </code>
     *
     * @param     string $filePath The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return ImagesQuery The current query, for fluid interface
     */
    public function filterByFilePath($filePath = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($filePath)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $filePath)) {
                $filePath = str_replace('*', '%', $filePath);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(ImagesPeer::FILE_PATH, $filePath, $comparison);
    }

    /**
     * Filter the query on the user_id column
     *
     * Example usage:
     * <code>
     * $query->filterByUserId(1234); // WHERE user_id = 1234
     * $query->filterByUserId(array(12, 34)); // WHERE user_id IN (12, 34)
     * $query->filterByUserId(array('min' => 12)); // WHERE user_id >= 12
     * $query->filterByUserId(array('max' => 12)); // WHERE user_id <= 12
     * </code>
     *
     * @param     mixed $userId The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return ImagesQuery The current query, for fluid interface
     */
    public function filterByUserId($userId = null, $comparison = null)
    {
        if (is_array($userId)) {
            $useMinMax = false;
            if (isset($userId['min'])) {
                $this->addUsingAlias(ImagesPeer::USER_ID, $userId['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($userId['max'])) {
                $this->addUsingAlias(ImagesPeer::USER_ID, $userId['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(ImagesPeer::USER_ID, $userId, $comparison);
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
     * @return ImagesQuery The current query, for fluid interface
     */
    public function filterByAddTime($addTime = null, $comparison = null)
    {
        if (is_array($addTime)) {
            $useMinMax = false;
            if (isset($addTime['min'])) {
                $this->addUsingAlias(ImagesPeer::ADD_TIME, $addTime['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($addTime['max'])) {
                $this->addUsingAlias(ImagesPeer::ADD_TIME, $addTime['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(ImagesPeer::ADD_TIME, $addTime, $comparison);
    }

    /**
     * Filter the query on the point_id column
     *
     * Example usage:
     * <code>
     * $query->filterByPointId(1234); // WHERE point_id = 1234
     * $query->filterByPointId(array(12, 34)); // WHERE point_id IN (12, 34)
     * $query->filterByPointId(array('min' => 12)); // WHERE point_id >= 12
     * $query->filterByPointId(array('max' => 12)); // WHERE point_id <= 12
     * </code>
     *
     * @param     mixed $pointId The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return ImagesQuery The current query, for fluid interface
     */
    public function filterByPointId($pointId = null, $comparison = null)
    {
        if (is_array($pointId)) {
            $useMinMax = false;
            if (isset($pointId['min'])) {
                $this->addUsingAlias(ImagesPeer::POINT_ID, $pointId['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($pointId['max'])) {
                $this->addUsingAlias(ImagesPeer::POINT_ID, $pointId['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(ImagesPeer::POINT_ID, $pointId, $comparison);
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
     * @return ImagesQuery The current query, for fluid interface
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

        return $this->addUsingAlias(ImagesPeer::DESCRIPTION, $description, $comparison);
    }

    /**
     * Filter the query on the thumb_file_path column
     *
     * Example usage:
     * <code>
     * $query->filterByThumbFilePath('fooValue');   // WHERE thumb_file_path = 'fooValue'
     * $query->filterByThumbFilePath('%fooValue%'); // WHERE thumb_file_path LIKE '%fooValue%'
     * </code>
     *
     * @param     string $thumbFilePath The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return ImagesQuery The current query, for fluid interface
     */
    public function filterByThumbFilePath($thumbFilePath = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($thumbFilePath)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $thumbFilePath)) {
                $thumbFilePath = str_replace('*', '%', $thumbFilePath);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(ImagesPeer::THUMB_FILE_PATH, $thumbFilePath, $comparison);
    }

    /**
     * Exclude object from result
     *
     * @param   Images $images Object to remove from the list of results
     *
     * @return ImagesQuery The current query, for fluid interface
     */
    public function prune($images = null)
    {
        if ($images) {
            $this->addUsingAlias(ImagesPeer::ID, $images->getId(), Criteria::NOT_EQUAL);
        }

        return $this;
    }

}
