<?php
	include_once("grid_common.php");
	//include_once(ROOTPATH."/user/grid_common.php");	
?>
<b><h3>*{files.page_title}*</h3></b>
<a class="btn btn-primary" href="<?=WEBROOT ?>/user/add_file.php" >*{files.add_file}*</a>
<a class="btn btn-primary" href="<?=WEBROOT ?>/user/add_multiple_files.php" >*{files.add_multiple_files}*</a>
<?php
################################################################################   
## +---------------------------------------------------------------------------+
## | 1. Creating & Calling:                                                    | 
## +---------------------------------------------------------------------------+
##  *** only relative (virtual) path (to the current document)

  //require_once("grid_common.php");

  ##  *** creating variables that we need for database connection 
  
  $DB_USER= DB_USER;
  $DB_PASS= DB_PASS;
  $DB_HOST= DB_HOST;       
  $DB_NAME= DB_NAME;

  /*define('USER_INTERFACE_TIMEZONE', '+00:00');  
  @$filter_start_time_min = $_REQUEST['filter_start_time_min'];
  @$filter_start_time_max = $_REQUEST['filter_start_time_max'];
  
   if (!empty($filter_start_time_max)) 
   {
        if (defined('USER_INTERFACE_TIMEZONE'))
            $filter_start_time_max = date( 'Y-m-d H:i:s', convert_to_timezone($filter_start_time_max, USER_INTERFACE_TIMEZONE));            
        else
            $filter_start_time_max = date( 'Y-m-d H:i:s', strtotime($filter_start_time_max));        
   }        

  if (!empty($filter_start_time_min)) 
   {
        if (defined('USER_INTERFACE_TIMEZONE'))
            $filter_start_time_min = date( 'Y-m-d H:i:s', convert_to_timezone($filter_start_time_min, USER_INTERFACE_TIMEZONE));            
        else
            $filter_start_time_min = date( 'Y-m-d H:i:s', strtotime($filter_start_time_min));
   }
   
   print ("<form name='mainform' action='view_add_user_activity_report.php' method='get' > <script type='text/javascript' src='datetimep/datetimepicker_css.js'></script>"); // action='feed_stats.php'  //("<form action='$PHP_SELF'>");
   
   echo "Time between: ";
   echo "<input id='filter_start_time_min' name='filter_start_time_min' type='text' size='25' value='$filter_start_time_min'> <a href=\"javascript: NewCssCal('filter_start_time_min','ddmmyyyy','dropdown',true)\"> <img src='datetimep/images/cal.gif' width='16' height='16' alt='Pick a date'></a>";   
   echo " and ";
   echo "<input id='filter_start_time_max' name='filter_start_time_max' type='text' size='25' value='$filter_start_time_max'> <a href=\"javascript: NewCssCal('filter_start_time_max','ddmmyyyy','dropdown',true)\"> <img src='datetimep/images/cal.gif' width='16' height='16' alt='Pick a date'></a>";
   echo " ";
   //echo "";
   //print("<INPUT TYPE=CHECKBOX ".($show_only_end_time_reached ? "checked='on'": "")." onclick=\" document.getElementsByName('submit')[0].click(); \" id='show_only_end_time_reached' name='show_only_end_time_reached' />");
   //echo "<br/>";

    
   print ("<input id='submitButton' type='submit' name='submit' value='Filter' />");
   */   

ob_start();
  $db_conn = DB::factory('mysql'); 
  $db_conn -> connect(DB::parseDSN('mysql://'.$DB_USER.':'.$DB_PASS.'@'.$DB_HOST.'/'.$DB_NAME));

##  *** put a primary key on the first place 
  //$sql="SELECT files.id, X(coords) as lat, Y(coords) as lon, elevation, gpx_name, gpx_time, `_details`, files._id_point_type FROM files"; //.(!empty($filter_start_time_min) || !empty($filter_start_time_max) ? " WHERE 1 = 1 ".getSQLFilterString("time", $filter_start_time_min, $filter_start_time_max, "") : ""); 
   //  WHERE caves.id = $cave_id
   
