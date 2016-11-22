<?php
    
    define('MSG_INFO', 1);
    define('MSG_ERROR', 2);
    define('MSG_WARNING', 3); //require_once ('log4php/Logger.php');

class Utils
{
    static function get_object_tree($xml_template)
    {
        $parser=xml_parser_create();

        xml_parser_set_option($parser, XML_OPTION_CASE_FOLDING, 0);
        xml_parser_set_option($parser, XML_OPTION_SKIP_WHITE, 1);

        //        while ($data=fread($fp, 40960000))
        //        {        
        //xml_parse($parser,$data,feof($fp)) or 
        xml_parse_into_struct($parser, $xml_template, $tags) or
        die (sprintf("XML Error: %s at line %d", 
        xml_error_string(xml_get_error_code($parser)),
        xml_get_current_line_number($parser)));

        //echo "time: ".date("i:s:m", (int)($end_time - $start_time))."<br/>";
        //print_r($tags);
        //        }

        xml_parser_free($parser);

        $elements = array();
        $stack = array();

        foreach ($tags as $tag)
        {
            $index = count($elements);

            if (($tag['type'] == "complete") || ($tag['type'] == "open"))
            {
                $elements[$index] = new XMLTemplateNode();
                $elements[$index]->title = $tag['tag'];
                if (array_key_exists('attributes', $tag)) 
                    $elements[$index]->attributes = $tag['attributes'];
                if (array_key_exists('value', $tag)) 
                    $elements[$index]->content = $tag['value'];                

                if ($tag['type'] == "open")
                {
                    $elements[$index]->children = array();
                    $stack[count($stack)] = &$elements;
                    $elements = &$elements[$index]->children;
                }                                            
            }

            if ($tag['type'] == "close")
            {
                $elements = &$stack[count($stack) - 1];
                unset($stack[count($stack) - 1]);
            }
        }

        Utils::_set_node_parent($elements[0]);

        return $elements[0];        
    }

    static function microtime_float()
    {
        list($usec, $sec) = explode(" ", microtime());
        return ((float)$usec + (float)$sec);
    }

    static function read_file($file_path)
    {
        //print_r($file_path);

        if (!($fp = fopen($file_path, "r")))
            die ("__could not open specified file");

        //$data = "";

        //        while ($data=fread($fp, 40960000))
        //        {                    
        //xml_parse($parser,$data,feof($fp)) or 
        //xml_parse_into_struct($parser, $data, $tags) or
        //        die (sprintf("XML Error: %s at line %d", 
        //        xml_error_string(xml_get_error_code($parser)),
        //        xml_get_current_line_number($parser)));

        //echo "time: ".date("i:s:m", (int)($end_time - $start_time))."<br/>";
        //print_r($tags);
        //        }

        $raw_data=fread($fp, 40960000);

        //echo $data."zxc";
        
        $data = utf8_decode($raw_data);
        
        //print(htmlentities( $data));
        fclose($fp);

        return $data;        
    }

//    function set_node_parent(&$node)
//    {       
//        for ($i = 0; $i < count($node->children); $i++)        
//        {
//            $node->children[$i]->parent = &$node;
//            set_node_parent($node->children[$i]);
//        }
//    }

    private static function _set_node_parent(&$node)
    {       
        for ($i = 0; $i < count($node->children); $i++)        
        {
            $node->children[$i]->parent = &$node;
            Utils::_set_node_parent($node->children[$i]);
        }
    }

    //    function _get_key_index(&$array, $key)
    //    {
    //        for ($i = 0; $i < count($array); $i++)
    //            if ($array[$i]->title == $key)
    //                return $i;
    //                
    //        return -1; //null
    //    }

    static function set_handler(&$node, $node_title, $handler, $is_function_first)
    {
        if ($node->title == $node_title)
        {
            $node->function = $handler; 
            $node->execute_function_first = $is_function_first;
            //return; // this should not return here because there might be other child nodes with this title
        }

        for ($i = 0; $i < count($node->children); $i++)
            Utils::set_handler($node->children[$i], $node_title, $handler, $is_function_first);
    }
        
