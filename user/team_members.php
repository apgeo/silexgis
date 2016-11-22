<?php
################################################################################   
## +---------------------------------------------------------------------------+
## | 1. Creating & Calling:                                                    | 
## +---------------------------------------------------------------------------+
##  *** only relative (virtual) path (to the current document)

	require_once("grid_common.php");  //require_once('utilities.php'); include_once 'db_interface.php'; include_once 'data_interface.php'; require_once 'languages.php'; 

  //echo "<b><h2>Users</h2></b>";
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
  //$sql = "select `id`, 	`first_name`, 	`last_name`, 	`nickname`, 	`group_id`, 	`picture_file_name`, 	`add_time`, 	`description`, 	`email`, 	`phone_number`, 	`notes`, 	`connected_user_id` from team_members";
  $sql = "select team_members.id, 	`first_name`, 	`last_name`, 	`nickname`, 	`group_id`, 	`picture_file_name`, 	`add_time`, 	`description`, 	`email`, 	`phone_number`, 	`notes`, 	`connected_user_id`,
  COUNT(trip_logs_to_team_members.id) as member_trip_count,
  ROUND((COUNT(trip_logs_to_team_members.id) * 100) / (SELECT COUNT(trip_logs.id) FROM trip_logs WHERE trip_logs.temporary != 1), 2) as member_trip_percentage
FROM team_members
INNER JOIN trip_logs_to_team_members ON trip_logs_to_team_members.id_team_member = team_members.id
WHERE 1=1
GROUP BY team_members.id ;
  ";
  
  //.(!empty($filter_start_time_min) || !empty($filter_start_time_max) ? " WHERE 1 = 1 ".getSQLFilterString("time", $filter_start_time_min, $filter_start_time_max, "") : ""); 
   //." CASE WHEN countries.is_democracy = 1 THEN 'Yes' ELSE 'No' END as is_democracy "   
   
##  *** set needed options
  $debug_mode = !false;
  $messaging = true;
  $unique_prefix = "f_";  
  $dgrid = new DataGrid($debug_mode, $messaging, $unique_prefix, DATAGRID_DIR);
##  *** set data source with needed options
  $default_order_field = "team_members.id";
  $default_order_type = "ASC";
  $dgrid->dataSource($db_conn, $sql, $default_order_field, $default_order_type);        

##
##
## +---------------------------------------------------------------------------+
## | 5. Filter Settings:                                                       | 
## +---------------------------------------------------------------------------+
##  *** set filtering option: true or false(default)
 $filtering_option = true;
 
 // enable for filtering --------------------
 //$dgrid->allowFiltering($filtering_option);
 
