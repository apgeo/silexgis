<?php



/**
 * This class defines the structure of the 'caves' table.
 *
 *
 *
 * This map class is used by Propel to do runtime db structure discovery.
 * For example, the createSelectSql() method checks the type of a given column used in an
 * ORDER BY clause to know whether it needs to apply SQL to make the ORDER BY case-insensitive
 * (i.e. if it's a text column type).
 *
 * @package    propel.generator.speogis.map
 */
class CavesTableMap extends TableMap
{

    /**
     * The (dot-path) name of this class
     */
    const CLASS_NAME = 'speogis.map.CavesTableMap';

    /**
     * Initialize the table attributes, columns and validators
     * Relations are not initialized by this method since they are lazy loaded
     *
     * @return void
     * @throws PropelException
     */
    public function initialize()
    {
        // attributes
        $this->setName('caves');
        $this->setPhpName('Caves');
        $this->setClassname('Caves');
        $this->setPackage('speogis');
        $this->setUseIdGenerator(true);
        // columns
        $this->addPrimaryKey('id', 'Id', 'BIGINT', true, null, null);
        $this->addColumn('name', 'Name', 'VARCHAR', true, 100, null);
        $this->addColumn('type_id', 'TypeId', 'BIGINT', true, null, null);
        $this->addColumn('identification_code', 'IdentificationCode', 'VARCHAR', false, 50, null);
        $this->addColumn('description', 'Description', 'LONGVARCHAR', false, null, null);
        $this->addColumn('user_id', 'UserId', 'BIGINT', true, null, null);
        $this->addColumn('other_toponyms', 'OtherToponyms', 'VARCHAR', false, 250, null);
        $this->addColumn('rock_type_id', 'RockTypeId', 'BIGINT', false, null, null);
        $this->addColumn('rock_age', 'RockAge', 'VARCHAR', false, 50, null);
        $this->addColumn('hydrographic_basin', 'HydrographicBasin', 'VARCHAR', false, 100, null);
        $this->addColumn('valley', 'Valley', 'VARCHAR', false, 50, null);
        $this->addColumn('tributary_river', 'TributaryRiver', 'VARCHAR', false, 50, null);
        $this->addColumn('closest_address', 'ClosestAddress', 'VARCHAR', false, 200, null);
        $this->addColumn('is_show_cave', 'IsShowCave', 'VARCHAR', false, 1, null);
        $this->addColumn('show_cave_length', 'ShowCaveLength', 'SMALLINT', false, null, null);
        $this->addColumn('website', 'Website', 'VARCHAR', false, 255, null);
        $this->addColumn('land_registry_number', 'LandRegistryNumber', 'VARCHAR', false, 50, null);
        $this->addColumn('region', 'Region', 'VARCHAR', false, 50, null);
        $this->addColumn('depth', 'Depth', 'SMALLINT', false, null, null);
        $this->addColumn('surveyed_length', 'SurveyedLength', 'SMALLINT', false, 9, null);
        $this->addColumn('discovery_date', 'DiscoveryDate', 'VARCHAR', false, 50, null);
        $this->addColumn('discoverer', 'Discoverer', 'VARCHAR', false, 50, null);
        $this->addColumn('volume', 'Volume', 'INTEGER', false, null, null);
        $this->addColumn('positive_depth', 'PositiveDepth', 'SMALLINT', false, null, null);
        $this->addColumn('negative_depth', 'NegativeDepth', 'SMALLINT', false, null, null);
        $this->addColumn('ramification_index', 'RamificationIndex', 'TINYINT', false, null, null);
        $this->addColumn('real_extension', 'RealExtension', 'SMALLINT', false, 9, null);
        $this->addColumn('cave_age', 'CaveAge', 'INTEGER', false, null, null);
        $this->addColumn('projected_extension', 'ProjectedExtension', 'SMALLINT', false, 9, null);
        $this->addColumn('exploration_status', 'ExplorationStatus', 'CHAR', false, null, null);
        $this->getColumn('exploration_status', false)->setValueSet(array (
  0 => 'Unknown',
  1 => 'Not explored',
  2 => 'Partially explored',
  3 => 'Exploration finished',
));
        $this->addColumn('protection_class', 'ProtectionClass', 'VARCHAR', false, 20, null);
        $this->addColumn('potential_depth', 'PotentialDepth', 'SMALLINT', false, null, null);
        // validators
    } // initialize()

    /**
     * Build the RelationMap objects for this table relationships
     */
    public function buildRelations()
    {
    } // buildRelations()

} // CavesTableMap