    public static function print_message($message, $category, $type = 0)
    {
        if (@$GLOBALS['LOG_TO_SCREEN']) //if(LOG_TO_SCREEN)
        {
            $begin_tags = "";
            $end_tags = "";
            
            if ($type > 0)
            {
                switch($type)
                {
                    case MSG_INFO: 
                        $begin_tags .= "<p style='background-color:green'>".$begin_tags;
                        $end_tags = $end_tags."</p>";
                    case MSG_ERROR: 
                        $begin_tags .= "<p style='background-color:red'>".$begin_tags;
                        $end_tags = $end_tags."</p>";
                    case MSG_WARNING: 
                        $begin_tags .= "<p style='foreground-color:cyan'>".$begin_tags;
                        $end_tags = $end_tags."</p>";

                    break;
                }
            }            
            
            echo $begin_tags.htmlentities("$category:    $message").$end_tags."<br/>";
        }
    }
    
    static function render_combobox_control($control_id, $rows, $selected_index, $submit_on_change = true, $properties = "")
    {
        $html_output = "<select id='$control_id' name='$control_id' ".(($submit_on_change) ? " onChange=\"".($properties ? " ".$properties : "")." document.getElementById('submitButton').click();\"" : "").">";//style='white-space: pre;' 
        
        //for ($i = 0; $i < count($rows); $i++)
        //    $html_output .= "<option value='".$rows[$i][0]."'>".stripslashes(htmlentities($rows[$i][1]))."</option>";
                          
        foreach ($rows as $key => $value)
            $html_output .= "<option ".($selected_index && ($selected_index == $key)? "selected=\"true\"" :"")." value='".$key."'>".stripslashes($value)."</option>"; //stripslashes(htmlentities($value))
        $html_output .= "</select>";
        
        return $html_output;
    }
    
    static function combo_from_query($query, /*$id_field_name, $value_field_name, */$control_id, $con_id, $selected_index, $properties = "")
    {
        $fields = DB_Execute($con_id, $query);
        
        $rows[-1] = "All";
        
        $index = 1;
        
        while ($row = mysql_fetch_row($fields))
            $rows[$row[0]] = $row[1];            
        
        return Utils::render_combobox_control($control_id, $rows, $selected_index, true, $properties);//$submit_on_change = 
    }
    
    static function get_query_filter($field, $value, $is_number = 1)
    {
        if ($value)
        {
            if (is_numeric($value))
                return " $field = $value";
            else
                return " $field = '$value'";
        }
        
        return " 1 = 1";
    }
    
    /// gets the child nodes that have the given title (key), result is returned in an integer indexed array
    static function get_key_nodes(&$node, $title)
    {
        $result = array();
        
        for ($i = 0; $i < count($node->children); $i++)
            if ($node->children[$i]->title == $title) 
                $result[] = &$node->children[$i];

        return $result;
    }
    
    function get_alias_type($id_type)
    {
        switch  ($id_type)
        {
            case ALIAS_TYPE_GENERIC: return "Generic"; break;
            case ALIAS_TYPE_LOCATION: return "Location"; break;
            case ALIAS_TYPE_PARTICIPANT: return "Partipant"; break;
            case ALIAS_TYPE_SPORT: return"Sport"; break;
            case ALIAS_TYPE_TOURNAMENT: return "Tournament"; break;
        }
        
        return "Not known";
    }
    
    static function links_from_query($query, /*$id_field_name, $value_field_name, */$control_id, $con_id, $path)
    {
        $fields = DB_Execute($con_id, $query);
                
        $result = "";
        
        while ($row = mysql_fetch_row($fields))
            $result .= "<a href='$path?$control_id=".$row[0]."'>$row[1]</a><br/>";//<br/>";
        
        return $result;
    }

    static function check_parameter($parameter, $error_text)
    {
        if (empty($parameter)) //or !($parameter > 0)
        {
            Utils::print_message($error_text, "Incorrect parameter");
            return false;
        }
        else
            return true;
    }

