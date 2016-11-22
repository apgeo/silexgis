<?php



/**
 * This class defines the structure of the 'files' table.
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
class FilesTableMap extends TableMap
{

    /**
     * The (dot-path) name of this class
     */
    const CLASS_NAME = 'speogis.map.FilesTableMap';

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
        $this->setName('files');
        $this->setPhpName('Files');
        $this->setClassname('Files');
        $this->setPackage('speogis');
        $this->setUseIdGenerator(true);
        // columns
        $this->addPrimaryKey('id', 'Id', 'BIGINT', true, null, null);
        $this->addColumn('file_name', 'FileName', 'VARCHAR', true, 255, null);
        $this->addColumn('id_user', 'IdUser', 'BIGINT', true, null, null);
        $this->addColumn('add_time', 'AddTime', 'TIMESTAMP', true, null, null);
        $this->addColumn('type', 'Type', 'CHAR', false, null, null);
        $this->getColumn('type', false)->setValueSet(array (
  0 => 'GPX',
  1 => 'KML',
  2 => 'undefined',
));
        $this->addColumn('size', 'Size', 'INTEGER', true, null, null);
        $this->addColumn('md5_hash', 'Md5Hash', 'VARCHAR', false, 50, null);
        // validators
    } // initialize()

    /**
     * Build the RelationMap objects for this table relationships
     */
    public function buildRelations()
    {
    } // buildRelations()

} // FilesTableMap
