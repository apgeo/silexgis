<?php
    include_once 'db_common.php';

	$_user_id = $_SESSION["id_user"];
	

	// $submitData = file_get_contents('php://input'); // $HTTP_RAW_POST_DATA
	// $searchDetails = json_decode($submitData);
	// $searchDetails->cave_id

	//$caves = CavesQuery::create()->findByUserid($_user_id);

	$cave_id = $_GET["cave_id"];
	
	if (!empty($cave_id)) ;

	$caves = CavesQuery::create()->findById($cave_id); // findPK
	
	$caveEntrances = CaveEntrancesQuery::create()
  		->filterByCaveid($cave_id)  		
  		->find();
	
	foreach ($caveEntrances as $caveEntrance)
	{
		$point = PointsQuery::create()
  			->filterById($caveEntrance->getPointid())
  			//->find()
			->findOne();
		
		// var_dump($points);
		// echo "<br/><br/>";
		// var_dump($point);
		// echo "<br/><br/>";
		
		$pointObject = (object) [
			// 'id' => $point->getId(),
			'Lat' => $point->getLat(),
			'Long' => $point->getLong(),
			'Elevation' => $point->getElevation(),
		];
		
		$caveEntrance->setVirtualColumn('Point', 
			// json_decode($point->toJSON())
			$pointObject
		);
	}
	// var_dump($caveEntrances->getData());
	// $cave = array_values($caves)[0];

	$cave = $caves->getFirst();

	// foreach ($caves as $cave)
	// {
		// $caveEntrances->getData()
		//-- workaround for setting object child
		$cave->setVirtualColumn('CaveEntrances', json_decode($caveEntrances->toJSON()));
		echo $cave->toJSON();
		// break;
	// }
	
	//$cave->caveEntrances = $caveEntrances;

	// echo $cave->toJSON();
?>