    static function render_array_links($items, $control_id, $con_id, $path, $parameters)
    {
        $result = "";
                                         
        $query_string = "";
        
        if (count($parameters) > 0)
            foreach ($parameters as $key => $value)
                $query_string .= "&$key=$value";
                
        foreach ($items as $key => $fields)
            $result .= "<a href='$path?$control_id=".$fields[0].$query_string."'>".$fields[1]."</a><br/>";

        return $result;        
    }
    
    function str_insert($insertstring, $intostring, $offset) 
    {
       $part1 = substr($intostring, 0, $offset);
       $part2 = substr($intostring, $offset);
      
       $part1 = $part1 . $insertstring;
       $whole = $part1 . $part2;
       return $whole;
   }
    //ini_set('display_errors',1);    //error_reporting(E_ALL|E_STRICT);    //or if you only want to see Warning Messages and not Notice Messages:    //ini_set('display_errors',1);    //error_reporting(E_ALL);    //all Error Reporting off:    //error_reporting(0);
    
    static function get_array_fields($ar, $key_title)
    {
        $result = array();
        
        foreach($ar as $key => $fields)
            $result[$key] = $fields[$key_title];
            
        return $result;
    }
    
    static function start_timer()
    {
        return Utils::microtime_float();
    }
        
    static function get_timer_seconds($start_time)
    {
        $end_time = Utils::microtime_float(); 
        return $time = $end_time - $start_time;
    }
    
    static function print_timer_seconds($start_time)
    {
        $end_time = Utils::microtime_float(); $time = $end_time - $start_time; 
        echo "Time span: ".$time." seconds <br/>";
    }
    
    static function set_null_integer($parameter)
    {
        if (!isset($parameter))
            $parameter = 0; //-- or set to 0 or ''
    }
    
    static function set_null_string($parameter)
    {
        if (!isset($parameter))
            $parameter = ''; //-- or set to 0 or ''
    }
    

    static function combo_x()
    {
//        $fields = DB_Execute($con_id, $query);
//        
//        $rows[-1] = "All";
//        
//        $index = 1;
//        
//        while ($row = mysql_fetch_row($fields))
//            $rows[$row[0]] = $row[1];            
//        
//        return Utils::render_combobox_control($control_id, $rows, $selected_index, true, $properties);//$submit_on_change = 
    }
    
    static function record($message, $type = '')
    {   
        static $logger;
        
        if (empty($logger))
        {   
            Logger::configure(dirname(__FILE__).'/resources/appender_file.properties');
            
            $logger = Logger::getRootLogger();
            $logger->getAppender("default")->setFile(dirname(__FILE__).'/main_log.txt');
            $logger->getAppender("default")->activateOptions();
            //echo "file: ".dirname(__FILE__).'/main_log.txt';
        }
            
        $logger->info(($type == '' ? '' : "$type: ").$message);
    }
    
    static function print_combobox_control($control_id, $rows, $selected_index, $submit_on_change = true, $properties = "")
    {
         $add_script = " appendSelectOptions(this, $selected_index); "; //this.selectedIndex = $selected_index; 
         echo "<select id='$control_id' name='$control_id' ".(($submit_on_change) ? " onChange=\"".($properties ? " ".$properties : "")." /*document.getElementById('submitButton').click();*/\"" : "")." onMouseDown=\"$add_script\">";//style='white-space: pre;' 
        
        //for ($i = 0; $i < count($rows); $i++)
        //    $html_output .= "<option value='".$rows[$i][0]."'>".stripslashes(htmlentities($rows[$i][1]))."</option>";

        // initialy put only the selected option
        if (empty($selected_index)) echo "<option selected=\"true\" value='-1'>None</option>"; else
        echo "<option selected=\"true\" value='".$selected_index."'>".stripslashes($rows[$selected_index])."</option>"; //stripslashes(htmlentities($value))                          
//        foreach ($rows as $key => $value)
//            echo "<option ".($selected_index && ($selected_index == $key)? "selected=\"true\"" :"")." value='".$key."'>".stripslashes($value)."</option>"; //stripslashes(htmlentities($value))

        echo "</select>";
        
        //return $html_output;
    }