/*  $sql="SELECT files.id, file_name, user_id, files.add_time, files.file_type as file_type, size, md5_hash, geoobjects_to_files.id, t, username
	FROM files
	INNER JOIN geoobjects_to_files ON geoobjects_to_files.file_id = files.id
	INNER JOIN 
	(SELECT id, 'cave' AS t FROM caves
	 UNION
	 SELECT id, 'feature' AS t FROM features
	 ) AS geoobjects_vt ON geoobjects_to_files.geoobject_id = geoobjects_vt.id AND t = geoobjects_to_files.geoobject_type
	 INNER JOIN users ON users.id = files.user_id
 ";*/
  $sql="SELECT files.id, file_name, user_id, files.add_time, files.file_type as file_type, size, md5_hash, username FROM files
		INNER JOIN users ON users.id = files.user_id";
 
 // ORDER BY files.id
 //GROUP BY files.id 
  //.(!empty($filter_start_time_min) || !empty($filter_start_time_max) ? " WHERE 1 = 1 ".getSQLFilterString("time", $filter_start_time_min, $filter_start_time_max, "") : ""); 
   
##  *** set needed options
  $debug_mode = false;
  $messaging = true;
  $unique_prefix = "f_";  
  $dgrid = new DataGrid($debug_mode, $messaging, $unique_prefix, DATAGRID_DIR);
##  *** set data source with needed options
  $default_order_field = "id";//"files.id";
  $default_order_type = "ASC";
  $dgrid->dataSource($db_conn, $sql, $default_order_field, $default_order_type);        

  
{

// querstring: f_new $_GET['f_rid']

  // add new row into project_schedule table
$mode = (isset($_REQUEST[$unique_prefix.'mode'])) ? $_REQUEST[$unique_prefix.'mode'] : '';
$rid = (isset($_REQUEST[$unique_prefix.'rid'])) ? $_REQUEST[$unique_prefix.'rid'] : '';
$pid = (isset($_REQUEST[$unique_prefix.'pid'])) ? $_REQUEST[$unique_prefix.'pid'] : '';


  if(($mode == "update") && ($rid == "-1") ){//&& $dgrid->IsOperationCompleted()
  $_rid = $dgrid->GetCurrentId();
  $_rid = $dgrid->rid;
  //GetCurrentId()
  //var_dump("z: $last_insert_id");
   $sql = "INSERT INTO `geoobjects_to_files` (`file_id`, 	`geoobject_id`, 	`geoobject_type`	)	VALUES	('$_rid', 	'0', 	''	);";
   // $sql = "INSERT INTO project_schedule (project_id, task_id) VALUES (".$pid.",".$dgrid->rid.") ";
   
   $dSet = $dgrid->ExecuteSQL($sql);
   //mysql_query($sql);
   //var_dump("update");
   /*if(mysql_insert_id() > 0){
        echo "OK";
   }else{
       echo "Error!";
   }*/
}
} 
##
##
## +---------------------------------------------------------------------------+
## | 2. General Settings:                                                      | 
## +---------------------------------------------------------------------------+
##  *** set encoding and collation (default: utf8/utf8_unicode_ci)
 $dg_encoding = "utf8";
 $dg_collation = "utf8_unicode_ci";
 $dgrid->setEncoding($dg_encoding, $dg_collation);
##  *** set interface language (default - English)
##  *** (en) - English     (de) - German     (se) Swedish     (hr) - Bosnian/Croatian
##  *** (hu) - Hungarian   (es) - Espanol    (ca) - Catala    (fr) - Francais
##  *** (nl) - Netherlands/"Vlaams"(Flemish) (it) - Italiano  (pl) - Polish
##  *** (ch) - Chinese     (sr) - Serbian
 $dg_language = "en";  
 $dgrid->setInterfaceLang($dg_language);
##  *** set direction: "ltr" or "rtr" (default - "ltr")
 $direction = "ltr";
 $dgrid->setDirection($direction);
##  *** set layouts: 0 - tabular(horizontal) - default, 1 - columnar(vertical) 
 $layouts = array("view"=>0, "edit"=>1, "filter"=>1, "add"=>1); 
 $dgrid->setLayouts($layouts);
