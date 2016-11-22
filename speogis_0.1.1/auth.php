<?php  

    $adm_details = null; 
	
	if(!isset($_SESSION)) session_start();

    //require_once("config.php");
	
		//print_r($_SESSION);
//exit;

    if (isset($_SESSION["logged"]))
	{
		//echo "logged"; exit;

        $adm_details = &$_SESSION["username"];
		}
    else		
	{
		//echo "not logged"; exit;

       header('location: login.php');
	   exit;
	   }

    //if (isset($_SESSION['details']))
    //    $adm_details = &$_SESSION['details'];
    //else
    //   header('location: login.php');
    ?>