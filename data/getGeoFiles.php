<?php
    include_once 'db_common.php';

	$uploads_path = "../uploads/";
	$geofiles = GeofilesQuery::create()->find();
		
	//count($geofiles->getData())
	$index = 0;
	
	foreach ($geofiles as $key => $geofile)
	{		
		if (!file_exists($uploads_path.($geofile->getFileName())))
			$geofiles->remove($index);
		
		$index++;
	}
	
	//var_dump("count = ".(count($geofiles->getData()))." ");
		
	echo $geofiles->toJSON();
?>