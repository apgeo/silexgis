<?php
################################################################################   
## +---------------------------------------------------------------------------+
## | 1. Creating & Calling:                                                    | 
## +---------------------------------------------------------------------------+
##  *** only relative (virtual) path (to the current document)
  define ("DATAGRID_DIR", "../../datagrid_class_4_2_8/");
  define ("PEAR_DIR", "../../datagrid_class_4_2_8/pear/");
  
  require_once(DATAGRID_DIR.'datagrid.class.php');
  require_once(PEAR_DIR.'PEAR.php');
  require_once(PEAR_DIR.'DB.php');

		@error_reporting(E_ALL ^ E_DEPRECATED); //error_reporting(E_ALL);
  ##  *** creating variables that we need for database connection 
  $DB_USER='root';            
  $DB_PASS='';           
  $DB_HOST='localhost';       
  $DB_NAME='test_datagrid_db';    

ob_start();
  $db_conn = DB::factory('mysql'); 
  $db_conn -> connect(DB::parseDSN('mysql://'.$DB_USER.':'.$DB_PASS.'@'.$DB_HOST.'/'.$DB_NAME));

 

##  *** put a primary key on the first place 
  $sql=" SELECT "  
   ."countries.id, "
   ."countries.name, "
   ."countries.description, "
   ."countries.picture_url, "
   ."FORMAT(countries.population, 0) as population, "   
   ." CASE WHEN countries.is_democracy = 1 THEN 'Yes' ELSE 'No' END as is_democracy "
   ."FROM countries ";
   
##  *** set needed options
  $debug_mode = false;
  $messaging = true;
  $unique_prefix = "f_";  
  $dgrid = new DataGrid($debug_mode, $messaging, $unique_prefix, DATAGRID_DIR);
##  *** set data source with needed options
  $default_order_field = "name";
  $default_order_type = "ASC";
  $dgrid->dataSource($db_conn, $sql, $default_order_field, $default_order_type);	    

## +---------------------------------------------------------------------------+
## | 6. View Mode Settings:                                                    | 
## +---------------------------------------------------------------------------+
##  *** set columns in view mode
   $dgrid->setAutoColumnsInViewMode(true);  

## +---------------------------------------------------------------------------+
## | 7. Add/Edit/Details Mode settings:                                        | 
## +---------------------------------------------------------------------------+
##  ***  set settings for edit/details mode
  $table_name = "countries";
  $primary_key = "id";
  $condition = "";
  $dgrid->setTableEdit($table_name, $primary_key, $condition);
  $dgrid->setAutoColumnsInEditMode(true);
  
## +---------------------------------------------------------------------------+
## | 8. Bind the DataGrid:                                                     | 
## +---------------------------------------------------------------------------+
##  *** set debug mode & messaging options
    $dgrid->bind();        
    ob_end_flush();
################################################################################   
 ?>