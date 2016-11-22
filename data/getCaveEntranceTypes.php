<?php
    include_once 'db_common.php';

	
	$caveEntranceTypes = EntranceTypesQuery::create()->find();

	echo $caveEntranceTypes->toJSON();
?>