<?php
    include_once 'db_interface.php';
    include_once 'config.php';

    class GPSData
    {        
        public static $ConId;
        
		public static $tablePrefix;
        ///
        /// gets GPS points
        ///
        public static function get_gps_points($user_id)
        {
            if (empty($user_id) && ($user_id < 0))
            {
                Utils::print_message("user_id parameter is empty", "argument error", MSG_ERROR);  
                return false;
				
                //throw new Exception('Title are empty'); //return 0;
            }
			$tp = GPSData::$tablePrefix;
			$query = "select *, X(coords) AS lat_from_point, Y(coords) AS long_from_point FROM {$tp}points"; //where disabled != 1 or disabled is null
			
			//$query = "select *, AsWKB(coords) AS wkb FROM {$tp}points"; //where disabled != 1 or disabled is null
            $db_result = DB_Execute(GPSData::$ConId, $query);

            $rows = array();
            
            while ($row = mysql_fetch_array($db_result, MYSQL_ASSOC))
                $rows[/*$row["id"]*/] = $row; // 0 based index
                
            return $rows;

        }

        public static function get_cave_entrances($user_id)
        {
            if (empty($user_id) && ($user_id < 0))
            {
                Utils::print_message("user_id parameter is empty", "argument error", MSG_ERROR);  
                return false;
				
                //throw new Exception('Title are empty'); //return 0;
            }
			$tp = GPSData::$tablePrefix;
			$query = "SELECT
						cave_entrances.id AS cave_entrance_id, 
						cave_entrances.name AS cave_entrance_name, 
						cave_entrances.entranceType AS cave_entrance_type,
						caves.id AS cave_id, 
						caves.name AS cave_name, 
						caves.locationIdentifier AS cave_location_identifier,
						X(coords) AS lat_from_point, Y(coords) AS long_from_point, elevation, ASTEXT(spatial_geometry) as sg FROM points
						INNER JOIN cave_entrances ON points.id = cave_entrances.point_id
						INNER JOIN caves ON cave_entrances.cave_id = caves.id"; //where disabled != 1 or disabled is null
			
			//$query = "select *, AsWKB(coords) AS wkb FROM {$tp}points"; //where disabled != 1 or disabled is null
            $db_result = DB_Execute(GPSData::$ConId, $query);

            $rows = array();
            
            while ($row = mysql_fetch_array($db_result, MYSQL_ASSOC))
			{
				$row["point_type"] = "cave_entrance";
                $rows[/*$row["id"]*/] = $row; // 0 based index
			}
                
            return $rows;
        }

        public static function get_features($user_id)
        {
            if (empty($user_id) && ($user_id < 0))
            {
                Utils::print_message("user_id parameter is empty", "argument error", MSG_ERROR);  
                return false;
				
                //throw new Exception('Title are empty'); //return 0;
            }
			$tp = GPSData::$tablePrefix;
			$query = "SELECT 
						features.*, 
						X(coords) AS lat_from_point, Y(coords) AS long_from_point, elevation, ASTEXT(spatial_geometry) as sg FROM points
					  INNER JOIN features ON points.id = features.point_id;"; //where disabled != 1 or disabled is null
			
			//$query = "select *, AsWKB(coords) AS wkb FROM {$tp}points"; //where disabled != 1 or disabled is null
            $db_result = DB_Execute(GPSData::$ConId, $query);

            $rows = array();
            
            while ($row = mysql_fetch_array($db_result, MYSQL_ASSOC))
			{
				$row["point_type"] = "feature";
                $rows[/*$row["id"]*/] = $row; // 0 based index
			}
                
            return $rows;
        }
		
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
        
        //public static function add_gps_points($points)
		public static function add_gps_point($point, $user_id)
        {
            if (empty($point))
            {
                Utils::print_message("Point parameter is empty", "Add error", MSG_ERROR);
                //throw new Exception('Title are empty'); //return 0;
            }

            if (empty($user_id)) 
                Utils::print_message("The user_id parameter is empty", "Add error", MSG_ERROR);  
                //throw new Exception("The itemId is empty");
            
            //global $CONN_ID;                   
            $gpx_name = mysql_real_escape_string($point->name, GPSData::$ConId);
            $gpx_sym = mysql_real_escape_string($point->sym, GPSData::$ConId);
            $gpx_type = mysql_real_escape_string($point->type, GPSData::$ConId);
            $gpx_cmt = mysql_real_escape_string($point->cmt, GPSData::$ConId);
            
			$gpx_time;
			
            if (empty($point->time))
                $gpx_time = "NULL";
            else
                $gpx_time = date('Y-m-d H:i:s', strtotime($point->time));
            
			//echo "z>> $point->name";
            //$listing_type_id = GPSData::get_listing_type_id("$listingType");
            //$selling_state_id = GPSData::get_selling_state_id("$sellingState");
            
			$gpx_sat = 'NULL';
            
			if (!empty($point->sat))
                $gpx_sat = $point->sat;
			
			$elevation = 'NULL';
			
			if (isset($point->coords[2]))
				$elevation = $point->coords[2];
			
            $tp = GPSData::$tablePrefix;
			
			// POINT({$point->coords[1]} {$point->coords[0]}) because openlayers sends point coordinate data in [long, lat] format
            $query = "INSERT INTO `{$tp}points` 	(`lat`, 	`long`, 	`elevation`, 	`coords`, 	`gpx_name`, 	`gpx_sym`, 	`gpx_type`, 	`gpx_cmt`, 	`gpx_sat`, 	`gpx_fix`, 	`gpx_time`, 	`_type`, 	`_details`, 	`added_by_user_id`, 	`add_time`	)
					  VALUES	('{$point->coords[0]}', 	'{$point->coords[1]}', 	'{$elevation}', 	GEOMFROMTEXT('POINT({$point->coords[1]} {$point->coords[0]})'), '{$gpx_name}', 	'{$gpx_sym}', 	'{$gpx_type}', 	'{$gpx_cmt}', 	{$gpx_sat}, 	'{$point->fix}', 	'{$gpx_time}', 	0, 	'', 	$user_id, 	NOW()	);";
                                                         
			//"ON DUPLICATE KEY update title = '$title' ,     sub_title = '$subtitle' ,     view_item_url = '$viewItemURL' ,     gallery_url = '$galleryURL' ,     listing_type = $listing_type_id ,     start_time = '$startTime' ,     end_time = '$endTime' ,     selling_state = $selling_state_id ,     converted_current_price = $convertedCurrentPrice ,     bid_count = $bidCount,    last_update_time = NOW(),    seller_user_name = '$sellerUserName'"; /*,     item_id = $itemId*/ //SELECT `id` FROM `disciplines` WHERE (`title` = '$title' AND `id_provider` = $provider_id)"; //`path` = '$path'
            //GPSData::$comindex++; echo "command ".GPSData::$comindex." <br/>";
            //$last_id = mysql_insert_id(GPSData::$ConId);
            //print_r($query);
            
            $qres = DB_Execute(GPSData::$ConId, $query);

            $affected_rows = mysql_affected_rows(GPSData::$ConId);
            $last_id = mysql_insert_id(GPSData::$ConId);

            if ($qres)
                return true;
            else
                return false;
        }		

		public static function add_file($file_path, $user_id)
        {
            if (empty($file_path))
            {
                Utils::print_message("file_path parameter is empty", "Add error", MSG_ERROR);
                //throw new Exception('Title are empty'); //return 0;
            }

            if (empty($user_id)) 
                Utils::print_message("The user_id parameter is empty", "Add error", MSG_ERROR);  
                //throw new Exception("The itemId is empty");
            
            //global $CONN_ID;              
			
			$file_name = basename($file_path);
            //$file_path = mysql_real_escape_string($point->name, GPSData::$ConId);
			
			//>> add duplicate detection and duplicate file management (on duplicate check for file existence on disk, ask user what to do > re add on layer)
            $md5_hash = md5_file($file_path);
			$file_size = filesize($file_path);
			
            $tp = GPSData::$tablePrefix;
			
            $query = "INSERT INTO `{$tp}files` 	(`file_name`, 	`id_user`, 	`add_time`, 	`type`, size, md5_hash	)
					  VALUES	( '$file_name', 	$user_id, 	NOW(), 	'undefined', $file_size, '$md5_hash');";
                                                                     
            $qres = DB_Execute(GPSData::$ConId, $query);

            $affected_rows = mysql_affected_rows(GPSData::$ConId);
            $last_id = mysql_insert_id(GPSData::$ConId);

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