##  *** set aditional filtering settings
  //$fill_from_array = array("10000"=>"10000", "250000"=>"250000", "5000000"=>"5000000", "25000000"=>"25000000", "100000000"=>"100000000");
  $filtering_fields = array(
    //"Browser title"     =>array("table"=>"team_members", "field"=>"browser_title", "source"=>"self", "operator"=>true, "default_operator"=>"like", "type"=>"textbox", "case_sensitive"=>true,  "comparison_type"=>"string"),
    //"First name"        =>array("table"=>"team_members", "field"=>"first_name", "source"=>"self", "operator"=>true, "type"=>"textbox", "case_sensitive"=>false,  "comparison_type"=>"string"),      
	//"Last name"        =>array("table"=>"team_members", "field"=>"last_name", "source"=>"self", "operator"=>true, "type"=>"textbox", "case_sensitive"=>false,  "comparison_type"=>"string"),      
	//"Nickname"        =>array("table"=>"team_members", "field"=>"nickname", "source"=>"self", "operator"=>true, "type"=>"textbox", "case_sensitive"=>false,  "comparison_type"=>"string"),      
	//"Email"        =>array("table"=>"team_members", "field"=>"email", "source"=>"self", "operator"=>true, "type"=>"textbox", "case_sensitive"=>false,  "comparison_type"=>"string"),      
	//"Notes"        =>array("table"=>"team_members", "field"=>"notes", "source"=>"self", "operator"=>true, "type"=>"textbox", "case_sensitive"=>false,  "comparison_type"=>"string"),
    //""      =>array("table"=>"users",   "field"=>"password", "source"=>"self", "order"=>"DESC", "operator"=>true, "type"=>"dropdownlist", "case_sensitive"=>false,  "comparison_type"=>"binary"),
    //"Email"      =>array("table"=>"users",   "field"=>"email", "source"=>"self", "order"=>"DESC", "operator"=>true, "type"=>"dropdownlist", "case_sensitive"=>false,  "comparison_type"=>"binary"),
    //"OS title"      =>array("table"=>"team_members",   "field"=>"os_title", "source"=>"self", "order"=>"DESC", "operator"=>true, "type"=>"dropdownlist", "case_sensitive"=>false,  "comparison_type"=>"binary"),
    //"OS version"      =>array("table"=>"team_members",   "field"=>"os_version", "source"=>"self", "order"=>"DESC", "operator"=>true, "type"=>"dropdownlist", "case_sensitive"=>false,  "comparison_type"=>"binary"),
    //"IP"      =>array("table"=>"team_members",   "field"=>"user_ip", "source"=>"self", "operator"=>true, "type"=>"textbox", "case_sensitive"=>false,  "comparison_type"=>"numeric"),    
    //"User ID"      =>array("table"=>"team_members",   "field"=>"user_id", "source"=>"self", "operator"=>true, "type"=>"textbox", "case_sensitive"=>false,  "comparison_type"=>"numeric"),
    
    //"Population"  =>array("table"=>"countries", "field"=>"population", "source"=>$fill_from_array, "order"=>"DESC", "operator"=>true, "type"=>"dropdownlist", "case_sensitive"=>false, "comparison_type"=>"numeric")
  );
  
  $dgrid->setFieldsFiltering($filtering_fields);
  
## +---------------------------------------------------------------------------+
## | 6. View Mode Settings:                                                    | 
## +---------------------------------------------------------------------------+
##  *** set columns in view mode
  $dgrid->setAutoColumnsInViewMode(true);  

## +---------------------------------------------------------------------------+
## | 7. Add/Edit/Details Mode settings:                                        | 
## +---------------------------------------------------------------------------+
##  ***  set settings for edit/details mode
  $table_name = "team_members";
  $primary_key = "id";
  $condition = "";
  $dgrid->setTableEdit($table_name, $primary_key, $condition);
  //$dgrid->setAutoColumnsInEditMode(true);
  
## +---------------------------------------------------------------------------+
## | 8. Bind the DataGrid:                                                     | 
## +---------------------------------------------------------------------------+
##  *** set debug mode & messaging options



    $vm_columns = array(   
    "first_name"  =>array("header"=>"First name", "type"=>"label", "align"=>"left", "width"=>"20px", "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal"),
    "last_name" =>array("header"=>"Last name",     "type"=>"label", "align"=>"left", "width"=>"20px", "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal"),
	"nickname" =>array("header"=>"Nickname",     "type"=>"label", "align"=>"left", "width"=>"20px", "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal"),
	"email" =>array("header"=>"Email",     "type"=>"label", "align"=>"left", "width"=>"20px", "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal"),
	"notes" =>array("header"=>"Notes",     "type"=>"label", "align"=>"left", "width"=>"20px", "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal"),
	"member_trip_count" =>array("header"=>"Count",     "type"=>"label", "align"=>"left", "width"=>"20px", "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal"),
	"member_trip_percentage" =>array("header"=>"Percentage",     "type"=>"label", "align"=>"left", "width"=>"20px", "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal"),
    //"email" => array("header"=>"Email", "type"=>"label", "align"=>"left", "width"=>"20px", "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal"),    
    // "browser_language"  => array("header"=>"Browser language",      "type"=>"label", "width"=>"20px", "align"=>"left",   "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal"),
    // "user_ip" => array("header"=>"User ip", "type"=>"label", "align"=>"left", "width"=>"20px", "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal"),
    // "os_title"  => array("header"=>"OS title",      "type"=>"label", "width"=>"20px", "align"=>"left",   "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal"),
    // "os_version"  => array("header"=>"OS version",      "type"=>"label", "width"=>"20px", "align"=>"left",   "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal"),
    // "time"  => array("header"=>"time",      "type"=>"label", "width"=>"20px", "align"=>"left",   "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal")    
    //"pic"  =>array("header"=>"Pic",      "type"=>"link", "width"=>"20px", "align"=>"left",   "wrap"=>"nowrap", "text_length"=>"-1", "case"=>"normal", "field_key"=>"pic", "field_key_1"=>"pic", "field_data"=>"x", "rel"=>"", "title"=>"", "target"=>"_self", "href"=>"{0}"),
    //"gallery_url"  =>array("header"=>"Pic",      "type"=>"link", "width"=>"20px", "align"=>"left",   "wrap"=>"nowrap", "text_length"=>"-1", "field_key"=>"gallery_url", "field_key_1"=>"gallery_url", "field_data"=>"{1}", "rel"=>"{0}", "title"=>"{0}", "target"=>"_self", "href"=>"{0}"),
    //"pic"=>array("header"=>"pic", "type"=>"image",      "align"=>"left", "width"=>"20px", "wrap"=>"wrap", "text_length"=>"-1", "field_key"=>"gallery_url", "target_path"=>"{0}", "default"=>"def", "image_width"=>"50px", "image_height"=>"50px", "linkto"=>"{0}", "magnify"=>"true", "magnify_type"=>"lightbox", "magnify_power"=>"2"),
  );
  
  $dgrid->setColumnsInViewMode($vm_columns);   
  $dgrid->setAutoColumnsInEditMode(true);

