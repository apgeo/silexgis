<?php
################################################################################   
## +---------------------------------------------------------------------------+
## | 1. Creating & Calling:                                                    | 
## +---------------------------------------------------------------------------+
##  *** only relative (virtual) path (to the current document)

  require_once("grid_common.php");
	
  echo "<b><h3>*{feature_types_page.page_title}*</h3></b>";
  ##  *** creating variables that we need for database connection 
  
  $DB_USER= DB_USER;    
  $DB_PASS= DB_PASS;
  $DB_HOST= DB_HOST;
  $DB_NAME= DB_NAME;

  $upload_path = "../assets/feature_symbols/";
  //$upload_path = WEBROOT."/assets/feature_symbols/";
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
  //$sql="SELECT points.id, X(coords) as lat, Y(coords) as lon, elevation, gpx_name, gpx_time, `_details`, points._id_point_type FROM points"; //.(!empty($filter_start_time_min) || !empty($filter_start_time_max) ? " WHERE 1 = 1 ".getSQLFilterString("time", $filter_start_time_min, $filter_start_time_max, "") : ""); 
   
  $sql="SELECT 	id, 	name, 	symbol_path, type FROM 	feature_types"; //.(!empty($filter_start_time_min) || !empty($filter_start_time_max) ? " WHERE 1 = 1 ".getSQLFilterString("time", $filter_start_time_min, $filter_start_time_max, "") : ""); 
   
##  *** set needed options
  $debug_mode = false;
  $messaging = true;
  $unique_prefix = "f_";  
  $dgrid = new DataGrid($debug_mode, $messaging, $unique_prefix, DATAGRID_DIR);
##  *** set data source with needed options
  $default_order_field = "feature_types.id";
  $default_order_type = "ASC";
  $dgrid->dataSource($db_conn, $sql, $default_order_field, $default_order_type);        

  
  
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
// To define inline editing, you have to define tabular layouts ("0" value) for view and Edit modes
 $layouts = array("view"=>0, "edit"=>0, "filter"=>1); 
// $layouts = array("view"=>0, "edit"=>1, "filter"=>1); 
 $dgrid->setLayouts($layouts);
##  *** set modes for operations ("type" => "link|button|image") 
##  *** "byFieldValue"=>"fieldName" - make the field to be a link to edit mode page
 $modes = array(
    "add"	 =>array("view"=>true, "edit"=>!false, "type"=>"link"),
    "edit"	 =>array("view"=>true, "edit"=>true,  "type"=>"link", "byFieldValue"=>""),
    "cancel"  =>array("view"=>true, "edit"=>true,  "type"=>"link"),
    "details" =>array("view"=>true, "edit"=>false, "type"=>"link"),
    "delete"  =>array("view"=>true, "edit"=>true,  "type"=>"image")
 );
 $dgrid->setModes($modes);
