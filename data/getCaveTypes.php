<?php
    include_once 'db_common.php';

	
	$caveTypes = CaveTypesQuery::create()->find();

	echo $caveTypes->toJSON();
?>