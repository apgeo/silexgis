<?php



/**
 * This class defines the structure of the 'points' table.
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
class PointsTableMap extends TableMap
{

    /**
     * The (dot-path) name of this class
     */
    const CLASS_NAME = 'speogis.map.PointsTableMap';

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
        $this->setName('points');
        $this->setPhpName('Points');
        $this->setClassname('Points');
        $this->setPackage('speogis');
        $this->setUseIdGenerator(true);
        // columns
        $this->addPrimaryKey('id', 'Id', 'BIGINT', true, null, null);
        $this->addColumn('lat', 'Lat', 'DOUBLE', false, 9, null);
        $this->addColumn('long', 'Long', 'DOUBLE', false, 9, null);
        $this->addColumn('elevation', 'Elevation', 'INTEGER', false, null, null);
        $this->addColumn('coords', 'Coords', 'VARCHAR', true, null, null);
        $this->addColumn('gpx_name', 'GpxName', 'VARCHAR', false, 500, null);
        $this->addColumn('gpx_sym', 'GpxSym', 'VARCHAR', false, 50, null);
        $this->addColumn('gpx_type', 'GpxType', 'VARCHAR', false, 50, null);
        $this->addColumn('gpx_cmt', 'GpxCmt', 'VARCHAR', false, 500, null);
        $this->addColumn('gpx_sat', 'GpxSat', 'INTEGER', false, null, null);
        $this->addColumn('gpx_fix', 'GpxFix', 'VARCHAR', false, 8, null);
        $this->addColumn('gpx_time', 'GpxTime', 'TIMESTAMP', false, null, null);
        $this->addColumn('_type', 'Type', 'INTEGER', false, null, null);
        $this->addColumn('_details', 'Details', 'VARCHAR', false, 5000, null);
        $this->addColumn('added_by_user_id', 'AddedByUserId', 'BIGINT', true, null, null);
        $this->addColumn('add_time', 'AddTime', 'TIMESTAMP', true, null, null);
        $this->addColumn('_id_point_type', 'IdPointType', 'BIGINT', false, null, null);
        // validators
    } // initialize()

    /**
     * Build the RelationMap objects for this table relationships
     */
    public function buildRelations()
    {
    } // buildRelations()

} // PointsTableMap