##  *** set modes for operations ("type" => "link|button|image") 
##  *** "byFieldValue"=>"fieldName" - make the field to be a link to edit mode page
/* $modes = array(
    "add"	 =>array("view"=>!true, "edit"=>!false, "type"=>"link"),
    "edit"	 =>array("view"=>!true, "edit"=>!true,  "type"=>"link", "byFieldValue"=>""),
    "cancel"  =>array("view"=>true, "edit"=>true,  "type"=>"link"),
    "details" =>array("view"=>!true, "edit"=>false, "type"=>"link"),
    "delete"  =>array("view"=>true, "edit"=>true,  "type"=>"image")
 );
 $dgrid->setModes($modes);*/
##  *** allow scrolling on datagrid
/// $scrolling_option = false;
/// $dgrid->allowScrollingSettings($scrolling_option);  
##  *** set scrolling settings (optional)
/// $scrolling_width = "90%";
/// $scrolling_height = "100%";
/// $dgrid->setScrollingSettings($scrolling_width, $scrolling_height);
##  *** allow mulirow operations
 $multirow_option = true;
 //$dgrid->allowMultirowOperations($multirow_option);
 $multirow_operations = array(
	"edit"	 => array("view"=>!true),	
	"delete"  => array("view"=>true),
    "details" => array("view"=>!true)
 );
 $dgrid->setMultirowOperations($multirow_operations);  
##  *** set CSS class for datagrid
##  *** "default" or "blue" or "gray" or "green" or your css file relative path with name
 $css_class = "default";
## "embedded" - use embedded classes, "file" - link external css file
 $css_type = "embedded"; 
 $dgrid->setCssClass($css_class, $css_type);
##  *** set variables that used to get access to the page (like: my_page.php?act=34&id=56 etc.) 
/// $http_get_vars = array("act", "id");
/// $dgrid->setHttpGetVars($http_get_vars);
##  *** set other datagrid/s unique prefixes (if you use few datagrids on one page)
##  *** format (in wich mode to allow processing of another datagrids)
##  *** array("unique_prefix"=>array("view"=>true|false, "edit"=>true|false, "details"=>true|false));
 $anotherDatagrids = array("fp_"=>array("view"=>false, "edit"=>true, "details"=>true));
 $dgrid->setAnotherDatagrids($anotherDatagrids);  

 
  $paging_option = true;
  $rows_numeration = false;
  $numeration_sign = "N #";
  $dropdown_paging = true;

  $dgrid->AllowPaging($paging_option, $rows_numeration, $numeration_sign, $dropdown_paging);
  
  $bottom_paging = array("results"=>true, "results_align"=>"left", "pages"=>true, "pages_align"=>"center", "page_size"=>true, "page_size_align"=>"right");
  $top_paging = array();
  
  $pages_array = array("25"=>"25", "50"=>"50", "100"=>"100", "250"=>"250", "500"=>"500");
  $default_page_size = empty($f_page_size) ? 100 : $f_page_size; // echo "default_page_size $default_page_size";
  $paging_arrows = array("first"=>"|&lt;&lt;", "previous"=>"&lt;&lt;", "next"=>"&gt;&gt;", "last"=>"&gt;&gt;|");
  $dgrid->SetPagingSettings($bottom_paging, $top_paging, $pages_array, $default_page_size, $paging_arrows);

 ##  *** set DataGrid caption
 /*$dg_caption = '
    <table ><tr valign="center"><td align="right"><b>My Favorite Lovely PHP DataGrid</b>&nbsp;</td>
    <td align="left"><a href="http://jigsaw.w3.org/css-validator/validator?uri=http://phpbuilder.awardspace.com/dg_4xx.php">
     <img style="border:0;width:88px;height:31px"
          src="http://jigsaw.w3.org/css-validator/images/vcss" 
          alt="Valid CSS!" />
    </a></td></tr></table>';
 $dgrid->setCaption($dg_caption);
  */
