<?php
	//session_start();
    require_once '../auth.php';

	require_once '../vendor/Propel/runtime/lib/Propel.php';
	Propel::init("../db/propel_1_6_pr/build/conf/speogis-conf.php");
	set_include_path("../db/propel_1_6_pr/build/classes" . PATH_SEPARATOR . get_include_path());
	
	require_once '../GPSData.php';

	$submitData = file_get_contents('php://input'); // $HTTP_RAW_POST_DATA
	@file_put_contents(basename(__FILE__, '.php')."input_data.log", $submitData); // file_get_contents('php://input')
	
	$caveData = json_decode($submitData);
	//$cave->importFrom('JSON', $submitData);	
	
	$cave_id = $caveData->cave_id; //-- might be empty if insert used instead of edit
	$_user_id = $_SESSION["id_user"];
	
	if (!GPSData::user_has_modify_right($_user_id, $cave_id))
	{	
		echo "Access not granted";
		exit;
	}
	
	if (empty($caveData->cave_name) || ctype_space($caveData->cave_name))  // might need trim($caveData->cave_name) if not done on client side?
		raise_error("Cave name is empty.");
		
	if (empty($caveData->cave_type) || ctype_space($caveData->cave_type))
		raise_error("Cave type is empty.");
	
	
	$mainCaveEntrance = null;
	$cave = null;
	
	if (!empty($cave_id))
	{
		$cave = CavesQuery::create()->findPK($cave_id);				
	}
	else
	{
		$cave = new Caves(); 		
		$mainCaveEntrance = new CaveEntrances();
	}
		
	
	
	$cave->setName($caveData->cave_name);
	$cave->setDescription($caveData->cave_description);
	$cave->setTypeid($caveData->cave_type);
	$cave->setLocationidentifier($caveData->cave_identifier);
	
	$cave->save(); // or ->update ?
	
	
	// add new default main entrance if in insert mode (new cave)
	if (empty($cave_id)) //!isset($mainCaveEntrance)) // insert
	{	
		$con = Propel::getConnection("speogis"); // BookPeer::DATABASE_NAME
		
        $query = "INSERT INTO `points` 	(`lat`, 	`long`, 	`elevation`, 	`coords`, 	`gpx_name`, 	`gpx_sym`, 	`gpx_type`, 	`gpx_cmt`, 	`gpx_sat`, 	`gpx_fix`, 	`gpx_time`, 	`_type`, 	`_details`, 	`added_by_user_id`, 	`add_time`	)
					  VALUES	('{$caveData->cave_coords_lat}', 	'{$caveData->cave_coords_lon}', 	'{-1}', 	GEOMFROMTEXT('POINT({$caveData->cave_coords_lat} {$caveData->cave_coords_lon})'), '', 	'', 	'', 	'', 	-1, 	'', 	'', 	0, 	'', 	$_user_id, 	NOW()	); "; // SELECT last_insert_id() as last_insert_id;
		
		//var_dump($query);
		$stmt = $con->prepare($query);
		$res = $stmt->execute(); //$res = $stmt->fetch(PDO::FETCH_OBJ);//var_dump($res);		
		$point_id = get_last_inserted_id();

		
		$mainCaveEntrance->setName("default");
		$mainCaveEntrance->setEntrancetype(0);
		$mainCaveEntrance->setIsmainentrance(true);
		$mainCaveEntrance->setCaveid($cave->getId());
		$mainCaveEntrance->setPointid($point_id);
		
		$mainCaveEntrance->save();
		
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
	
	// https://www.w3.org/Protocols/rfc2616/rfc2616-sec10.html
	// https://en.wikipedia.org/wiki/List_of_HTTP_status_codes
	function raise_error($errorText)
	{
		echo "400"; //?
		echo "\r\n".$errorText;
		exit;
	}
	
	function get_last_inserted_id()
	{	
		$con = Propel::getConnection("speogis");
		
		$get_id_query = "SELECT last_insert_id() as last_insert_id";
		$get_id_stmt = $con->prepare($get_id_query);
		$gires = $get_id_stmt->execute();
		$last_insert_id_obj = $get_id_stmt->fetch(PDO::FETCH_OBJ);
		
		return $last_insert_id_obj->last_insert_id;
		//var_dump($last_insert_id_obj->last_insert_id);
	}
	//file_put_contents(basename(__FILE__, '.php')."input_data.log", $submitData); // file_get_contents('php://input')
	
	// \Propel::getConnection()->getLastExecutedQuery() // Returns fully qualified SQL
	// $rawSql = (new BookQuery)::create()->filterById(25)->toString();
?>