##  *** allow scrolling on datagrid
/// $scrolling_option = false;
/// $dgrid->allowScrollingSettings($scrolling_option);  
##  *** set scrolling settings (optional)
/// $scrolling_width = "90%";
/// $scrolling_height = "100%";
/// $dgrid->setScrollingSettings($scrolling_width, $scrolling_height);
##  *** allow mulirow operations
 $multirow_option = true;
 $dgrid->allowMultirowOperations($multirow_option);
 $multirow_operations = array(
    "delete"  => array("view"=>true),
    "details" => array("view"=>true)
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
    //"lat"      =>array("table"=>"points",   "field"=>"lat", "source"=>"self", "order"=>"DESC", "operator"=>true, "type"=>"dropdownlist", "case_sensitive"=>false,  "comparison_type"=>"binary"),
    //"lon"      =>array("table"=>"points",   "field"=>"lon", "source"=>"self", "order"=>"DESC", "operator"=>true, "type"=>"dropdownlist", "case_sensitive"=>false,  "comparison_type"=>"binary"),    
    "Name"      =>array("table"=>"feature_types",   "field"=>"name", "source"=>"self", "order"=>"DESC", "operator"=>true, "type"=>"textbox", "case_sensitive"=>false,  "default_operator"=>"like", "comparison_type"=>"string"),
    //"time"      =>array("table"=>"points",   "field"=>"gpx_time", "source"=>"self", "order"=>"DESC", "operator"=>true, "type"=>"textbox", "case_sensitive"=>false,  "comparison_type"=>"date"),
    //"Type"      =>array("table"=>"feature_types",   "field"=>"name", "source"=>"self", "operator"=>true, "type"=>"dropdownlist", "case_sensitive"=>false,  "comparison_type"=>"string"),    
    //"User ID"      =>array("table"=>"user_activity_reports",   "field"=>"user_id", "source"=>"self", "operator"=>true, "type"=>"textbox", "case_sensitive"=>false,  "comparison_type"=>"numeric"),
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
/*    "lat"  =>array("header"=>"lat", "type"=>"label", "align"=>"left", "width"=>"20px", "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal", "readonly"=>true),    
    "lon" =>array("header"=>"lon",     "type"=>"label", "align"=>"left", "width"=>"20px", "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal", "readonly"=>true),
    "elevation" => array("header"=>"altitudine", "type"=>"label", "align"=>"left", "width"=>"20px", "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal"), */   
    "name"  => array("header"=>"Nume",      "type"=>"label", "width"=>"20px", "align"=>"left",   "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal"),
    /*"gpx_time"  => array("header"=>"Data",      "type"=>"label", "width"=>"20px", "align"=>"left",   "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal"),
    "point_type"  => array("header"=>"Tip",      "type"=>"label", "width"=>"20px", "align"=>"left",   "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal"),
    "_details"  => array("header"=>"Detalii",      "type"=>"label", "width"=>"20px", "align"=>"left",   "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal"),
	*/
	//"view_on_map"  => array("header"=>"Map",      "type"=>"label", "width"=>"20px", "align"=>"left",   "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal", "summarize"=>false, "on_js_event"=>"", "target_path"=>"http://localhost/speogis/?long=23.49174&lat=43.20218" ),    
    //"pic"  =>array("header"=>"Pic",      "type"=>"link", "width"=>"20px", "align"=>"left",   "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal", "field_key"=>"pic", "field_key_1"=>"pic", "field_data"=>"x", "rel"=>"", "title"=>"", "target"=>"_self", "href"=>"{0}"),
    //"gallery_url"  =>array("header"=>"Pic",      "type"=>"link", "width"=>"20px", "align"=>"left",   "wrap"=>"nowrap", "text_length"=>"-1", "field_key"=>"gallery_url", "field_key_1"=>"gallery_url", "field_data"=>"{1}", "rel"=>"{0}", "title"=>"{0}", "target"=>"_self", "href"=>"{0}"),
    //"pic"=>array("header"=>"pic", "type"=>"image",      "align"=>"left", "width"=>"20px", "wrap"=>"wrap", "text_length"=>"-1", "field_key"=>"gallery_url", "target_path"=>"{0}", "default"=>"def", "image_width"=>"50px", "image_height"=>"50px", "linkto"=>"{0}", "magnify"=>"true", "magnify_type"=>"lightbox", "magnify_power"=>"2"),
	"symbol_path" => array("header"=>"Picture", "type"=>"image", "align"=>"center", "width"=>"", "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal", "summarize"=>false, "on_js_event"=>"", "target_path"=>"$upload_path", "default"=>"", "image_width"=>"17px", "image_height"=>"17px"),
	//"type"       =>array("header"=>"Type",          "type"=>"enum",     "source"=>$fill_from_array, "view_type"=>"dropdownlist",  "width"=>"139px", "req_type"=>"ri", "title"=>"Type"),	
	"type"   =>array("header"=>"Type",       "type"=>"label", "summarize"=>false, "align"=>"right",  "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal"),
  );
  
  $dgrid->setColumnsInViewMode($vm_columns);
  
  ////if(isset($_GET['f_mode']) && (($_GET['f_mode'] == "edit") || ($_GET['f_mode'] == "details"))){
  
