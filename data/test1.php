<?php
	include_once 'db_common.php';	
	require_once 'GPSData.php';

	include_once('../geoPHP/geoPHP.inc');
	
			$main_entrance_point_db_id = 1;
			
			$main_entrance_point = PointsQuery::create()->filterById($main_entrance_point_db_id)->findOne(); 
			
			echo "xz mep=$main_entrance_point zx ";

			var_dump($main_entrance_point);
			
			echo "<br/>";
			echo "<br/>";
			echo "z_";
			//print_r(unpack("cchars/nint", $main_entrance_point->getSpatialgeometry()));
			var_dump(unpack("c3chars/nint", $main_entrance_point->getSpatialgeometry()));
			echo "_z";
			echo "<br/>";
			//print_r($main_entrance_point->getSpatialgeometry());
			echo "<br/>";			
			var_dump($main_entrance_point->getSpatialgeometry());
			
			echo "<br/><br/>";
			
			
				$conid = DBCon::open_connection();    
			
				GPSData::$ConId = DBCon::get_connection_id();

				$query = "SELECT spatial_geometry, points.id FROM points WHERE points.id = 1";
				$db_result = DB_Execute(GPSData::$ConId, $query);
				
				$rows = array();
            
            while ($row = mysql_fetch_array($db_result, MYSQL_ASSOC))
			{
				//$row["point_type"] = "feature";
                $rows[/*$row["id"]*/] = $row; // 0 based index
				
				var_dump($row);
			}

				DBCon::close_connection();    
			
			wkb_to_json($main_entrance_point->getSpatialgeometry());
	
		
	function wkb_to_json($wkb)
	{	
		if (empty($wkb) || $wkb == "NULL")
			return "";
			
		// open layers wants the returned objects to have the coordinates as long, lat -> so custom formatting needs to be done before feeding geoPHP
		// note that this is probably the wrong format to use in geoPHP or other libraries because for processing of the custom long lat order
		//$geom = geoPHP::load("POINT($lat $long)"); // $geom = geoPHP::load($wkb,'wkb');
		//var_dump($wkb);
		
		$geom = geoPHP::load($wkb, 'wkt'); // $geom = geoPHP::load($wkb, 'wkt');
		//$geom_coord_order = 1;
		var_dump($geom);
		print_r("xx");
		return $geom->out('json');
	}
?>