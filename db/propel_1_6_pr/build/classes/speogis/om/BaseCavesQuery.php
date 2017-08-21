<?php


/**
 * Base class that represents a query for the 'caves' table.
 *
 *
 *
 * @method CavesQuery orderById($order = Criteria::ASC) Order by the id column
 * @method CavesQuery orderByName($order = Criteria::ASC) Order by the name column
 * @method CavesQuery orderByTypeId($order = Criteria::ASC) Order by the type_id column
 * @method CavesQuery orderByIdentificationCode($order = Criteria::ASC) Order by the identification_code column
 * @method CavesQuery orderByDescription($order = Criteria::ASC) Order by the description column
 * @method CavesQuery orderByUserId($order = Criteria::ASC) Order by the user_id column
 * @method CavesQuery orderByOtherToponyms($order = Criteria::ASC) Order by the other_toponyms column
 * @method CavesQuery orderByRockTypeId($order = Criteria::ASC) Order by the rock_type_id column
 * @method CavesQuery orderByRockAge($order = Criteria::ASC) Order by the rock_age column
 * @method CavesQuery orderByHydrographicBasin($order = Criteria::ASC) Order by the hydrographic_basin column
 * @method CavesQuery orderByValley($order = Criteria::ASC) Order by the valley column
 * @method CavesQuery orderByTributaryRiver($order = Criteria::ASC) Order by the tributary_river column
 * @method CavesQuery orderByClosestAddress($order = Criteria::ASC) Order by the closest_address column
 * @method CavesQuery orderByIsShowCave($order = Criteria::ASC) Order by the is_show_cave column
 * @method CavesQuery orderByShowCaveLength($order = Criteria::ASC) Order by the show_cave_length column
 * @method CavesQuery orderByWebsite($order = Criteria::ASC) Order by the website column
 * @method CavesQuery orderByLandRegistryNumber($order = Criteria::ASC) Order by the land_registry_number column
 * @method CavesQuery orderByRegion($order = Criteria::ASC) Order by the region column
 * @method CavesQuery orderByDepth($order = Criteria::ASC) Order by the depth column
 * @method CavesQuery orderBySurveyedLength($order = Criteria::ASC) Order by the surveyed_length column
 * @method CavesQuery orderByDiscoveryDate($order = Criteria::ASC) Order by the discovery_date column
 * @method CavesQuery orderByDiscoverer($order = Criteria::ASC) Order by the discoverer column
 * @method CavesQuery orderByVolume($order = Criteria::ASC) Order by the volume column
 * @method CavesQuery orderByArea($order = Criteria::ASC) Order by the area column
 * @method CavesQuery orderByPositiveDepth($order = Criteria::ASC) Order by the positive_depth column
 * @method CavesQuery orderByNegativeDepth($order = Criteria::ASC) Order by the negative_depth column
 * @method CavesQuery orderByRamificationIndex($order = Criteria::ASC) Order by the ramification_index column
 * @method CavesQuery orderByRealExtension($order = Criteria::ASC) Order by the real_extension column
 * @method CavesQuery orderByCaveAge($order = Criteria::ASC) Order by the cave_age column
 * @method CavesQuery orderByProjectedExtension($order = Criteria::ASC) Order by the projected_extension column
 * @method CavesQuery orderByExplorationStatus($order = Criteria::ASC) Order by the exploration_status column
 * @method CavesQuery orderByProtectionClass($order = Criteria::ASC) Order by the protection_class column
 * @method CavesQuery orderByPotentialDepth($order = Criteria::ASC) Order by the potential_depth column
 * @method CavesQuery orderByEstimatedLength($order = Criteria::ASC) Order by the estimated_length column
 * @method CavesQuery orderByAltitude($order = Criteria::ASC) Order by the altitude column
 *
 * @method CavesQuery groupById() Group by the id column
 * @method CavesQuery groupByName() Group by the name column
 * @method CavesQuery groupByTypeId() Group by the type_id column
 * @method CavesQuery groupByIdentificationCode() Group by the identification_code column
 * @method CavesQuery groupByDescription() Group by the description column
 * @method CavesQuery groupByUserId() Group by the user_id column
 * @method CavesQuery groupByOtherToponyms() Group by the other_toponyms column
 * @method CavesQuery groupByRockTypeId() Group by the rock_type_id column
 * @method CavesQuery groupByRockAge() Group by the rock_age column
 * @method CavesQuery groupByHydrographicBasin() Group by the hydrographic_basin column
 * @method CavesQuery groupByValley() Group by the valley column
 * @method CavesQuery groupByTributaryRiver() Group by the tributary_river column
 * @method CavesQuery groupByClosestAddress() Group by the closest_address column
 * @method CavesQuery groupByIsShowCave() Group by the is_show_cave column
 * @method CavesQuery groupByShowCaveLength() Group by the show_cave_length column
 * @method CavesQuery groupByWebsite() Group by the website column
 * @method CavesQuery groupByLandRegistryNumber() Group by the land_registry_number column
 * @method CavesQuery groupByRegion() Group by the region column
 * @method CavesQuery groupByDepth() Group by the depth column
 * @method CavesQuery groupBySurveyedLength() Group by the surveyed_length column
 * @method CavesQuery groupByDiscoveryDate() Group by the discovery_date column
 * @method CavesQuery groupByDiscoverer() Group by the discoverer column
 * @method CavesQuery groupByVolume() Group by the volume column
 * @method CavesQuery groupByArea() Group by the area column
 * @method CavesQuery groupByPositiveDepth() Group by the positive_depth column
 * @method CavesQuery groupByNegativeDepth() Group by the negative_depth column
 * @method CavesQuery groupByRamificationIndex() Group by the ramification_index column
 * @method CavesQuery groupByRealExtension() Group by the real_extension column
 * @method CavesQuery groupByCaveAge() Group by the cave_age column
 * @method CavesQuery groupByProjectedExtension() Group by the projected_extension column
 * @method CavesQuery groupByExplorationStatus() Group by the exploration_status column
 * @method CavesQuery groupByProtectionClass() Group by the protection_class column
 * @method CavesQuery groupByPotentialDepth() Group by the potential_depth column
 * @method CavesQuery groupByEstimatedLength() Group by the estimated_length column
 * @method CavesQuery groupByAltitude() Group by the altitude column
 *
 * @method CavesQuery leftJoin($relation) Adds a LEFT JOIN clause to the query
 * @method CavesQuery rightJoin($relation) Adds a RIGHT JOIN clause to the query
 * @method CavesQuery innerJoin($relation) Adds a INNER JOIN clause to the query
 *
 * @method Caves findOne(PropelPDO $con = null) Return the first Caves matching the query
 * @method Caves findOneOrCreate(PropelPDO $con = null) Return the first Caves matching the query, or a new Caves object populated from the query conditions when no match is found
 *
 * @method Caves findOneByName(string $name) Return the first Caves filtered by the name column
 * @method Caves findOneByTypeId(string $type_id) Return the first Caves filtered by the type_id column
 * @method Caves findOneByIdentificationCode(string $identification_code) Return the first Caves filtered by the identification_code column
 * @method Caves findOneByDescription(string $description) Return the first Caves filtered by the description column
 * @method Caves findOneByUserId(string $user_id) Return the first Caves filtered by the user_id column
 * @method Caves findOneByOtherToponyms(string $other_toponyms) Return the first Caves filtered by the other_toponyms column
 * @method Caves findOneByRockTypeId(string $rock_type_id) Return the first Caves filtered by the rock_type_id column
 * @method Caves findOneByRockAge(string $rock_age) Return the first Caves filtered by the rock_age column
 * @method Caves findOneByHydrographicBasin(string $hydrographic_basin) Return the first Caves filtered by the hydrographic_basin column
 * @method Caves findOneByValley(string $valley) Return the first Caves filtered by the valley column
 * @method Caves findOneByTributaryRiver(string $tributary_river) Return the first Caves filtered by the tributary_river column
 * @method Caves findOneByClosestAddress(string $closest_address) Return the first Caves filtered by the closest_address column
 * @method Caves findOneByIsShowCave(boolean $is_show_cave) Return the first Caves filtered by the is_show_cave column
 * @method Caves findOneByShowCaveLength(int $show_cave_length) Return the first Caves filtered by the show_cave_length column
 * @method Caves findOneByWebsite(string $website) Return the first Caves filtered by the website column
 * @method Caves findOneByLandRegistryNumber(string $land_registry_number) Return the first Caves filtered by the land_registry_number column
 * @method Caves findOneByRegion(string $region) Return the first Caves filtered by the region column
 * @method Caves findOneByDepth(int $depth) Return the first Caves filtered by the depth column
 * @method Caves findOneBySurveyedLength(int $surveyed_length) Return the first Caves filtered by the surveyed_length column
 * @method Caves findOneByDiscoveryDate(string $discovery_date) Return the first Caves filtered by the discovery_date column
 * @method Caves findOneByDiscoverer(string $discoverer) Return the first Caves filtered by the discoverer column
 * @method Caves findOneByVolume(int $volume) Return the first Caves filtered by the volume column
 * @method Caves findOneByArea(int $area) Return the first Caves filtered by the area column
 * @method Caves findOneByPositiveDepth(int $positive_depth) Return the first Caves filtered by the positive_depth column
 * @method Caves findOneByNegativeDepth(int $negative_depth) Return the first Caves filtered by the negative_depth column
 * @method Caves findOneByRamificationIndex(int $ramification_index) Return the first Caves filtered by the ramification_index column
 * @method Caves findOneByRealExtension(int $real_extension) Return the first Caves filtered by the real_extension column
 * @method Caves findOneByCaveAge(int $cave_age) Return the first Caves filtered by the cave_age column
 * @method Caves findOneByProjectedExtension(int $projected_extension) Return the first Caves filtered by the projected_extension column
 * @method Caves findOneByExplorationStatus(string $exploration_status) Return the first Caves filtered by the exploration_status column
 * @method Caves findOneByProtectionClass(string $protection_class) Return the first Caves filtered by the protection_class column
 * @method Caves findOneByPotentialDepth(int $potential_depth) Return the first Caves filtered by the potential_depth column
 * @method Caves findOneByEstimatedLength(int $estimated_length) Return the first Caves filtered by the estimated_length column
 * @method Caves findOneByAltitude(int $altitude) Return the first Caves filtered by the altitude column
 *
 * @method array findById(string $id) Return Caves objects filtered by the id column
 * @method array findByName(string $name) Return Caves objects filtered by the name column
 * @method array findByTypeId(string $type_id) Return Caves objects filtered by the type_id column
 * @method array findByIdentificationCode(string $identification_code) Return Caves objects filtered by the identification_code column
 * @method array findByDescription(string $description) Return Caves objects filtered by the description column
 * @method array findByUserId(string $user_id) Return Caves objects filtered by the user_id column
 * @method array findByOtherToponyms(string $other_toponyms) Return Caves objects filtered by the other_toponyms column
 * @method array findByRockTypeId(string $rock_type_id) Return Caves objects filtered by the rock_type_id column
 * @method array findByRockAge(string $rock_age) Return Caves objects filtered by the rock_age column
 * @method array findByHydrographicBasin(string $hydrographic_basin) Return Caves objects filtered by the hydrographic_basin column
 * @method array findByValley(string $valley) Return Caves objects filtered by the valley column
 * @method array findByTributaryRiver(string $tributary_river) Return Caves objects filtered by the tributary_river column
 * @method array findByClosestAddress(string $closest_address) Return Caves objects filtered by the closest_address column
 * @method array findByIsShowCave(boolean $is_show_cave) Return Caves objects filtered by the is_show_cave column
 * @method array findByShowCaveLength(int $show_cave_length) Return Caves objects filtered by the show_cave_length column
 * @method array findByWebsite(string $website) Return Caves objects filtered by the website column
 * @method array findByLandRegistryNumber(string $land_registry_number) Return Caves objects filtered by the land_registry_number column
 * @method array findByRegion(string $region) Return Caves objects filtered by the region column
 * @method array findByDepth(int $depth) Return Caves objects filtered by the depth column
 * @method array findBySurveyedLength(int $surveyed_length) Return Caves objects filtered by the surveyed_length column
 * @method array findByDiscoveryDate(string $discovery_date) Return Caves objects filtered by the discovery_date column
 * @method array findByDiscoverer(string $discoverer) Return Caves objects filtered by the discoverer column
 * @method array findByVolume(int $volume) Return Caves objects filtered by the volume column
 * @method array findByArea(int $area) Return Caves objects filtered by the area column
 * @method array findByPositiveDepth(int $positive_depth) Return Caves objects filtered by the positive_depth column
 * @method array findByNegativeDepth(int $negative_depth) Return Caves objects filtered by the negative_depth column
 * @method array findByRamificationIndex(int $ramification_index) Return Caves objects filtered by the ramification_index column
 * @method array findByRealExtension(int $real_extension) Return Caves objects filtered by the real_extension column
 * @method array findByCaveAge(int $cave_age) Return Caves objects filtered by the cave_age column
 * @method array findByProjectedExtension(int $projected_extension) Return Caves objects filtered by the projected_extension column
 * @method array findByExplorationStatus(string $exploration_status) Return Caves objects filtered by the exploration_status column
 * @method array findByProtectionClass(string $protection_class) Return Caves objects filtered by the protection_class column
 * @method array findByPotentialDepth(int $potential_depth) Return Caves objects filtered by the potential_depth column
 * @method array findByEstimatedLength(int $estimated_length) Return Caves objects filtered by the estimated_length column
 * @method array findByAltitude(int $altitude) Return Caves objects filtered by the altitude column
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
        $sql = 'SELECT `id`, `name`, `type_id`, `identification_code`, `description`, `user_id`, `other_toponyms`, `rock_type_id`, `rock_age`, `hydrographic_basin`, `valley`, `tributary_river`, `closest_address`, `is_show_cave`, `show_cave_length`, `website`, `land_registry_number`, `region`, `depth`, `surveyed_length`, `discovery_date`, `discoverer`, `volume`, `area`, `positive_depth`, `negative_depth`, `ramification_index`, `real_extension`, `cave_age`, `projected_extension`, `exploration_status`, `protection_class`, `potential_depth`, `estimated_length`, `altitude` FROM `caves` WHERE `id` = :p0';
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
     * Filter the query on the type_id column
     *
     * Example usage:
     * <code>
     * $query->filterByTypeId(1234); // WHERE type_id = 1234
     * $query->filterByTypeId(array(12, 34)); // WHERE type_id IN (12, 34)
     * $query->filterByTypeId(array('min' => 12)); // WHERE type_id >= 12
     * $query->filterByTypeId(array('max' => 12)); // WHERE type_id <= 12
     * </code>
     *
     * @param     mixed $typeId The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByTypeId($typeId = null, $comparison = null)
    {
        if (is_array($typeId)) {
            $useMinMax = false;
            if (isset($typeId['min'])) {
                $this->addUsingAlias(CavesPeer::TYPE_ID, $typeId['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($typeId['max'])) {
                $this->addUsingAlias(CavesPeer::TYPE_ID, $typeId['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CavesPeer::TYPE_ID, $typeId, $comparison);
    }

    /**
     * Filter the query on the identification_code column
     *
     * Example usage:
     * <code>
     * $query->filterByIdentificationCode('fooValue');   // WHERE identification_code = 'fooValue'
     * $query->filterByIdentificationCode('%fooValue%'); // WHERE identification_code LIKE '%fooValue%'
     * </code>
     *
     * @param     string $identificationCode The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByIdentificationCode($identificationCode = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($identificationCode)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $identificationCode)) {
                $identificationCode = str_replace('*', '%', $identificationCode);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(CavesPeer::IDENTIFICATION_CODE, $identificationCode, $comparison);
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
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByUserId($userId = null, $comparison = null)
    {
        if (is_array($userId)) {
            $useMinMax = false;
            if (isset($userId['min'])) {
                $this->addUsingAlias(CavesPeer::USER_ID, $userId['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($userId['max'])) {
                $this->addUsingAlias(CavesPeer::USER_ID, $userId['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CavesPeer::USER_ID, $userId, $comparison);
    }

    /**
     * Filter the query on the other_toponyms column
     *
     * Example usage:
     * <code>
     * $query->filterByOtherToponyms('fooValue');   // WHERE other_toponyms = 'fooValue'
     * $query->filterByOtherToponyms('%fooValue%'); // WHERE other_toponyms LIKE '%fooValue%'
     * </code>
     *
     * @param     string $otherToponyms The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByOtherToponyms($otherToponyms = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($otherToponyms)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $otherToponyms)) {
                $otherToponyms = str_replace('*', '%', $otherToponyms);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(CavesPeer::OTHER_TOPONYMS, $otherToponyms, $comparison);
    }

    /**
     * Filter the query on the rock_type_id column
     *
     * Example usage:
     * <code>
     * $query->filterByRockTypeId(1234); // WHERE rock_type_id = 1234
     * $query->filterByRockTypeId(array(12, 34)); // WHERE rock_type_id IN (12, 34)
     * $query->filterByRockTypeId(array('min' => 12)); // WHERE rock_type_id >= 12
     * $query->filterByRockTypeId(array('max' => 12)); // WHERE rock_type_id <= 12
     * </code>
     *
     * @param     mixed $rockTypeId The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByRockTypeId($rockTypeId = null, $comparison = null)
    {
        if (is_array($rockTypeId)) {
            $useMinMax = false;
            if (isset($rockTypeId['min'])) {
                $this->addUsingAlias(CavesPeer::ROCK_TYPE_ID, $rockTypeId['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($rockTypeId['max'])) {
                $this->addUsingAlias(CavesPeer::ROCK_TYPE_ID, $rockTypeId['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CavesPeer::ROCK_TYPE_ID, $rockTypeId, $comparison);
    }

    /**
     * Filter the query on the rock_age column
     *
     * Example usage:
     * <code>
     * $query->filterByRockAge('fooValue');   // WHERE rock_age = 'fooValue'
     * $query->filterByRockAge('%fooValue%'); // WHERE rock_age LIKE '%fooValue%'
     * </code>
     *
     * @param     string $rockAge The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByRockAge($rockAge = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($rockAge)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $rockAge)) {
                $rockAge = str_replace('*', '%', $rockAge);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(CavesPeer::ROCK_AGE, $rockAge, $comparison);
    }

    /**
     * Filter the query on the hydrographic_basin column
     *
     * Example usage:
     * <code>
     * $query->filterByHydrographicBasin('fooValue');   // WHERE hydrographic_basin = 'fooValue'
     * $query->filterByHydrographicBasin('%fooValue%'); // WHERE hydrographic_basin LIKE '%fooValue%'
     * </code>
     *
     * @param     string $hydrographicBasin The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByHydrographicBasin($hydrographicBasin = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($hydrographicBasin)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $hydrographicBasin)) {
                $hydrographicBasin = str_replace('*', '%', $hydrographicBasin);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(CavesPeer::HYDROGRAPHIC_BASIN, $hydrographicBasin, $comparison);
    }

    /**
     * Filter the query on the valley column
     *
     * Example usage:
     * <code>
     * $query->filterByValley('fooValue');   // WHERE valley = 'fooValue'
     * $query->filterByValley('%fooValue%'); // WHERE valley LIKE '%fooValue%'
     * </code>
     *
     * @param     string $valley The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByValley($valley = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($valley)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $valley)) {
                $valley = str_replace('*', '%', $valley);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(CavesPeer::VALLEY, $valley, $comparison);
    }

    /**
     * Filter the query on the tributary_river column
     *
     * Example usage:
     * <code>
     * $query->filterByTributaryRiver('fooValue');   // WHERE tributary_river = 'fooValue'
     * $query->filterByTributaryRiver('%fooValue%'); // WHERE tributary_river LIKE '%fooValue%'
     * </code>
     *
     * @param     string $tributaryRiver The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByTributaryRiver($tributaryRiver = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($tributaryRiver)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $tributaryRiver)) {
                $tributaryRiver = str_replace('*', '%', $tributaryRiver);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(CavesPeer::TRIBUTARY_RIVER, $tributaryRiver, $comparison);
    }

    /**
     * Filter the query on the closest_address column
     *
     * Example usage:
     * <code>
     * $query->filterByClosestAddress('fooValue');   // WHERE closest_address = 'fooValue'
     * $query->filterByClosestAddress('%fooValue%'); // WHERE closest_address LIKE '%fooValue%'
     * </code>
     *
     * @param     string $closestAddress The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByClosestAddress($closestAddress = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($closestAddress)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $closestAddress)) {
                $closestAddress = str_replace('*', '%', $closestAddress);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(CavesPeer::CLOSEST_ADDRESS, $closestAddress, $comparison);
    }

    /**
     * Filter the query on the is_show_cave column
     *
     * Example usage:
     * <code>
     * $query->filterByIsShowCave(true); // WHERE is_show_cave = true
     * $query->filterByIsShowCave('yes'); // WHERE is_show_cave = true
     * </code>
     *
     * @param     boolean|string $isShowCave The value to use as filter.
     *              Non-boolean arguments are converted using the following rules:
     *                * 1, '1', 'true',  'on',  and 'yes' are converted to boolean true
     *                * 0, '0', 'false', 'off', and 'no'  are converted to boolean false
     *              Check on string values is case insensitive (so 'FaLsE' is seen as 'false').
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByIsShowCave($isShowCave = null, $comparison = null)
    {
        if (is_string($isShowCave)) {
            $isShowCave = in_array(strtolower($isShowCave), array('false', 'off', '-', 'no', 'n', '0', '')) ? false : true;
        }

        return $this->addUsingAlias(CavesPeer::IS_SHOW_CAVE, $isShowCave, $comparison);
    }

    /**
     * Filter the query on the show_cave_length column
     *
     * Example usage:
     * <code>
     * $query->filterByShowCaveLength(1234); // WHERE show_cave_length = 1234
     * $query->filterByShowCaveLength(array(12, 34)); // WHERE show_cave_length IN (12, 34)
     * $query->filterByShowCaveLength(array('min' => 12)); // WHERE show_cave_length >= 12
     * $query->filterByShowCaveLength(array('max' => 12)); // WHERE show_cave_length <= 12
     * </code>
     *
     * @param     mixed $showCaveLength The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByShowCaveLength($showCaveLength = null, $comparison = null)
    {
        if (is_array($showCaveLength)) {
            $useMinMax = false;
            if (isset($showCaveLength['min'])) {
                $this->addUsingAlias(CavesPeer::SHOW_CAVE_LENGTH, $showCaveLength['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($showCaveLength['max'])) {
                $this->addUsingAlias(CavesPeer::SHOW_CAVE_LENGTH, $showCaveLength['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CavesPeer::SHOW_CAVE_LENGTH, $showCaveLength, $comparison);
    }

    /**
     * Filter the query on the website column
     *
     * Example usage:
     * <code>
     * $query->filterByWebsite('fooValue');   // WHERE website = 'fooValue'
     * $query->filterByWebsite('%fooValue%'); // WHERE website LIKE '%fooValue%'
     * </code>
     *
     * @param     string $website The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByWebsite($website = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($website)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $website)) {
                $website = str_replace('*', '%', $website);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(CavesPeer::WEBSITE, $website, $comparison);
    }

    /**
     * Filter the query on the land_registry_number column
     *
     * Example usage:
     * <code>
     * $query->filterByLandRegistryNumber('fooValue');   // WHERE land_registry_number = 'fooValue'
     * $query->filterByLandRegistryNumber('%fooValue%'); // WHERE land_registry_number LIKE '%fooValue%'
     * </code>
     *
     * @param     string $landRegistryNumber The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByLandRegistryNumber($landRegistryNumber = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($landRegistryNumber)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $landRegistryNumber)) {
                $landRegistryNumber = str_replace('*', '%', $landRegistryNumber);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(CavesPeer::LAND_REGISTRY_NUMBER, $landRegistryNumber, $comparison);
    }

    /**
     * Filter the query on the region column
     *
     * Example usage:
     * <code>
     * $query->filterByRegion('fooValue');   // WHERE region = 'fooValue'
     * $query->filterByRegion('%fooValue%'); // WHERE region LIKE '%fooValue%'
     * </code>
     *
     * @param     string $region The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByRegion($region = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($region)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $region)) {
                $region = str_replace('*', '%', $region);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(CavesPeer::REGION, $region, $comparison);
    }

    /**
     * Filter the query on the depth column
     *
     * Example usage:
     * <code>
     * $query->filterByDepth(1234); // WHERE depth = 1234
     * $query->filterByDepth(array(12, 34)); // WHERE depth IN (12, 34)
     * $query->filterByDepth(array('min' => 12)); // WHERE depth >= 12
     * $query->filterByDepth(array('max' => 12)); // WHERE depth <= 12
     * </code>
     *
     * @param     mixed $depth The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByDepth($depth = null, $comparison = null)
    {
        if (is_array($depth)) {
            $useMinMax = false;
            if (isset($depth['min'])) {
                $this->addUsingAlias(CavesPeer::DEPTH, $depth['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($depth['max'])) {
                $this->addUsingAlias(CavesPeer::DEPTH, $depth['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CavesPeer::DEPTH, $depth, $comparison);
    }

    /**
     * Filter the query on the surveyed_length column
     *
     * Example usage:
     * <code>
     * $query->filterBySurveyedLength(1234); // WHERE surveyed_length = 1234
     * $query->filterBySurveyedLength(array(12, 34)); // WHERE surveyed_length IN (12, 34)
     * $query->filterBySurveyedLength(array('min' => 12)); // WHERE surveyed_length >= 12
     * $query->filterBySurveyedLength(array('max' => 12)); // WHERE surveyed_length <= 12
     * </code>
     *
     * @param     mixed $surveyedLength The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterBySurveyedLength($surveyedLength = null, $comparison = null)
    {
        if (is_array($surveyedLength)) {
            $useMinMax = false;
            if (isset($surveyedLength['min'])) {
                $this->addUsingAlias(CavesPeer::SURVEYED_LENGTH, $surveyedLength['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($surveyedLength['max'])) {
                $this->addUsingAlias(CavesPeer::SURVEYED_LENGTH, $surveyedLength['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CavesPeer::SURVEYED_LENGTH, $surveyedLength, $comparison);
    }

    /**
     * Filter the query on the discovery_date column
     *
     * Example usage:
     * <code>
     * $query->filterByDiscoveryDate('fooValue');   // WHERE discovery_date = 'fooValue'
     * $query->filterByDiscoveryDate('%fooValue%'); // WHERE discovery_date LIKE '%fooValue%'
     * </code>
     *
     * @param     string $discoveryDate The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByDiscoveryDate($discoveryDate = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($discoveryDate)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $discoveryDate)) {
                $discoveryDate = str_replace('*', '%', $discoveryDate);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(CavesPeer::DISCOVERY_DATE, $discoveryDate, $comparison);
    }

    /**
     * Filter the query on the discoverer column
     *
     * Example usage:
     * <code>
     * $query->filterByDiscoverer('fooValue');   // WHERE discoverer = 'fooValue'
     * $query->filterByDiscoverer('%fooValue%'); // WHERE discoverer LIKE '%fooValue%'
     * </code>
     *
     * @param     string $discoverer The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByDiscoverer($discoverer = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($discoverer)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $discoverer)) {
                $discoverer = str_replace('*', '%', $discoverer);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(CavesPeer::DISCOVERER, $discoverer, $comparison);
    }

    /**
     * Filter the query on the volume column
     *
     * Example usage:
     * <code>
     * $query->filterByVolume(1234); // WHERE volume = 1234
     * $query->filterByVolume(array(12, 34)); // WHERE volume IN (12, 34)
     * $query->filterByVolume(array('min' => 12)); // WHERE volume >= 12
     * $query->filterByVolume(array('max' => 12)); // WHERE volume <= 12
     * </code>
     *
     * @param     mixed $volume The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByVolume($volume = null, $comparison = null)
    {
        if (is_array($volume)) {
            $useMinMax = false;
            if (isset($volume['min'])) {
                $this->addUsingAlias(CavesPeer::VOLUME, $volume['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($volume['max'])) {
                $this->addUsingAlias(CavesPeer::VOLUME, $volume['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CavesPeer::VOLUME, $volume, $comparison);
    }

    /**
     * Filter the query on the area column
     *
     * Example usage:
     * <code>
     * $query->filterByArea(1234); // WHERE area = 1234
     * $query->filterByArea(array(12, 34)); // WHERE area IN (12, 34)
     * $query->filterByArea(array('min' => 12)); // WHERE area >= 12
     * $query->filterByArea(array('max' => 12)); // WHERE area <= 12
     * </code>
     *
     * @param     mixed $area The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByArea($area = null, $comparison = null)
    {
        if (is_array($area)) {
            $useMinMax = false;
            if (isset($area['min'])) {
                $this->addUsingAlias(CavesPeer::AREA, $area['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($area['max'])) {
                $this->addUsingAlias(CavesPeer::AREA, $area['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CavesPeer::AREA, $area, $comparison);
    }

    /**
     * Filter the query on the positive_depth column
     *
     * Example usage:
     * <code>
     * $query->filterByPositiveDepth(1234); // WHERE positive_depth = 1234
     * $query->filterByPositiveDepth(array(12, 34)); // WHERE positive_depth IN (12, 34)
     * $query->filterByPositiveDepth(array('min' => 12)); // WHERE positive_depth >= 12
     * $query->filterByPositiveDepth(array('max' => 12)); // WHERE positive_depth <= 12
     * </code>
     *
     * @param     mixed $positiveDepth The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByPositiveDepth($positiveDepth = null, $comparison = null)
    {
        if (is_array($positiveDepth)) {
            $useMinMax = false;
            if (isset($positiveDepth['min'])) {
                $this->addUsingAlias(CavesPeer::POSITIVE_DEPTH, $positiveDepth['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($positiveDepth['max'])) {
                $this->addUsingAlias(CavesPeer::POSITIVE_DEPTH, $positiveDepth['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CavesPeer::POSITIVE_DEPTH, $positiveDepth, $comparison);
    }

    /**
     * Filter the query on the negative_depth column
     *
     * Example usage:
     * <code>
     * $query->filterByNegativeDepth(1234); // WHERE negative_depth = 1234
     * $query->filterByNegativeDepth(array(12, 34)); // WHERE negative_depth IN (12, 34)
     * $query->filterByNegativeDepth(array('min' => 12)); // WHERE negative_depth >= 12
     * $query->filterByNegativeDepth(array('max' => 12)); // WHERE negative_depth <= 12
     * </code>
     *
     * @param     mixed $negativeDepth The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByNegativeDepth($negativeDepth = null, $comparison = null)
    {
        if (is_array($negativeDepth)) {
            $useMinMax = false;
            if (isset($negativeDepth['min'])) {
                $this->addUsingAlias(CavesPeer::NEGATIVE_DEPTH, $negativeDepth['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($negativeDepth['max'])) {
                $this->addUsingAlias(CavesPeer::NEGATIVE_DEPTH, $negativeDepth['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CavesPeer::NEGATIVE_DEPTH, $negativeDepth, $comparison);
    }

    /**
     * Filter the query on the ramification_index column
     *
     * Example usage:
     * <code>
     * $query->filterByRamificationIndex(1234); // WHERE ramification_index = 1234
     * $query->filterByRamificationIndex(array(12, 34)); // WHERE ramification_index IN (12, 34)
     * $query->filterByRamificationIndex(array('min' => 12)); // WHERE ramification_index >= 12
     * $query->filterByRamificationIndex(array('max' => 12)); // WHERE ramification_index <= 12
     * </code>
     *
     * @param     mixed $ramificationIndex The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByRamificationIndex($ramificationIndex = null, $comparison = null)
    {
        if (is_array($ramificationIndex)) {
            $useMinMax = false;
            if (isset($ramificationIndex['min'])) {
                $this->addUsingAlias(CavesPeer::RAMIFICATION_INDEX, $ramificationIndex['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($ramificationIndex['max'])) {
                $this->addUsingAlias(CavesPeer::RAMIFICATION_INDEX, $ramificationIndex['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CavesPeer::RAMIFICATION_INDEX, $ramificationIndex, $comparison);
    }

    /**
     * Filter the query on the real_extension column
     *
     * Example usage:
     * <code>
     * $query->filterByRealExtension(1234); // WHERE real_extension = 1234
     * $query->filterByRealExtension(array(12, 34)); // WHERE real_extension IN (12, 34)
     * $query->filterByRealExtension(array('min' => 12)); // WHERE real_extension >= 12
     * $query->filterByRealExtension(array('max' => 12)); // WHERE real_extension <= 12
     * </code>
     *
     * @param     mixed $realExtension The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByRealExtension($realExtension = null, $comparison = null)
    {
        if (is_array($realExtension)) {
            $useMinMax = false;
            if (isset($realExtension['min'])) {
                $this->addUsingAlias(CavesPeer::REAL_EXTENSION, $realExtension['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($realExtension['max'])) {
                $this->addUsingAlias(CavesPeer::REAL_EXTENSION, $realExtension['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CavesPeer::REAL_EXTENSION, $realExtension, $comparison);
    }

    /**
     * Filter the query on the cave_age column
     *
     * Example usage:
     * <code>
     * $query->filterByCaveAge(1234); // WHERE cave_age = 1234
     * $query->filterByCaveAge(array(12, 34)); // WHERE cave_age IN (12, 34)
     * $query->filterByCaveAge(array('min' => 12)); // WHERE cave_age >= 12
     * $query->filterByCaveAge(array('max' => 12)); // WHERE cave_age <= 12
     * </code>
     *
     * @param     mixed $caveAge The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByCaveAge($caveAge = null, $comparison = null)
    {
        if (is_array($caveAge)) {
            $useMinMax = false;
            if (isset($caveAge['min'])) {
                $this->addUsingAlias(CavesPeer::CAVE_AGE, $caveAge['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($caveAge['max'])) {
                $this->addUsingAlias(CavesPeer::CAVE_AGE, $caveAge['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CavesPeer::CAVE_AGE, $caveAge, $comparison);
    }

    /**
     * Filter the query on the projected_extension column
     *
     * Example usage:
     * <code>
     * $query->filterByProjectedExtension(1234); // WHERE projected_extension = 1234
     * $query->filterByProjectedExtension(array(12, 34)); // WHERE projected_extension IN (12, 34)
     * $query->filterByProjectedExtension(array('min' => 12)); // WHERE projected_extension >= 12
     * $query->filterByProjectedExtension(array('max' => 12)); // WHERE projected_extension <= 12
     * </code>
     *
     * @param     mixed $projectedExtension The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByProjectedExtension($projectedExtension = null, $comparison = null)
    {
        if (is_array($projectedExtension)) {
            $useMinMax = false;
            if (isset($projectedExtension['min'])) {
                $this->addUsingAlias(CavesPeer::PROJECTED_EXTENSION, $projectedExtension['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($projectedExtension['max'])) {
                $this->addUsingAlias(CavesPeer::PROJECTED_EXTENSION, $projectedExtension['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CavesPeer::PROJECTED_EXTENSION, $projectedExtension, $comparison);
    }

    /**
     * Filter the query on the exploration_status column
     *
     * Example usage:
     * <code>
     * $query->filterByExplorationStatus('fooValue');   // WHERE exploration_status = 'fooValue'
     * $query->filterByExplorationStatus('%fooValue%'); // WHERE exploration_status LIKE '%fooValue%'
     * </code>
     *
     * @param     string $explorationStatus The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByExplorationStatus($explorationStatus = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($explorationStatus)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $explorationStatus)) {
                $explorationStatus = str_replace('*', '%', $explorationStatus);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(CavesPeer::EXPLORATION_STATUS, $explorationStatus, $comparison);
    }

    /**
     * Filter the query on the protection_class column
     *
     * Example usage:
     * <code>
     * $query->filterByProtectionClass('fooValue');   // WHERE protection_class = 'fooValue'
     * $query->filterByProtectionClass('%fooValue%'); // WHERE protection_class LIKE '%fooValue%'
     * </code>
     *
     * @param     string $protectionClass The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByProtectionClass($protectionClass = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($protectionClass)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $protectionClass)) {
                $protectionClass = str_replace('*', '%', $protectionClass);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(CavesPeer::PROTECTION_CLASS, $protectionClass, $comparison);
    }

    /**
     * Filter the query on the potential_depth column
     *
     * Example usage:
     * <code>
     * $query->filterByPotentialDepth(1234); // WHERE potential_depth = 1234
     * $query->filterByPotentialDepth(array(12, 34)); // WHERE potential_depth IN (12, 34)
     * $query->filterByPotentialDepth(array('min' => 12)); // WHERE potential_depth >= 12
     * $query->filterByPotentialDepth(array('max' => 12)); // WHERE potential_depth <= 12
     * </code>
     *
     * @param     mixed $potentialDepth The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByPotentialDepth($potentialDepth = null, $comparison = null)
    {
        if (is_array($potentialDepth)) {
            $useMinMax = false;
            if (isset($potentialDepth['min'])) {
                $this->addUsingAlias(CavesPeer::POTENTIAL_DEPTH, $potentialDepth['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($potentialDepth['max'])) {
                $this->addUsingAlias(CavesPeer::POTENTIAL_DEPTH, $potentialDepth['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CavesPeer::POTENTIAL_DEPTH, $potentialDepth, $comparison);
    }

    /**
     * Filter the query on the estimated_length column
     *
     * Example usage:
     * <code>
     * $query->filterByEstimatedLength(1234); // WHERE estimated_length = 1234
     * $query->filterByEstimatedLength(array(12, 34)); // WHERE estimated_length IN (12, 34)
     * $query->filterByEstimatedLength(array('min' => 12)); // WHERE estimated_length >= 12
     * $query->filterByEstimatedLength(array('max' => 12)); // WHERE estimated_length <= 12
     * </code>
     *
     * @param     mixed $estimatedLength The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByEstimatedLength($estimatedLength = null, $comparison = null)
    {
        if (is_array($estimatedLength)) {
            $useMinMax = false;
            if (isset($estimatedLength['min'])) {
                $this->addUsingAlias(CavesPeer::ESTIMATED_LENGTH, $estimatedLength['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($estimatedLength['max'])) {
                $this->addUsingAlias(CavesPeer::ESTIMATED_LENGTH, $estimatedLength['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CavesPeer::ESTIMATED_LENGTH, $estimatedLength, $comparison);
    }

    /**
     * Filter the query on the altitude column
     *
     * Example usage:
     * <code>
     * $query->filterByAltitude(1234); // WHERE altitude = 1234
     * $query->filterByAltitude(array(12, 34)); // WHERE altitude IN (12, 34)
     * $query->filterByAltitude(array('min' => 12)); // WHERE altitude >= 12
     * $query->filterByAltitude(array('max' => 12)); // WHERE altitude <= 12
     * </code>
     *
     * @param     mixed $altitude The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CavesQuery The current query, for fluid interface
     */
    public function filterByAltitude($altitude = null, $comparison = null)
    {
        if (is_array($altitude)) {
            $useMinMax = false;
            if (isset($altitude['min'])) {
                $this->addUsingAlias(CavesPeer::ALTITUDE, $altitude['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($altitude['max'])) {
                $this->addUsingAlias(CavesPeer::ALTITUDE, $altitude['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CavesPeer::ALTITUDE, $altitude, $comparison);
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
