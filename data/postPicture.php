<?php
	include_once 'db_common.php';
	
	require_once 'GPSData.php';	
	include_once('../geoPHP/geoPHP.inc');

	require_once('PictureFileSaver.php');

	//var_dump($_POST);
	//var_dump($_FILES);
	
	$submitData = $_POST["form_data"]; // $HTTP_RAW_POST_DATA
	// $submitData = file_get_contents('php://input'); // $HTTP_RAW_POST_DATA
	@file_put_contents(basename(__FILE__, '.php')."input_data.log", $submitData); // file_get_contents('php://input')
	
	$pictureData = json_decode($submitData);
	//$feature->importFrom('JSON', $submitData);
	
	$picture_id = $pictureData->picture_id; //-- might be empty if insert used instead of edit
	$_user_id = $_SESSION["id_user"];
	
	/* //-- if (!GPSData::user_has_modify_right($_user_id, $feature_id))
	{	
		echo "Access not granted";
		exit;
	}
	*/
	//if (empty($pictureData->feature_name) || ctype_space($featureData->feature_name))  // might need trim($featureData->feature_name) if not done on client side?
	//	raise_error("Feature name is empty.");
			
	if (empty($_FILES['file']['name']) || ctype_space($_FILES['file']['name']))  // might need trim($featureData->feature_name) if not done on client side?
		raise_error("No file specified.");
	
	$image = null;
	
	if (!empty($picture_id))
	{
		$image = ImagesQuery::create()->findPK($picture_id);				
	}
	else
	{
		$image = new Images();
	}
	
	//$file_path = "";//$pictureData->
	
	$file_name = savePictureFile();
	$thumbnail_file_name = "thumb".$_FILES['file']['name']; //- should be returned by savePictureFile
	
	//$image->setName($featureData->feature_name);
	$image->setDescription($pictureData->picture_description);	
	$image->setFilepath($file_name); //-- filePath relative or full?
	$image->setUserid($_user_id);
	$image->setThumbfilepath($thumbnail_file_name);	
	$image->setAddtime(time());

	// add new point if in insert mode (new cave)
	if (empty($picture_id)) //!isset($mainCaveEntrance)) // insert
	{	
		$con = Propel::getConnection("speogis"); // BookPeer::DATABASE_NAME

		/*$pictureGeoJsonString = $pictureData->feature_string;
		$featureWKTString = json_to_wkt($pictureGeoJsonString);
		
        $query = "INSERT INTO `points` 	(`lat`, 	`long`, 	`elevation`, 	`coords`, 	`gpx_name`, 	`gpx_sym`, 	`gpx_type`, 	`gpx_cmt`, 	`gpx_sat`, 	`gpx_fix`, 	`gpx_time`, 	`_type`, 	`_details`, 	`added_by_user_id`, 	`add_time`, spatial_geometry	)
					  VALUES	('0', 	'0', 	'{-1}', 	GEOMFROMTEXT('POINT(0 0)'), '', 	'', 	'', 	'', 	-1, 	'', 	'', 	0, 	'', 	$_user_id, 	NOW(), GEOMFROMTEXT('$featureWKTString')	); "; // SELECT last_insert_id() as last_insert_id;
					  */
        $query = "INSERT INTO `points` 	(`lat`, 	`long`, 	`elevation`, 	`gpx_name`, 	`gpx_sym`, 	`gpx_type`, 	`gpx_cmt`, 	`gpx_sat`, 	`gpx_fix`, 	`gpx_time`, 	`_type`, 	`_details`, 	`added_by_user_id`, 	`add_time`, spatial_geometry	)
					 VALUES	('{$pictureData->point_coords_lat}', 	'{$pictureData->point_coords_lon}', 	'{-1}', 	'', 	'', 	'', 	'', 	-1, 	'', 	'', 	0, 	'', 	$_user_id, 	NOW(), GEOMFROMTEXT('POINT({$pictureData->point_coords_lat} {$pictureData->point_coords_lon})')	); "; // SELECT last_insert_id() as last_insert_id;
		
		//var_dump($query);
		$stmt = $con->prepare($query);
		$res = $stmt->execute(); //$res = $stmt->fetch(PDO::FETCH_OBJ);//var_dump($res);		
		$point_id = get_last_inserted_id();

		
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
	
	echo "201";
	
	// https://www.w3.org/Protocols/rfc2616/rfc2616-sec10.html
	// https://en.wikipedia.org/wiki/List_of_HTTP_status_codes
	function raise_error($errorText)
	{
		echo "400"; //?
		echo "\r\n".$errorText;
		exit;
	}
	
	function json_to_wkt($json) {
		$geom = geoPHP::load($json,'json');
		return $geom->out('wkt');
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