##
##
## +---------------------------------------------------------------------------+
## | 5. Filter Settings:                                                       | 
## +---------------------------------------------------------------------------+
##  *** set filtering option: true or false(default)
 $filtering_option = true;
 $dgrid->allowFiltering($filtering_option);
##  *** set aditional filtering settings
  //$fill_from_array = array("10000"=>"10000", "250000"=>"250000", "5000000"=>"5000000", "25000000"=>"25000000", "100000000"=>"100000000");
  $filtering_fields = array(
    //"Browser title"     =>array("table"=>"user_activity_reports", "field"=>"browser_title", "source"=>"self", "operator"=>true, "default_operator"=>"like", "type"=>"textbox", "case_sensitive"=>true,  "comparison_type"=>"string"),
    //"lat"      =>array("table"=>"files",   "field"=>"lat", "source"=>"self", "order"=>"DESC", "operator"=>true, "type"=>"dropdownlist", "case_sensitive"=>false,  "comparison_type"=>"binary"),
    //"lon"      =>array("table"=>"files",   "field"=>"lon", "source"=>"self", "order"=>"DESC", "operator"=>true, "type"=>"dropdownlist", "case_sensitive"=>false,  "comparison_type"=>"binary"),
    "User"      =>array("table"=>"users",   "field"=>"username", "source"=>"self", "order"=>"DESC", "operator"=>true, "type"=>"dropdownlist", "case_sensitive"=>false,  "comparison_type"=>"binary"),
    "Name"      =>array("table"=>"files",   "field"=>"file_name", "source"=>"self", "order"=>"DESC", "operator"=>true, "type"=>"textbox", "case_sensitive"=>false,  "default_operator"=>"like", "comparison_type"=>"string"),
    "Time"      =>array("table"=>"files",   "field"=>"add_time", "source"=>"self", "order"=>"DESC", "operator"=>true, "type"=>"textbox", "case_sensitive"=>false,  "comparison_type"=>"date"),
    //"Type"      =>array("table"=>"files",   "field"=>"file_type", "source"=>"self", "operator"=>true, "type"=>"dropdownlist", "case_sensitive"=>false,  "comparison_type"=>"string"),    
    "Size"      =>array("table"=>"files",   "field"=>"size", "source"=>"self", "operator"=>true, "type"=>"textbox", "case_sensitive"=>false,  "comparison_type"=>"numeric"),
    //"Date"        =>array("table"=>"user_activity_reports", "field"=>"time", "source"=>"self", "operator"=>true, "type"=>"textbox", "case_sensitive"=>false,  "comparison_type"=>"string"),      
    //"Population"  =>array("table"=>"countries", "field"=>"population", "source"=>$fill_from_array, "order"=>"DESC", "operator"=>true, "type"=>"dropdownlist", "case_sensitive"=>false, "comparison_type"=>"numeric")	
  );
  
  $dgrid->setFieldsFiltering($filtering_fields);
  
## +---------------------------------------------------------------------------+
## | 6. View Mode Settings:                                                    | 
## +---------------------------------------------------------------------------+
##  *** set columns in view mode
  $dgrid->setAutoColumnsInViewMode(false);  

    $vm_columns = array(   
    
    "file_name" => array("header"=>"Name", "type"=>"label", "align"=>"left", "width"=>"20px", "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal", "readonly"=>true),
    "add_time"  => array("header"=>"Time", "type"=>"label", "align"=>"left", "width"=>"20px", "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal", "readonly"=>true),
    "file_type" => array("header"=>"Type", "type"=>"label", "align"=>"left", "width"=>"20px", "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal", "readonly"=>true),
	"size" => array("header"=>"Size", "type"=>"label", "align"=>"left", "width"=>"20px", "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal", "readonly"=>true),
    "username"  => array("header"=>"User", "type"=>"label", "align"=>"left", "width"=>"20px", "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal", "readonly"=>true),
  );
  
  $dgrid->setColumnsInViewMode($vm_columns);
  
  ////if(isset($_GET['f_mode']) && (($_GET['f_mode'] == "edit") || ($_GET['f_mode'] == "details"))){
  
