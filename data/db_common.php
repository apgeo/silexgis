<?php
	//session_start();
    $root = realpath($_SERVER["DOCUMENT_ROOT"])."/speogis";
	
	require_once "$root/auth.php";

	require_once "$root/vendor/Propel/runtime/lib/Propel.php";
	Propel::init("$root/db/propel_1_6_pr/build/conf/speogis-conf.php");
	set_include_path("$root/db/propel_1_6_pr/build/classes" . PATH_SEPARATOR . get_include_path());
?>