<?php
    include_once 'db_common.php';

	
	$geofiles = GeofilesQuery::create()->find();
	
	echo $geofiles->toJSON();
?>