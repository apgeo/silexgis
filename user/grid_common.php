<?php
	//session_start();
    $root = realpath($_SERVER["DOCUMENT_ROOT"])."/speogis";
	
	require_once "$root/auth.php";
	//echo "<header__>";
	include_once "$root/header.php";
	
  define ("DATAGRID_DIR", "../datagrid_class_4_2_8/");
  define ("PEAR_DIR", "../datagrid_class_4_2_8/pear/");
  
  require_once(DATAGRID_DIR.'datagrid.class.php');
  require_once(PEAR_DIR.'PEAR.php');
  require_once(PEAR_DIR.'DB.php');

  require_once("$root/config.php");
  
  //include_once "$root/header.php"; // include_once 'db_interface.php'; require_once('utilities.php'); 
  //require_once('utilities.php'); include_once 'db_interface.php'; include_once 'data_interface.php'; require_once 'languages.php'; 
	//echo "$root/header.php"; exit;
	//GG 
	//echo "</header__>";
	//echo "</body></html>";
	//exit;
	//require_once "$root/vendor/Propel/runtime/lib/Propel.php";
	//Propel::init("$root/db/propel_1_6_pr/build/conf/speogis-conf.php");
	//set_include_path("$root/db/propel_1_6_pr/build/classes" . PATH_SEPARATOR . get_include_path());
	
	//<!--<script>
	//$(document).ready(function() {
	//});
	//</script>-->
?>
<script type="text/javascript" src="<?=WEBROOT ?>scripts/user_common.js"></script>