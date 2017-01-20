<?php
    include_once 'db_common.php';
	
	$featureTypes = FeatureTypesQuery::create()->find();
	echo $featureTypes->toJSON();
?>