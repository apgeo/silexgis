<?php



/**
 * This class defines the structure of the 'trip_logs' table.
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
class TripLogsTableMap extends TableMap
{

    /**
     * The (dot-path) name of this class
     */
    const CLASS_NAME = 'speogis.map.TripLogsTableMap';

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
        $this->setName('trip_logs');
        $this->setPhpName('TripLogs');
        $this->setClassname('TripLogs');
        $this->setPackage('speogis');
        $this->setUseIdGenerator(true);
        // columns
        $this->addPrimaryKey('id', 'Id', 'BIGINT', true, null, null);
        $this->addColumn('add_time', 'AddTime', 'TIMESTAMP', false, null, null);
        $this->addColumn('trip_start_time', 'TripStartTime', 'TIMESTAMP', false, null, null);
        $this->addColumn('trip_end_time', 'TripEndTime', 'TIMESTAMP', false, null, null);
        $this->addColumn('details', 'Details', 'LONGVARCHAR', false, null, null);
        $this->addColumn('target_zone', 'TargetZone', 'VARCHAR', false, 90, null);
        $this->addColumn('type', 'Type', 'VARCHAR', false, 50, null);
        $this->addColumn('temporary', 'Temporary', 'VARCHAR', false, 1, null);
        $this->addColumn('summary', 'Summary', 'VARCHAR', false, 500, null);
        // validators
    } // initialize()

    /**
     * Build the RelationMap objects for this table relationships
     */
    public function buildRelations()
    {
    } // buildRelations()

} // TripLogsTableMap