## +---------------------------------------------------------------------------+
## | 7. Add/Edit/Details Mode settings:                                        | 
## +---------------------------------------------------------------------------+
##  ***  set settings for edit/details mode
  $table_name = "feature_types";
  $primary_key = "id";
  $condition = "";
  //$condition = "feature_types.id = ".$dgrid->f_rid." ";  
  
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
	   $fill_from_array = array("1"=>"point", "2"=>"linestring", "3"=>"polygon");
	   
     $em_columns = array(
         "name"  =>array("header"=>"Name",    "type"=>"textbox",  "width"=>"160px", "req_type"=>"rt", "title"=>"Name", "readonly"=>false, "view_type"=>"textbox"),      
         //"gpx_time"       =>array("header"=>"Time",       "type"=>"textbox",  "width"=>"140px", "req_type"=>"rt", "title"=>"Time", "readonly"=>false, "view_type"=>"textbox"),
		"symbol_path" => array("header"=>"Symbol image", "type"=>"image", "req_type"=>"st", "width"=>"210px", "title"=>"Symbol", "readonly"=>false, "maxlength"=>"-1", "default"=>"", "unique"=>false, "unique_condition"=>"", "on_js_event"=>"", "target_path"=>"$upload_path", "max_file_size"=>"100K", "image_width"=>"100px", "image_height"=>"100px", "file_name"=>"", "host"=>"local"),		   		
		"type"       =>array("header"=>"Type",          "type"=>"enum",     "source"=>$fill_from_array, "view_type"=>"dropdownlist",  "width"=>"139px", "req_type"=>"ri", "title"=>"Type"),
     );
	 
	 //"ForeignKey_2"=>array("table"=>"TableName_2", "field_key"=>"FieldKey_2", "field_name"=>"FieldName_2", "view_type"=>"dropdownlist(default)|radiobutton|textbox", "condition"=>"", "order_by_field"=>"Field_Name", "order_type"=>"ASC|DESC", "on_js_event"=>"")
      $dgrid->setColumnsInEditMode($em_columns);
    // ##  *** set auto-genereted columns in edit mode
     $auto_column_in_edit_mode = false;
     $dgrid->setAutoColumnsInEditMode($auto_column_in_edit_mode);
	 
	 //$dgrid->setAutoColumnsInEditMode(false); // $dgrid->setAutoColumnsInEditMode(true);
	 
    // ##  *** set foreign keys for add/edit/details modes (if there are linked tables)
    // ##  *** Ex.: "condition"=>"TableName_1.FieldName > 'a' AND TableName_1.FieldName < 'c'"
    // ##  *** Ex.: "on_js_event"=>"onclick='alert(\"Yes!!!\");'"
  
  $foreign_keys = array(
          "_id_point_type"=>array("table"=>"feature_types", "field_key"=>"id", "field_name"=>"name", "view_type"=>"dropdownbox", "condition"=>"")
         //"ForeignKey_2"=>array("table"=>"TableName_2", "field_key"=>"FieldKey_2", "field_name"=>"FieldName_2", "view_type"=>"dropdownlist(default)|radiobutton|textbox", "condition"=>"", "order_by_field"=>"Field_Name", "order_type"=>"ASC|DESC", "on_js_event"=>"")
      ); 
      $dgrid->setForeignKeysEdit($foreign_keys);

  ////}
## +---------------------------------------------------------------------------+
## | 8. Bind the DataGrid:                                                     | 
## +---------------------------------------------------------------------------+
##  *** set debug mode & messaging options  

  
  
    //////////////////////////////////////////////////////
    /*
    $conid = DB_Connect();
    DBCon::open_connection();
    
    $query = "select count(distinct(user_id)) as unique_points from user_activity_reports ".(!empty($filter_start_time_min) || !empty($filter_start_time_max) ? " WHERE 1 = 1 ".getSQLFilterString("time", $filter_start_time_min, $filter_start_time_max, "") : "");
    
    $db_result = DB_Execute($conid, $query, 1);      
    
    $unique_points = -1;
    
    if ($row = mysql_fetch_array($db_result, MYSQL_ASSOC))
        $unique_points = $row['unique_points'];

    echo "<br/>";

    echo "<div>";
    echo "<div style='float:left;padding-left:20px;'>";
        
    if (!empty($unique_points))
    {
        echo "Unique points: <b>".$unique_points."</b><br/>";
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

?>