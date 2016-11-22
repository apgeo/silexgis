<?php
    include_once 'db_common.php';

	$_user_id = $_SESSION["id_user"];
	
	//$caves = CavesQuery::create()->find();
	//$caves = CavesQuery::create()->findByUserid($_user_id);
	$caves = CavesQuery::create()->find();
	
	echo $caves->toJSON();
?>