    // returns the browser script function that adds an option to a select element
        static function get_add_function()
    {
        return "function appendSelectOptions(select_object, selected_index)
{ if (select_object.tag == 1) return; select_object.tag = 1; select_object.options.length = 0;
for (key in tournaments) {  var option = document.createElement('option');
  option.text = tournaments[key];
  option.value = key;
  if (selected_index == key) {
      option.selected = true;           //if (selected_index == key) option.defaultSelected = true;
	  //select_object.options[select_object.selectedIndex].text=tournaments[key];
  }
  try {
    select_object.add(option, null); // standards compliant; doesn't work in IE
  }
  catch(ex) {
    select_object.add(option); // IE only
  }  //if (key == 0) select_object.options[select_object.selectedIndex].text=tournaments[key];
}
}
";
    }
    
    static function redirect_to_page($url)
    {
        header("Location: /$url");
    }
    
    static function render_combobox_ex($control_id, $rows, $selected_index, $submit_on_change = true, $properties = "")
    {
        $html_output = "<select id='$control_id' name='$control_id' ".(($submit_on_change) ? " onChange=\"".($properties ? " ".$properties : "")." \"" : "")." STYLE='width: 150px'>";//style='white-space: pre;' 
        
        //for ($i = 0; $i < count($rows); $i++)
        //    $html_output .= "<option value='".$rows[$i][0]."'>".stripslashes(htmlentities($rows[$i][1]))."</option>";
                          
        foreach ($rows as $key => $ar)
            $html_output .= "<option ".($selected_index && ($selected_index == $key)? "selected=\"true\"" :"")." value='".$key."'>".stripslashes($ar[1])."</option>"; //stripslashes(htmlentities($value))
        $html_output .= "</select>";
        
        return $html_output;
    }
    
    static function get_country()
    {
        @$id_country = $_REQUEST['countries_combobox'];
                            
        if(empty($id_country))
            @$id_country = $_COOKIE['country'];
                            
        //if(!empty($id_country))
            return $id_country;
                            
    }
    
    static function get_parameter_array($key_template, &$parameters)
    {
        $result = array();
        
        foreach ($parameters as $key => $value)
            if (strpos($key, $key_template) !== false)
                $result[(int)str_replace($key_template, "", $key)] = $value;    
                
        return $result;
    }

    // formats a datetime string to be compatible with mysql database
    static function get_formated_date($date_str)
    {
        if (empty($date_str))
            return 'NULL';
        else
            return "'$date_str'";//$start_time
    }

    static function convert_to_timezone($date_str, $timezone)
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
}

    function printline($message)
    {
        echo "$message<br/>";
    }
    
    function print_vars()
    {
        
        $params = func_get_args();

        foreach ($params as $key => $value)            
            echo "$value  ";
            
        echo "<br/>";
    }
    
    function getSQLFilterString($field, $min_value, $max_value, $equal_value)    
    {
        $sqlString = "";
                
        /*if (empty($value))
            return "";*/
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
    
    function getResetFormButtonCode()
    {
        return "
        <script type='text/javascript'>
        function clearForm(frm_elements)
        {
        for (i = 0; i < frm_elements.length; i++)
{
    field_type = frm_elements[i].type.toLowerCase();
    switch (field_type)
    {
    case 'text':
    case 'password':
    case 'textarea':
    case 'hidden':
        frm_elements[i].value = '';
        break;
    case 'radio':
    case 'checkbox':
        if (frm_elements[i].checked)
        {
            frm_elements[i].checked = false;
        }
        break;
    case 'select-one':
    case 'select-multi':
        frm_elements[i].selectedIndex = -1;
        break;
    default:
        break;
    }
}
}
        </script>
        <input type='button' name='clear' value='Clear form' onclick='clearForm(this.form);'>
        ";
    }
    
    function flush_buffers()
    { 
        ob_end_flush(); 
        @ob_flush(); 
        flush(); 
        ob_start(); 
    } 
?>
