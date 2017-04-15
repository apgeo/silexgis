<?php  

	
	require_once("config.php");
	//$root = realpath($_SERVER["DOCUMENT_ROOT"]).$application_url_root;
	
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
		//var_dump($_SERVER);
		//exit;
	   
		$url_parameters = "";
	   
		if (isset($_SERVER["REQUEST_URI"]))
			$url_parameters = "?".http_build_query(array('redir' => $_SERVER["REQUEST_URI"]));
		
		header('location: '.$application_url_root.'/login.php'.$url_parameters);
		exit;
	}

    //if (isset($_SESSION['details']))
    //    $adm_details = &$_SESSION['details'];
    //else
    //   header('location: login.php');
    ?>