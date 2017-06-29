<?php
session_start();
include_once('config.php');
//include_once("conf/dbconnection.php");

require_once 'DbData.php';

$username = ($_POST["username"]) ? htmlentities($_POST["username"]):"";
$password = ($_POST["password"]) ? htmlentities($_POST["password"]):"";

if($username=="") {
    $_SESSION["login_error"] = "Please insert username!";
    header("Location: login.php");
} else if($password=="") {
    $_SESSION["login_error"] = "Please insert password!";
    header("Location: login.php");
} else {

	$conid = DBCon::open_connection();    
			
	GPSData::$ConId = DBCon::get_connection_id();

	$res = GPSData::get_user($username, $password);
	//echo "count ".count($res);
	
	$err = "";
	
	//session_start();
	
	if (is_array($res))
	{	
	if ( (count($res) == 1))
	{		
		 
		$userObj = $res[0];
        $_SESSION["username"] = $userObj["username"];
        $_SESSION["logged"] = 1;
        //$_SESSION["email"] = $rw["email"];
        $_SESSION["id_user"] = $userObj["id"];
    }
    else 
	{
		$err ="Username and password are invalid!";
	}
	
	    $_SESSION["login_error"] = ($err!="") ? $err:"";
		
        if($_SESSION["login_error"]!="") {
			//echo "error";
			
			//-- redirect breaks if first login is failed for subsequent attempts
			if(isset($_REQUEST['redirurl']) && !empty($_REQUEST['redirurl']))
            	header("Location: login.php?redirurl=".urldecode($_REQUEST['redirurl']));
			else
				header("Location: login.php");
        } else {
			//echo "logged in"; exit;            
						
			$url = "index.php";
			
			if(isset($_REQUEST['redirurl']) && !empty($_REQUEST['redirurl'])) 
			{
				$url = $_REQUEST['redirurl']; // holds url for last page visited.

				if (strpos($url, "login.php"))
					$url = "index.php";
			}
			else 
				$url = "index.php";
						
			//echo "logged in redirect: _{$url}_"; exit;
			header("Location: $url");
        }

            /* Error */
			
            //printf("Prepared Statement Error: %s\n", $db_conn->error);
    
	}
    else
		printf("Problem in authentication");
	
	DBCon::close_connection();
}

?>