##  *** set layouts: 0 - tabular(horizontal) - default, 1 - columnar(vertical) 
 $layouts = array("view"=>0, "edit"=>1, "add"=>1, "filter"=>1); 
 $dgrid->setLayouts($layouts);

 //$auto_column_in_edit_mode = true;
 //$dgrid->setAutoColumnsInEditMode($auto_column_in_edit_mode);
  
   $modes = array(
    "add"     =>array("view"=>1, "edit"=>false, "type"=>"link"),
    "edit"     =>array("view"=>true, "edit"=>true,  "type"=>"link", "byFieldValue"=>""),
    "cancel"  =>array("view"=>true, "edit"=>true,  "type"=>"link"),
    //"details" =>array("view"=>true, "edit"=>false, "type"=>"link"),
    "delete"  =>array("view"=>true, "edit"=>true,  "type"=>"image")
 );
 
 $dgrid->setModes($modes);

  $paging_option = true;
  $rows_numeration = false;
  $numeration_sign = "N #";
  $dropdown_paging = true;

  $dgrid->AllowPaging($paging_option, $rows_numeration, $numeration_sign, $dropdown_paging);
  
  $bottom_paging = array("results"=>true, "results_align"=>"left", "pages"=>true, "pages_align"=>"center", "page_size"=>true, "page_size_align"=>"right");
  $top_paging = array();
  
  $pages_array = array("25"=>"25", "50"=>"50", "100"=>"100", "250"=>"250", "500"=>"500");
  $default_page_size = empty($f_page_size) ? 25 : $f_page_size; // echo "default_page_size $default_page_size";
  $paging_arrows = array("first"=>"|&lt;&lt;", "previous"=>"&lt;&lt;", "next"=>"&gt;&gt;", "last"=>"&gt;&gt;|");
  $dgrid->SetPagingSettings($bottom_paging, $top_paging, $pages_array, $default_page_size, $paging_arrows);

    
    //////////////////////////////////////////////////////
    /*
    $conid = DB_Connect();
    DBCon::open_connection();
    
    $query = "select count(distinct(user_id)) as unique_users from team_members ".(!empty($filter_start_time_min) || !empty($filter_start_time_max) ? " WHERE 1 = 1 ".getSQLFilterString("time", $filter_start_time_min, $filter_start_time_max, "") : "");
    
    $db_result = DB_Execute($conid, $query, 1);      
    
    $unique_users = -1;
    
    if ($row = mysql_fetch_array($db_result, MYSQL_ASSOC))
        $unique_users = $row['unique_users'];

    echo "<br/>";

    echo "<div>";
    echo "<div style='float:left;padding-left:20px;'>";
        
    if (!empty($unique_users))
    {
        echo "Unique users: <b>".$unique_users."</b><br/>";
    }
    
    echo "</div>";
    echo "</div>";

    DBCon::close_connection();
    */

    $dgrid->bind();        
    ob_end_flush();
            
    echo "</form>";
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
            
        if (!empty($min_value))
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