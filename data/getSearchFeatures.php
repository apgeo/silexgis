<?php
	include_once 'db_common.php';	
	require_once 'GPSData.php';

	include_once('../geoPHP/geoPHP.inc');
	
	$submitData = file_get_contents('php://input'); // $HTTP_RAW_POST_DATA
	@file_put_contents(basename(__FILE__, '.php')."_input_data.log", $submitData); // file_get_contents('php://input')
		
	$text =  $_GET["q"]; //$text = $_POST["q"];
	
	//header('Content-Type: application/json');
	
	if (empty($text)) // alternate JSON request
	{
	$searchDetails = json_decode($submitData);
	//$cave->importFrom('JSON', $submitData);	
	
	$text = $searchDetails->text;
	}
	
	$_user_id = $_SESSION["id_user"];

	/*if (!GPSData::user_has_modify_right($_user_id, -1))
	{	
		echo "Access not granted";
		exit;
	}*/

	if (empty($text) || ctype_space($text))  // might need trim($caveData->cave_name) if not done on client side?
		raise_error("Search text is empty.");
	
	$results = array();
	
	$caves = CavesQuery::create()->filterByName("%".$text."%")->find();
	
	foreach ($caves as $cave)
	{
		//-- should filter by main cave entrance and use ->find() instead of ->findOne()
		$caveEntrance = CaveEntrancesQuery::create()->filterByCaveid($cave->getId())->findOne(); 
		
		$mainEntrance = null;
		$main_entrance_point_db_id = null;
		
		if (!empty($caveEntrance))
		{
			$mainEntrance = $caveEntrance;			
			$main_entrance_db_id = $mainEntrance->getId();
			
			$main_entrance_point_db_id = $mainEntrance->getPointid();
			//$caveEntrance = PointsQuery::create()->filterByCaveid($cave->getId())->findOne(); 
			
			$main_entrance_point = PointsQuery::create()->filterById($main_entrance_point_db_id)->findOne(); 
			
			$wkb = $main_entrance_point->getSpatialgeometry();
			
			//var_dump(bin2hex($wkb));
			//$unpacked = unpack('H*', $wkb);
			// var_dump($unpacked);
			//-- bit order apparently differs between x86 and x64: http://php.net/manual/ro/function.unpack.php#106041
			//var_dump($wkb);
			
			$geometry = unpack('Lpadding/corder/Lgtype/dlatitude/dlongitude', $wkb);			
			//var_dump($geometry);
						
			$wkt = 0;
			$latitude = null;
			$longitude = null;
			
			if ($geometry['gtype'] == 1) {
				$latitude = $geometry['latitude'];
				$longitude = $geometry['longitude'];			
				
				$wkt = "POINT($latitude $longitude)";
			}
			else
				throw new Exception("not supported geometry type for wkb->wkt conversion");
			
			
			//var_dump($main_entrance_point->getSpatialgeometry());			
			//wkt_to_json($wkt);
		//}
		
		$cave_result = (object) [
			'id' => $cave->getId(),
			'name' => $cave->getName(),
			'res_type' => 'cave',
			'point_db_id' => $main_entrance_point_db_id,
			'c_lat' => $latitude,
			'c_lon' => $longitude,
			
			//main_entrance_point_db_id
			//'point_id'
		];
		
		$results[] = $cave_result;
		}
	};
	
	$features = FeaturesQuery::create()->filterByName("%".$text."%")->find();

	foreach ($features as $feature)
	{	
		//-- should filter by main cave entrance and use ->find() instead of ->findOne()
		//$caveEntrance = FeaturesQuery::create()->filterByFeatureid($cave->getId())->findOne(); 
				
		$feature_result = (object) [
			'id' => "f_"+$feature->getId(),
			'name' => $feature->getName(),
			'res_type' => 'feature',
			'point_db_id' => $feature->getPointid(),
			//main_entrance_point_db_id
			//'point_id'
		];
		
		$results[] = $feature_result;
	};
	
	$points = PointsQuery::create()->filterByGpxname("%".$text."%")->find();
	//$points = get_gps_points_by_text
	foreach ($points as $point)
	{	
		//-- should filter by main cave entrance and use ->find() instead of ->findOne()
		//$caveEntrance = FeaturesQuery::create()->filterByFeatureid($cave->getId())->findOne(); 
				
		$point_result = (object) [
			'id' => "f_"+$point->getId(),
			'name' => $point->getGpxname(),
			'res_type' => 'point',
			'point_db_id' => $point->getId(),
			//main_entrance_point_db_id
			//'point_id'
		];
		
		$results[] = $point_result;
	};
	
	echo json_encode($results);
	//echo "201";
	
	// https://www.w3.org/Protocols/rfc2616/rfc2616-sec10.html
	// https://en.wikipedia.org/wiki/List_of_HTTP_status_codes
	function raise_error($errorText)
	{
		echo "400"; //?
		echo "\r\n".$errorText;
		exit;
	}
	//file_put_contents(basename(__FILE__, '.php')."input_data.log", $submitData); // file_get_contents('php://input')	
	
	function wkt_to_json($wkb)
	{	
		if (empty($wkb) || $wkb == "NULL")
			return "";
			
		// open layers wants the returned objects to have the coordinates as long, lat -> so custom formatting needs to be done before feeding geoPHP
		// note that this is probably the wrong format to use in geoPHP or other libraries because for processing of the custom long lat order
		//$geom = geoPHP::load("POINT($lat $long)"); // $geom = geoPHP::load($wkb,'wkb');
		//var_dump($wkb);
		
		$geom = geoPHP::load($wkb, 'wkt'); // $geom = geoPHP::load($wkb, 'wkt');
		//$geom_coord_order = 1;
		echo "geom";
		var_dump($geom);
		echo "x";
		return $geom->out('json');
	}
	
function big_endian_unpack ($format, $data) {
    $ar = unpack ($format, $data);
    $vals = array_values ($ar);
    $f = explode ('/', $format);
    $i = 0;
    foreach ($f as $f_k => $f_v) {
    $repeater = intval (substr ($f_v, 1));
    if ($repeater == 0) $repeater = 1;
    if ($f_v{1} == '*')
    {
        $repeater = count ($ar) - $i;
    }
    if ($f_v{0} != 'd') { $i += $repeater; continue; }
    $j = $i + $repeater;
    for ($a = $i; $a < $j; ++$a)
    {
        $p = pack ('d',$vals[$i]);
        $p = strrev ($p);
        list ($vals[$i]) = array_values (unpack ('d1d', $p));
        ++$i;
    }
    }
    $a = 0;
    foreach ($ar as $ar_k => $ar_v) {
    $ar[$ar_k] = $vals[$a];
    ++$a;
    }
    return $ar;
}	
?>