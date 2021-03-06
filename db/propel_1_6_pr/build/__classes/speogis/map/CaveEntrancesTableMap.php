<?php



/**
 * This class defines the structure of the 'cave_entrances' table.
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
class CaveEntrancesTableMap extends TableMap
{

    /**
     * The (dot-path) name of this class
     */
    const CLASS_NAME = 'speogis.map.CaveEntrancesTableMap';

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
        $this->setName('cave_entrances');
        $this->setPhpName('CaveEntrances');
        $this->setClassname('CaveEntrances');
        $this->setPackage('speogis');
        $this->setUseIdGenerator(true);
        // columns
        $this->addPrimaryKey('id', 'Id', 'BIGINT', true, null, null);
        $this->addColumn('name', 'Name', 'VARCHAR', false, 100, null);
        $this->addColumn('point_id', 'PointId', 'BIGINT', false, null, null);
        $this->addColumn('entranceType', 'Entrancetype', 'BIGINT', true, null, null);
        $this->addColumn('description', 'Description', 'VARCHAR', false, 2000, null);
        $this->addColumn('is_main_entrance', 'IsMainEntrance', 'VARCHAR', false, 1, null);
        $this->addColumn('hydrologic_type', 'HydrologicType', 'BIGINT', false, null, null);
        $this->addColumn('cave_id', 'CaveId', 'BIGINT', true, null, null);
        // validators
    } // initialize()

    /**
     * Build the RelationMap objects for this table relationships
     */
    public function buildRelations()
    {
    } // buildRelations()

} // CaveEntrancesTableMap
