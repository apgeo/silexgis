<?php
    include_once 'db_common.php';
	include_once 'db_utilities.php';
	
	$_user_id = $_SESSION["id_user"];
	
	$mapViews = MapViewsQuery::create()->find(); //-- filter by user id

	$results = array();
	
	foreach ($mapViews as $mv)
	{
		$center_geometry_wkb = $mv->getCentergeometry();
				
		$center_geometry = DbUtils::parse_wkb($center_geometry_wkb);
		
		if (empty($center_geometry) || $center_geometry == "NULL")
			throw new Exception("empty geometry");
			//continue;
		
		$coordinates = (object) [
			$center_geometry->latitude,
			$center_geometry->longitude
			];
		
		$properties = json_decode($mv->getProperties());
		
		//$mv->center_geometry_coordinates = $coordinates;
		//$mv->setProperties($properties);
		
		$results[] = (object) [
			'id' => $mv->getId(),
			'mapview_name' => $mv->getName(),
			'center_geometry_coordinates' => $coordinates,
			'is_default' => $mv->getIsdefault(),
			'properties' => $properties
		];
	}
		
	//echo $mapViews->toJSON();
	echo json_encode($results);
?>