<?php	
	//session_start();	
	require_once dirname(__DIR__).'/config.php';
    $root = realpath($_SERVER["DOCUMENT_ROOT"]).$application_url_root;
	
	require_once "$root/auth.php";

	require_once "$root/vendor/Propel/runtime/lib/Propel.php";
	Propel::init("$root/db/propel_1_6_pr/build/conf/speogis-conf.php");
	set_include_path("$root/db/propel_1_6_pr/build/classes" . PATH_SEPARATOR . get_include_path());	

	// echo "$root/db/propel_1_6_pr/build/classes" . PATH_SEPARATOR . get_include_path();
?>