## +---------------------------------------------------------------------------+
## | 7. Add/Edit/Details Mode settings:                                        | 
## +---------------------------------------------------------------------------+
##  ***  set settings for edit/details mode
  $table_name = "files";
  $primary_key = "id";
  $condition = "";
  //$condition = "files.id = ".$dgrid->f_rid." ";  
  
     $em_table_properties = array("width"=>"70%");
     $dgrid->setEditModeTableProperties($em_table_properties);
    // ##  *** set details mode table properties
      $dm_table_properties = array("width"=>"70%");
      $dgrid->setDetailsModeTableProperties($dm_table_properties);
    // ##  ***  set settings for add/edit/details modes
      
      $dgrid->setTableEdit($table_name, $primary_key, $condition);
	  
    // ##  *** set columns in edit mode
    // ##  *** first letter: r - required, s - simple (not required)
    // ##  *** second letter: t - text(including datetime), n - numeric, a - alphanumeric, e - email, f - float, y - any, l - login name, z - zipcode, p - password, i - integer, v - verified
    // ##  *** third letter (optional): 
    // ##          for numbers: s - signed, u - unsigned, p - positive, n - negative
    // ##          for strings: u - upper,  l - lower,    n - normal,   y - any
    // ##  *** Ex.: "on_js_event"=>"onclick='alert(\"Yes!!!\");'"
    // ##  *** Ex.: type = textbox|textarea|label|date(yyyy-mm-dd)|datedmy(dd-mm-yyyy)|datetime(yyyy-mm-dd hh:mm:ss)|datetimedmy(dd-mm-yyyy hh:mm:ss)|image|password|enum|print|checkbox
    // ##  *** make sure your WYSIWYG dir has 777 permissions
    // $fill_from_array = array("0"=>"No", "1"=>"Yes", "2"=>"Don't know", "3"=>"My be"); /* as "value"=>"option" */
	
     $em_columns = array(
	 
		// "lat"  =>array("header"=>"Lat",    "type"=>"textbox",  "width"=>"160px", "req_type"=>"rf", "title"=>"Lat", "readonly"=>!false /*"readonly"=>true*/),      
		// "long"  =>array("header"=>"Lon",    "type"=>"textbox",  "width"=>"160px", "req_type"=>"rf", "title"=>"Lon", "readonly"=>!false /*"readonly"=>true*/),      	 
          "file_name"  =>array("header"=>"Name",    "type"=>"textbox",  "width"=>"160px", "req_type"=>"rt", "title"=>"Name", "readonly"=>false, "view_type"=>"textbox"),      
		 
         // //"gpx_time"       =>array("header"=>"Time",       "type"=>"textbox",  "width"=>"140px", "req_type"=>"rt", "title"=>"Time", "readonly"=>false, "view_type"=>"textbox"),
         // "_id_point_type"  =>array("header"=>"Tip", "type"=>"enum",     "req_type"=>"st", "width"=>"210px", "title"=>"Type", "readonly"=>false, "maxlength"=>"-1", "default"=>"", "unique"=>false, "unique_condition"=>"", "on_js_event"=>"alert('x');", "source"=>"self", "view_type"=>"dropdownlist"),
		 // "_details"  =>array("header"=>"Detalii",    "type"=>"textarea",  "width"=>"160px", "req_type"=>"st", "title"=>"Detalii", "readonly"=>false, "view_type"=>"textarea",  "rows"=>"7", "cols"=>"50"),      
     );
	 
	 //"ForeignKey_2"=>array("table"=>"TableName_2", "field_key"=>"FieldKey_2", "field_name"=>"FieldName_2", "view_type"=>"dropdownlist(default)|radiobutton|textbox", "condition"=>"", "order_by_field"=>"Field_Name", "order_type"=>"ASC|DESC", "on_js_event"=>"")
      //zz $dgrid->setColumnsInEditMode($em_columns);
    // ##  *** set auto-genereted columns in edit mode
     //$auto_column_in_edit_mode = false;
     //$dgrid->setAutoColumnsInEditMode($auto_column_in_edit_mode);
	 
	$dgrid->setAutoColumnsInEditMode(!false); // $dgrid->setAutoColumnsInEditMode(true);
	 
	    $modes = array(
    "add"     =>array("view"=>0, "edit"=>1, "type"=>"link"),
	"edit"     =>array("view"=>0, "edit"=>0,  "type"=>"link", "byFieldValue"=>""),
    "cancel"  =>array("view"=>true, "edit"=>true,  "type"=>"link"),
    "delete"  =>array("view"=>true, "edit"=>true,  "type"=>"image")
 );
