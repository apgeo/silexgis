<?php
    include_once 'db_common.php';
	
	$con = Propel::getConnection("speogis");
	$sql = "SELECT * FROM feature_types WHERE disabled IS NULL or disabled = 0 ORDER BY ISNULL(order_index), order_index";
	$stmt = $con->prepare($sql);
	
	$stmt->execute(
		//array(':name' => 'Austen')
	);	
	// $featureTypes = FeatureTypesQuery::create()
	// 	->orderBy('ISNULL(index)')
	// 	->orderByIndex()
	// 	->find();
	
	$formatter = new PropelObjectFormatter();
	$formatter->setClass('FeatureTypes');
	$featureTypes = $formatter->format($stmt);
	
	echo $featureTypes->toJSON();
?>