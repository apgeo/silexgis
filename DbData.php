<?php
    include_once 'db_interface.php';
    include_once 'config.php';

    class GPSData
    {        
        public static $ConId;
        
		public static $tablePrefix;
		
        public static function get_user($user, $pass)
        {	$tp = GPSData::$tablePrefix;
            $query = "SELECT 	`id`, `username`, 	`password`, 	`email`, 	`admin_level`	 	FROM 	`{$tp}users` WHERE username = '$user' && password = '$pass'";

			//var_dump($query);
            $db_result = DB_Execute(GPSData::$ConId, $query);

			//print_r($db_result);
            //$count = 0;            
            $row = null;

            $rows = array();
            
            while ($row = mysql_fetch_array($db_result, MYSQL_ASSOC))
                $rows[/*$row["id"]*/] = $row; // 0 based index
                
            return $rows;
		}

        /*
            1    LAST_CHECKED_EMAIL_INDEX    0    \N    \N
            2    TOTAL_CHECKS    0    \N    \N
            3    LAST_CHECK_TIME    \N    \N    \N
            4    LAST_ERROR    \N    \N    \N
            5    LAST_ERROR_TIME    \N    \N    \N
        */
        
        public static function get_and_set_next_email()
        {            
            $index = SystemOptionsData::getOptionValue("LAST_CHECKED_EMAIL_INDEX");            
            
            $email_count = self::get_email_account_count();
            
            $new_index = $index + 1;
            
            if ($new_index >= $email_count)
                $new_index = 0; // 0 based index

            $email = self::get_email_by_index($new_index);
            
            SystemOptionsData::updateOption("LAST_CHECKED_EMAIL_INDEX", $new_index);
            
            return $email;
        }
        
        public static function get_triggers()
        {            $tp = GPSData::$tablePrefix;
            $query = "select     id,     expression,     type,     action     from     {$tp}triggers";

            $db_result = DB_Execute(GPSData::$ConId, $query);

            $items = array();
            
            while ($row = mysql_fetch_array($db_result, MYSQL_ASSOC))
                $items[$row["id"]] = $row;
                
            return $items;
        }

        public static function add_log_entry($text, $type)
        {
            if (empty($text))
            {
                Utils::print_message("Text is empty", "Add log error", MSG_ERROR);  
                return;
                //throw new Exception('Title are empty'); //return 0;
            }

            if (empty($type)) 
                Utils::print_message("The type is empty", "Add log warning", MSG_WARNING);  
                //throw new Exception("The itemId is empty");
            
            //global $CONN_ID;                   
            $text = mysql_real_escape_string($text, GPSData::$ConId);
            $type = mysql_real_escape_string($type, GPSData::$ConId);
            
			$tp = GPSData::$tablePrefix;				
            $query = "insert into {$tp}log     (text,     type,     time    )
                        values    ('$text',     '$type',     NOW()  )";            

            $qres = DB_Execute(GPSData::$ConId, $query);

            //$affected_rows = mysql_affected_rows(GPSData::$ConId);
            //$last_id = mysql_insert_id(GPSData::$ConId);

            if ($qres)
                return true;
            else
                return false;                      
        }
        		
		public static function user_has_modify_right($user_id, $cave_id)
        {
			//--
			return true;
		}		
	}
?>