$dgrid->setModes($modes);
    // ##  *** set foreign keys for add/edit/details modes (if there are linked tables)
    // ##  *** Ex.: "condition"=>"TableName_1.FieldName > 'a' AND TableName_1.FieldName < 'c'"
    // ##  *** Ex.: "on_js_event"=>"onclick='alert(\"Yes!!!\");'"
  /*
	$foreign_keys = array(
          //"_id_point_type"=>array("table"=>"feature_types", "field_key"=>"id", "field_name"=>"name", "view_type"=>"dropdownbox", "condition"=>"")
         //"ForeignKey_2"=>array("table"=>"TableName_2", "field_key"=>"FieldKey_2", "field_name"=>"FieldName_2", "view_type"=>"dropdownlist(default)|radiobutton|textbox", "condition"=>"", "order_by_field"=>"Field_Name", "order_type"=>"ASC|DESC", "on_js_event"=>"")
      ); 
      $dgrid->setForeignKeysEdit($foreign_keys);
*/
  ////}
## +---------------------------------------------------------------------------+
## | 8. Bind the DataGrid:                                                     | 
## +---------------------------------------------------------------------------+
##  *** set debug mode & messaging options  

  
  
    //////////////////////////////////////////////////////
    /*
    $conid = DB_Connect();
    DBCon::open_connection();
    
    $query = "select count(distinct(user_id)) as unique_files from user_activity_reports ".(!empty($filter_start_time_min) || !empty($filter_start_time_max) ? " WHERE 1 = 1 ".getSQLFilterString("time", $filter_start_time_min, $filter_start_time_max, "") : "");
    
    $db_result = DB_Execute($conid, $query, 1);      
    
    $unique_files = -1;
    
    if ($row = mysql_fetch_array($db_result, MYSQL_ASSOC))
        $unique_files = $row['unique_files'];

    echo "<br/>";

    echo "<div>";
    echo "<div style='float:left;padding-left:20px;'>";
        
    if (!empty($unique_files))
    {
        echo "Unique files: <b>".$unique_files."</b><br/>";
    }
    
    echo "</div>";
    echo "</div>";

    DBCon::close_connection();
    */

    $dgrid->bind();        
    
            
    // echo "</form>";
	
	ob_end_flush();
################################################################################

/*    function getSQLFilterString($field, $min_value, $max_value, $equal_value)    
    {
        $sqlString = "";
                
        // if (empty($value))
        //    return "";
        if (!empty($min_value) && !empty($equal_value))
            throw new Exception("Cannot set both equal and min value");
            
        if (!empty($max_value) && !empty($equal_value))
            throw new Exception("Cannot set both equal and max value");

                
        if (!empty($max_value) && is_string($max_value))
            $max_value = "'$max_value'";

        if (!empty($min_value) && is_string($min_value))
            $min_value = "'$min_value'";
        
        if (!empty($equal_value) && is_string($equal_value))
            $equal_value = "'$equal_value'";


        if (!empty($max_value))
            $sqlString .= " AND $field <= $max_value";
            
        if (!empty($min_value))Nume
            $sqlString .= " AND $field >= $min_value";
            
        if (!empty($equal_value))
            $sqlString .= " AND $field = $equal_value";
            
        return $sqlString;
    }
*/
    function convert_to_timezone($date_str, $timezone)
    {
        $offset = 0;
        $pos = strpos($timezone, ':');
        
        if ($pos === false)
            $offset = intval($timezone) * 3600;
        else
        {
            $timezone = substr($timezone, 0, $pos);            
            $offset = $timezone * 3600;    
        }
                
        return  strtotime($date_str) + $offset;
    }
?>