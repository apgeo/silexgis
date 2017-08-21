<?php
	include_once 'db_common.php';
	require_once 'GPSData.php';

	include_once('../geoPHP/geoPHP.inc');
	require_once 'db_utilities.php';
	
	$submitData = file_get_contents('php://input'); // $HTTP_RAW_POST_DATA
	@file_put_contents(basename(__FILE__, '.php')."input_data.log", $submitData); // file_get_contents('php://input')
	
	$caveEntranceData = json_decode($submitData);
	//$cave->importFrom('JSON', $submitData);	
	
	
	$_user_id = $_SESSION["id_user"];

	if (!DbUtils::checkParameter(@$caveEntranceData->cave_entrance_name, "string", "Cave entrance name is empty.") ||
		!DbUtils::checkParameter(@$caveEntranceData->cave_entrance_type, "string", "Cave entrance type is empty.") ||
		!DbUtils::checkParameter(@$caveEntranceData->cave_entrance_cave_id, "int", "Cave id is empty.")
		)
		;//exit;
	
	$cave_entrance_id = null;
	
	if (!empty($caveEntranceData->cave_entrance_id))
		$cave_entrance_id = $caveEntranceData->cave_entrance_id; //-- might be empty if insert used instead of edit
	
	$cave_id = $caveEntranceData->cave_entrance_cave_id;
		
	/*if (!GPSData::user_has_modify_right($_user_id, $cave_id))
	{	
		echo "Access not granted";
		exit;
	}
	*/
	
	$caveEntrance = null;
	$cave_entrance_existing_point_id = null;
	
	if (!empty($cave_entrance_id))
	{
		$caveEntrance = CaveEntrancesQuery::create()->findPK($cave_entrance_id);

		if (empty($caveEntrance))
		{
			// could not find a point for this cave entrance which means it is orphan - problem in the db
			echo "404"; //-- should maybe send a dedicated return error code or throw an error
			return;
		}

		$cave_entrance_existing_point_id = $caveEntrance->getPointId();
	}
	else
	{		
		$caveEntrance = new CaveEntrances();
	}
		
	
	// add new default main entrance if in insert mode (new cave)
	if (!empty($cave_id)) //!isset($mainCaveEntrance)) // insert
	{	
		$con = Propel::getConnection("speogis"); // BookPeer::DATABASE_NAME
		$query = "";

		$featureWKTString = null;
		$featureGeoJsonString = null;
		
		$featureGeoJsonString = $caveEntranceData->cave_entrance_feature_string;
		
		if(!empty($featureGeoJsonString))
			$featureWKTString = DbUtils::json_to_wkt($featureGeoJsonString);
	
		// change to INSERT ... ON DUPLICATE KEY UPDATE
		if (empty($cave_entrance_id))
		{
        $query = "INSERT INTO `points` 	(`lat`, 	`long`, 	`elevation`, 	`gpx_name`, 	`gpx_sym`, 	`gpx_type`, 	`gpx_cmt`, 	`gpx_sat`, 	`gpx_fix`, 	`gpx_time`, 	`_type`, 	`_details`, 	`added_by_user_id`, 	`add_time` ".(!empty($featureWKTString) ? ",	spatial_geometry" : "")."	)
					  VALUES	('{$caveEntranceData->cave_entrance_coords_lat}', 	'{$caveEntranceData->cave_entrance_coords_lon}',	NULL, 	'', 	'', 	'', 	'', 	-1, 	'', 	NULL, 	0, 	'', 	$_user_id, 	NOW() ".(!empty($featureWKTString) ? ", GEOMFROMTEXT('$featureWKTString')" : "")."	); "; // SELECT last_insert_id() as last_insert_id;
		}
		else
		{			
        $query = "UPDATE `points` SET
			`lat` = '{$caveEntranceData->cave_entrance_coords_lat}',
			`long` = '{$caveEntranceData->cave_entrance_coords_lon}',
			`elevation` = NULL,
			`gpx_name` = '',
			`gpx_sym` = '',
			`gpx_type` = '',
			`gpx_cmt` = '',
			`gpx_sat` = -1,
			`gpx_fix` = '',
			`gpx_time` = NULL, 	
			`_type` = 0,
			`_details` = '',
			`added_by_user_id` = $_user_id,
			".(!empty($featureWKTString) ? "spatial_geometry = GEOMFROMTEXT('$featureWKTString'), " : "")."
			`update_time` = NOW()
			WHERE id = $cave_entrance_existing_point_id"; // SELECT last_insert_id() as last_insert_id;			
		}

/*        $query = "INSERT INTO `points` 	(`lat`, 	`long`, 	`elevation`, 	`coords`, 	`gpx_name`, 	`gpx_sym`, 	`gpx_type`, 	`gpx_cmt`, 	`gpx_sat`, 	`gpx_fix`, 	`gpx_time`, 	`_type`, 	`_details`, 	`added_by_user_id`, 	`add_time` ".(!empty($featureWKTString) ? ",	spatial_geometry" : "")."	)
					  VALUES	('{$caveEntranceData->cave_coords_lat}', 	'{$caveEntranceData->cave_coords_lon}', 	'{-1}', 	GEOMFROMTEXT('POINT({$caveEntranceData->cave_coords_lat} {$caveEntranceData->cave_coords_lon})'), '', 	'', 	'', 	'', 	-1, 	'', 	'', 	0, 	'', 	$_user_id, 	NOW() ".(!empty($featureWKTString) ? ", GEOMFROMTEXT('$featureWKTString')" : "")."	); "; // SELECT last_insert_id() as last_insert_id;
*/
		//var_dump($query);
		$stmt = $con->prepare($query);
		$res = $stmt->execute(); //$res = $stmt->fetch(PDO::FETCH_OBJ);//var_dump($res);		

		$point_id = NULL;

		if (empty($cave_entrance_existing_point_id)) // if there was an insert
			$point_id = DbUtils::get_last_inserted_id();
		else // there was an update
			$point_id = $cave_entrance_existing_point_id;

		
		$caveEntrance->setName($caveEntranceData->cave_entrance_name);
		$caveEntrance->setEntrancetype($caveEntranceData->cave_entrance_type);
		//$caveEntrance->setIsmainentrance(true);
		$caveEntrance->setCaveid($cave_id);
		$caveEntrance->setDescription($caveEntranceData->cave_entrance_description);
		$caveEntrance->setPointid($point_id);
		
		$caveEntrance->save();
		
		/*$point = new Points();
		$point->setLat($caveData->cave_coords_lat);
		$point->setLong($caveData->cave_coords_lon);
		$point->setAddedbyuserid($_user_id);
		$point->setAddtime(time());
		$point->setCoords("GEOMFROMTEXT('POINT(0 0)')");
		
		$point->save();
		*/		
	}
	//print_r($caveData);
	
	echo "201";
	
	// \Propel::getConnection()->getLastExecutedQuery() // Returns fully qualified SQL
	// $rawSql = (new BookQuery)::create()->filterById(25)->toString();
?>