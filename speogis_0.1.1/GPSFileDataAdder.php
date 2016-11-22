<?php
	include_once 'db_interface.php';
    include_once 'config.php';
	include_once 'GPSData.php';	
	include_once('geoPHP/geoPHP.inc');
	
// Include the main Propel script
require_once 'vendor/Propel/runtime/lib/Propel.php';

// Initialize Propel with the runtime configuration
Propel::init("db/propel_1_6_pr/build/conf/speogis-conf.php");

// Add the generated 'classes' directory to the include path
set_include_path("db/propel_1_6_pr/build/classes" . PATH_SEPARATOR . get_include_path());
	
    class GPSFileDataAdder
    {        
        /*public static function addGPSFileData()
		{
		}
		*/
		public function saveGPSFileData($file_path)
		{
		$user_id = 1;

		$conid = DBCon::open_connection();    
		GPSData::$ConId = DBCon::get_connection_id();								
		
		//$res = GPSData::add_file($file_path, $user_id);
		
		$md5_hash = md5_file($file_path);
		$file_size = filesize($file_path);

		$f = (new Files())
			->setFileName($file_path)
			->setIdUser($user_id)
			->setAddTime(time())
			->setType('undefined')
			->setSize($file_size)
			->setMd5Hash($md5_hash)
			->save();
		
		$parts = explode('.',$file_path);
    
	if ($parts[0]) 
	{
		$ext = pathinfo($file_path, PATHINFO_EXTENSION);
		$format = $ext;
		
		//var_dump($parts);
		
		$value = file_get_contents($file_path); // './input/'.$file_path
		//print '---- Testing '.$file_path."\n";
      
			$geometry = geoPHP::load($value, $format);
			
			$root_components = $geometry->getComponents();
			
			$points = array();
			foreach($root_components as $comp)
				if ($comp instanceOf Point)
					{
					$points[] = $comp;
					//print_r($comp);
					//echo $comp;
					//echo "<br/><br/>";
					}
				
			//print_r($geometry);
			//echo "<br/><br/>";
			//var_dump($geometry);
			
			//$geometry->getComponents();
	  //test_adapters($geometry, $format, $value);
      //test_methods($geometry);
      //test_geometry($geometry);
      //test_detection($value, $format, $file_path);
    

			//$conid = DBCon::open_connection();    
			
			//GPSData::$ConId = DBCon::get_connection_id();						
			
			foreach ($points as $point)
			{
				//var_dump($point);
				GPSData::add_gps_point($point, $user_id);
			}
			
			DBCon::close_connection();    


		/*foreach ($points as $point)
			{
				$p = new Points();
				$p->setLat($point->coords[0]);
				$p->setLong($point->coords[1]);
				$p->setLong($point->coords[2]);
				$p->setCoords("GEOMFROMTEXT('POINT({$point->coords[0]} {$point->coords[1]})')");
				$p->save();
				
				GPSData::add_gps_point($point, $user_id);
			}
			*/
			return true;
			}
		}
	}
?>
