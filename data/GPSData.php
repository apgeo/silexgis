<?php
	require_once dirname(__DIR__).'/config.php';
	$root = realpath($_SERVER["DOCUMENT_ROOT"]).$application_url_root;
	
    include_once "$root/db_interface.php";
    include_once "$root/config.php";
	
	include_once "$root/data/db_common.php";

    class GPSData
    {        
        public static $ConId;
        
		public static $tablePrefix;
        ///
        /// gets GPS points
        ///
        public static function get_gps_points($user_id)
        {
            if (empty($user_id) || ($user_id < 0))
            {
                Utils::print_message("user_id parameter is empty", "argument error", MSG_ERROR);  
                return false;
				
                //throw new Exception('Title are empty'); //return 0;
            }
			$tp = GPSData::$tablePrefix;
			$query = "select *, 
						X(spatial_geometry) AS lat_from_point, Y(spatial_geometry)
						FROM points"; //where disabled != 1 or disabled is null
			
			//$query = "select *, AsWKB(coords) AS wkb FROM {$tp}points"; //where disabled != 1 or disabled is null
            $db_result = DB_Execute(GPSData::$ConId, $query);

            $rows = array();
            
            while ($row = mysql_fetch_array($db_result, MYSQL_ASSOC))
                $rows[/*$row["id"]*/] = $row; // 0 based index
                
            return $rows;

        }

        public static function get_cave_entrances($user_id)
        {
            if (empty($user_id) || ($user_id < 0))
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
						caves.identification_code AS identification_code,
						elevation, 
						ASTEXT(spatial_geometry) as sg FROM points
						INNER JOIN cave_entrances ON points.id = cave_entrances.point_id
						INNER JOIN caves ON cave_entrances.cave_id = caves.id"; //where disabled != 1 or disabled is null
			
			// X(coords) AS lat_from_point, Y(coords) AS long_from_point, 
			//$query = "select *, AsWKB(coords) AS wkb FROM {$tp}points"; //where disabled != 1 or disabled is null
            $db_result = DB_Execute(GPSData::$ConId, $query);

            $rows = array();
            
            while ($row = mysql_fetch_array($db_result, MYSQL_ASSOC))
			{
				$row["point_type"] = "cave_entrance";
				$row["geoobject_type"] = "cave_entrance";
                $rows[/*$row["id"]*/] = $row; // 0 based index
			}
                
            return $rows;
        }

        public static function get_pictures($user_id, $bbox)
        {			
            if (empty($user_id) || ($user_id < 0))
            {
                Utils::print_message("user_id parameter is empty", "argument error", MSG_ERROR);  
                return false;
				
                //throw new Exception('Title are empty'); //return 0;
            }
			
			$bbox_polygon_text = "{$bbox[1]} {$bbox[0]},{$bbox[3]} {$bbox[0]},{$bbox[3]} {$bbox[2]},{$bbox[1]} {$bbox[2]},{$bbox[1]} {$bbox[0]}";
			//echo $bbox_polygon_text;
			
			$tp = GPSData::$tablePrefix;
			$query = "SELECT
						images.id AS image_id, 
						description,
						file_path,
						thumb_file_path,
						images.add_time as image_add_time,
						point_id,												
						elevation,
                        picture_storage_type, 
						ASTEXT(spatial_geometry) AS sg FROM points
						INNER JOIN images ON points.id = images.point_id
						WHERE CONTAINS(GEOMFROMTEXT('Polygon(($bbox_polygon_text))'), spatial_geometry)"; //where disabled != 1 or disabled is null
			
			// WHERE CONTAINS(GEOMFROMTEXT('Polygon((44 24,46 24,46 26,44 26,44 24))'), spatial_geometry)"
			// X(coords) AS lat_from_point, Y(coords) AS long_from_point, 
			
			//$query = "select *, AsWKB(coords) AS wkb FROM {$tp}points"; //where disabled != 1 or disabled is null
            $db_result = DB_Execute(GPSData::$ConId, $query);

            $rows = array();
            
            while ($row = mysql_fetch_array($db_result, MYSQL_ASSOC))
			{
				//$row["point_type"] = "cave_entrance";
                $rows[/*$row["id"]*/] = $row; // 0 based index
			}
                
            return $rows;
        }
		
        public static function get_features($user_id, $feature_type, $only_gallery_area)
        {
            if (empty($user_id) || ($user_id < 0))
            {
                Utils::print_message("user_id parameter is empty", "argument error", MSG_ERROR);  
                return false;
				
                //throw new Exception('Title are empty'); //return 0;
            }
			
            if (empty($feature_type))
            {
                Utils::print_message("feature_type parameter is empty", "argument error", MSG_ERROR);  
                return false;
				
                //throw new Exception('Title are empty'); //return 0;
            }
			
			//if ($only_gallery_area)
			
			$tp = GPSData::$tablePrefix;
			$query = "SELECT 
						features.*,
						elevation,
						ASTEXT(spatial_geometry) as sg FROM points
					  INNER JOIN features ON points.id = features.point_id
					  INNER JOIN feature_types ON features.feature_type_id = feature_types.id
					  WHERE feature_types.group_type=\"$feature_type\" AND ".
					  ($only_gallery_area ? " feature_types.name = 'gallery_area' " : " feature_types.name != 'gallery_area' ");
					  //where disabled != 1 or disabled is null

			// X(coords) AS lat_from_point, Y(coords) AS long_from_point, 
			//$query = "select *, AsWKB(coords) AS wkb FROM {$tp}points"; //where disabled != 1 or disabled is null
            $db_result = DB_Execute(GPSData::$ConId, $query);

            $rows = array();
            
            while ($row = mysql_fetch_array($db_result, MYSQL_ASSOC))
			{
				$row["point_type"] = "feature";
				//$row["geoobject_type"] = "cave_entrance or cave or";
				$row["geoobject_type"] = $feature_type;
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
			            			
			$spg = "POINT({$point->coords[1]} {$point->coords[0]})";

			$query = "SELECT 1 FROM points WHERE lat = ROUND({$point->coords[1]}, 6) AND `long` = ROUND({$point->coords[0]}, 6) AND gpx_name = '{$gpx_name}' LIMIT 1";
			
			$qres = DB_Execute(GPSData::$ConId, $query);

			//$affected_rows = mysql_affected_rows(GPSData::$ConId);
			//var_dump(mysql_fetch_object($qres));
			if(mysql_fetch_object($qres) === false)
			//if(mysql_fetch_array($qres) === false)
			{
			
			// POINT({$point->coords[1]} {$point->coords[0]}) because openlayers sends point coordinate data in [long, lat] format
            $query = "INSERT INTO `points` 	(`lat`, 	`long`, 	`elevation`, 	`gpx_name`, 	`gpx_sym`, 	`gpx_type`, 	`gpx_cmt`, 	`gpx_sat`, 	`gpx_fix`, 	`gpx_time`, 	`_type`, 	`_details`, 	`added_by_user_id`, 	`add_time`, 	spatial_geometry)
					  VALUES	({$point->coords[1]}, 	{$point->coords[0]}, 	{$elevation}, 	'{$gpx_name}', 	'{$gpx_sym}', 	'{$gpx_type}', 	'{$gpx_cmt}', 	{$gpx_sat}, 	'{$point->fix}', 	{$gpx_time}, 	0, 	'', 	$user_id, 	NOW(), GEOMFROMTEXT('$spg'));";
            
			echo 'added point '."{$point->coords[1]}, 	{$point->coords[0]}".'</br>';
			
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
			else
			{
				echo "duplicate_point<br/>";
				return "duplicate_point";
			}
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
			
            $query = "INSERT INTO `{$tp}geofiles` 	(`file_name`, 	`id_user`, 	`add_time`, 	`type`, size, md5_hash, enabled	)
					  VALUES	( '$file_name', 	$user_id, 	NOW(), 	'undefined', $file_size, '$md5_hash', 1);";
                                                                     
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
		
        public static function get_files($user_id, $cave_id)
        {
            if (empty($user_id) || ($user_id < 0))
            {
                Utils::print_message("user_id parameter is empty", "argument error", MSG_ERROR);
                return false;
				
                //throw new Exception('Title are empty'); //return 0;
            }

            if (empty($cave_id) || ($cave_id < 0))
            {
                Utils::print_message("cave_id parameter is empty", "argument error", MSG_ERROR);
                return false;
				
                //throw new Exception('Title are empty'); //return 0;
            }
			
			$query = "SELECT files.id as fid, file_name, user_id, files.add_time, file_type, size, md5_hash, geoobjects_to_files.id, object_type
						FROM files
						INNER JOIN geoobjects_to_files ON geoobjects_to_files.file_id = files.id
						INNER JOIN 
						(SELECT id, 'cave' AS object_type FROM caves WHERE caves.id = $cave_id
						 UNION
						 SELECT id, 'feature' AS object_type FROM features
						 ) AS geoobjects_vt ON geoobjects_to_files.geoobject_id = geoobjects_vt.id AND object_type = geoobjects_to_files.geoobject_type
						 INNER JOIN users ON users.id = files.user_id
						 
						 ORDER BY files.id"; //where disabled != 1 or disabled is null
                        //  WHERE enabled = 1 or enabled is null
			
			//$query = "select *, AsWKB(coords) AS wkb FROM {$tp}points"; //where disabled != 1 or disabled is null
            $db_result = DB_Execute(GPSData::$ConId, $query);

            $rows = array();
            
            while ($row = mysql_fetch_array($db_result, MYSQL_ASSOC))
			{
				//$row["file_type"] = "cave";
                $rows[/*$row["id"]*/] = $row; // 0 based index
			}
            
            return $rows;
        }
		
        public static function get_trip_report_files($user_id, $trip_log_id)
        {
            if (empty($user_id) || ($user_id < 0))
            {
                Utils::print_message("user_id parameter is empty", "argument error", MSG_ERROR);
                return false;
				
                //throw new Exception('Title are empty'); //return 0;
            }

            if (empty($trip_log_id) || ($trip_log_id < 0))
            {
                Utils::print_message("$trip_log_id parameter is empty", "argument error", MSG_ERROR);
                return false;
				
                //throw new Exception('Title are empty'); //return 0;
            }
			
			$query = "SELECT files.id, file_name, user_id, add_time, file_type, size, md5_hash, trip_logs_to_files.id AS trip_logs_to_files_id
								FROM files
								INNER JOIN trip_logs_to_files ON trip_logs_to_files.file_id = files.id
								WHERE trip_logs_to_files.trip_log_id = $trip_log_id
								ORDER BY files.id"; //where disabled != 1 or disabled is null
			
			//$query = "select *, AsWKB(coords) AS wkb FROM {$tp}points"; //where disabled != 1 or disabled is null
            $db_result = DB_Execute(GPSData::$ConId, $query);

            $rows = array();
            
            while ($row = mysql_fetch_array($db_result, MYSQL_ASSOC))
			{
				//$row["file_type"] = "cave";
                $rows[/*$row["id"]*/] = $row; // 0 based index
			}
                
            return $rows;
        }

        public static function get_trip_report_features($user_id, $trip_log_id)
        {
            if (empty($user_id) || ($user_id < 0))
            {
                Utils::print_message("user_id parameter is empty", "argument error", MSG_ERROR);
                return false;
				
                //throw new Exception('Title are empty'); //return 0;
            }

            if (empty($trip_log_id) || ($trip_log_id < 0))
            {
                Utils::print_message("trip_log_id parameter is empty", "argument error", MSG_ERROR);
                return false;
				
                //throw new Exception('Title are empty'); //return 0;
            }
			
			//-- query needs improvement as too many rows are selected
			
			$query = "SELECT trip_logs.id AS trip_log_id, trip_logs_to_features.id AS trip_logs_to_features_id, trip_logs_to_features.geoobject_id as geoobject_id, geoobject_type, geoobject_name
						FROM trip_logs
						INNER JOIN trip_logs_to_features ON trip_logs_to_features.trip_log_id = trip_logs.id
						INNER JOIN 
						(SELECT id, name as geoobject_name, 'cave' AS object_type FROM caves
						 UNION
						 SELECT id, name as geoobject_name, 'feature' AS object_type FROM features
						 UNION
						 SELECT id, name as geoobject_name, 'feature' AS object_type FROM cave_entrances
						 ) AS geoobjects_vt ON trip_logs_to_features.geoobject_id = geoobjects_vt.id AND object_type = trip_logs_to_features.geoobject_type
						 WHERE trip_logs.id = $trip_log_id
						 ORDER BY trip_logs.id"; //where disabled != 1 or disabled is null
			
			//$query = "select *, AsWKB(coords) AS wkb FROM {$tp}points"; //where disabled != 1 or disabled is null
            $db_result = DB_Execute(GPSData::$ConId, $query);

            $rows = array();
            
            while ($row = mysql_fetch_array($db_result, MYSQL_ASSOC))
			{
				//$row["file_type"] = "cave";
                $rows[/*$row["id"]*/] = $row; // 0 based index
			}
                
            return $rows;

/*			
            if (empty($user_id) || ($user_id < 0))
            {
                Utils::print_message("user_id parameter is empty", "argument error", MSG_ERROR);
                return false;
				
                //throw new Exception('Title are empty'); //return 0;
            }

            if (empty($trip_log_id) || ($trip_log_id < 0))
            {
                Utils::print_message("$trip_log_id parameter is empty", "argument error", MSG_ERROR);
                return false;
				
                //throw new Exception('Title are empty'); //return 0;
            }
			
			$query = "SELECT features.id, features.name, user_id, trip_logs_to_features.id AS trip_logs_to_features_id
								FROM features
								INNER JOIN trip_logs_to_features ON trip_logs_to_features.feature_id = features.id
								WHERE trip_logs_to_features.trip_log_id = $trip_log_id
								ORDER BY features.id"; //where disabled != 1 or disabled is null
			
			//$query = "select *, AsWKB(coords) AS wkb FROM {$tp}points"; //where disabled != 1 or disabled is null
            $db_result = DB_Execute(GPSData::$ConId, $query);

            $rows = array();
            
            while ($row = mysql_fetch_array($db_result, MYSQL_ASSOC))
			{
				//$row["file_type"] = "cave";
                $rows[] = $row; // 0 based index
			}
                
            return $rows;
*/			
        }
		
		public static function add_picture($user_id, $file_name, $thumbnail_file_name, $coord, $pic_storage_type)
		{
	$image = null;
	
	/*if (!empty($picture_id))
	{
		$image = ImagesQuery::create()->findPK($picture_id);				
	}
	else*/
	{
		$image = new Images();
	}
        
	//$file_path = "";//$pictureData->
		
	//$thumbnail_file_name = "thumb".$_FILES['file']['name']; //- should be returned by savePictureFile
	
	//$image->setName($featureData->feature_name);
	//$image->setDescription();
	$image->setFilepath($file_name); //-- filePath relative or full?
	$image->setUserid($user_id);
	$image->setThumbfilepath($thumbnail_file_name);	
	$image->setAddtime(time());
    $image->setPicturestoragetype($pic_storage_type);

    $lat = 'NULL';
    $long = 'NULL';

    $lat_string = 'NULL';
    $long_string = 'NULL';
    
    if (!is_null($coord))
    {
        $lat = "'".$coord[0]."'";
        $long = "'".$coord[1]."'";

        $lat_string = $coord[0];
        $long_string = $coord[1];
    }

	// add new point if in insert mode (new cave)
	if (empty($picture_id)) //!isset($mainCaveEntrance)) // insert
	{	
		$con = Propel::getConnection("speogis"); // BookPeer::DATABASE_NAME

		/*$pictureGeoJsonString = $pictureData->feature_string;
		$featureWKTString = json_to_wkt($pictureGeoJsonString);
		
        $query = "INSERT INTO `points` 	(`lat`, 	`long`, 	`elevation`, 	`coords`, 	`gpx_name`, 	`gpx_sym`, 	`gpx_type`, 	`gpx_cmt`, 	`gpx_sat`, 	`gpx_fix`, 	`gpx_time`, 	`_type`, 	`_details`, 	`added_by_user_id`, 	`add_time`, spatial_geometry	)
					  VALUES	('0', 	'0', 	'{-1}', 	GEOMFROMTEXT('POINT(0 0)'), '', 	'', 	'', 	'', 	-1, 	'', 	'', 	0, 	'', 	$_user_id, 	NOW(), GEOMFROMTEXT('$featureWKTString')	); "; // SELECT last_insert_id() as last_insert_id;
					  */
        $query = "INSERT INTO `points` 	(`lat`, 	`long`, 	`elevation`, 	`gpx_name`, 	`gpx_sym`, 	`gpx_type`, 	`gpx_cmt`, 	`gpx_sat`, 	`gpx_fix`, 	`gpx_time`, 	`_type`, 	`_details`, 	`added_by_user_id`, 	`add_time`, spatial_geometry)
					 VALUES	({$lat}, 	{$long}, 	0, 	'', 	'', 	'', 	'', 	-1, 	'', 	NULL, 	0, 	'', 	$user_id, 	NOW(), ".(is_null($coord) ? "NULL" : "GEOMFROMTEXT('POINT({$lat_string} {$long_string})')")."); "; // SELECT last_insert_id() as last_insert_id;
				
		$stmt = $con->prepare($query);
		$res = $stmt->execute(); //$res = $stmt->fetch(PDO::FETCH_OBJ);//var_dump($res);		
		$point_id = self::get_last_inserted_id();

		
		$image->setPointid($point_id);
		
		/*$point = new Points();
		$point->setLat($caveData->cave_coords_lat);
		$point->setLong($caveData->cave_coords_lon);
		$point->setAddedbyuserid($_user_id);
		$point->setAddtime(time());
		$point->setCoords("GEOMFROMTEXT('POINT(0 0)')");
		
		$point->save();
		*/		
	}

	$image->save(); // or ->update ?
		return $point_id;
		}
	
        ///
        /// gets GPS points
        ///
        public static function get_gps_points_by_text($user_id, $text)
        {
            if (empty($user_id) || ($user_id < 0))
            {
                Utils::print_message("user_id parameter is empty", "argument error", MSG_ERROR);  
                return false;
				
                //throw new Exception('Title are empty'); //return 0;
            }
			$tp = GPSData::$tablePrefix;
			$query = "select *, 
						X(spatial_geometry) AS lat_from_point, Y(spatial_geometry)
						FROM points
						WHERE points.gpx_name like '%$text%'"; //where disabled != 1 or disabled is null
			
			//$query = "select *, AsWKB(coords) AS wkb FROM {$tp}points"; //where disabled != 1 or disabled is null
            $db_result = DB_Execute(GPSData::$ConId, $query);

            $rows = array();
            
            while ($row = mysql_fetch_array($db_result, MYSQL_ASSOC))
                $rows[/*$row["id"]*/] = $row; // 0 based index
                
            return $rows;

        }
	
	static function get_last_inserted_id()
	{	
		$con = Propel::getConnection("speogis");
		
		$get_id_query = "SELECT last_insert_id() as last_insert_id";
		$get_id_stmt = $con->prepare($get_id_query);
		$gires = $get_id_stmt->execute();
		$last_insert_id_obj = $get_id_stmt->fetch(PDO::FETCH_OBJ);
		
		return $last_insert_id_obj->last_insert_id;
		//var_dump($last_insert_id_obj->last_insert_id);
	}		
	}
?>
