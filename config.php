<?php       
    //require_once ("constants.php");
    //require_once("admin_links.php");
    
    define('DB_HOST', 'localhost');
    define('DB_NAME', 'speogis');
    define('DB_USER', 'root');
    define('DB_PASS', '');
    define('APPLICATION_ROOT', dirname(__FILE__) . DIRECTORY_SEPARATOR); // define('DOMAIN_ROOT', 'http://'.$_SERVER['HTTP_HOST']);
	
	//error_reporting(E_ERROR);
    
    define("DEBUG", 1); // define("DEBUG", 1);
    $GLOBALS['LOG_TO_SCREEN'] = 1; define('LOG_TO_SCREEN', 1);
            
    if (DEBUG)
    {
		@error_reporting(E_ALL ^ E_DEPRECATED ^ E_STRICT); //error_reporting(E_ALL);
        //@error_reporting(E_ALL  ^ E_DEPRECATED); //error_reporting(E_ALL);
        
		//ini_set("display_errors", 1); //ini_set("display_errors", 1);
        //@ini_set('error_reporting', E_ALL ^ E_DEPRECATED ^ E_STRICT); //ini_set('error_reporting', E_ALL);
		
		// @ini_set('error_reporting', E_ALL  ^ E_DEPRECATED); //ini_set('error_reporting', E_ALL);
	
//error_reporting(E_ALL ^ (E_NOTICE | E_WARNING | E_DEPRECATED));
//ini_set('error_reporting', E_ALL ^ (E_NOTICE | E_WARNING | E_DEPRECATED));
        //error_reporting(E_ALL);
        //ini_set('log_errors',False);
        //ini_set('html_errors',FALSE);
        //ini_set('error_log','error_log.txt');
        //ini_set('display_errors',FALSE);
    }
    else
    {
        error_reporting(0); //error_reporting(E_ALL);
        ini_set("display_errors", 0); //ini_set("display_errors", 1);
        ini_set('error_reporting', 0); //ini_set('error_reporting', E_ALL);
    }

    //ini_set('allow_call_time_pass_reference', 1);

    ini_set('max_execution_time', 9000);
    ini_set('memory_limit', '128M');

    date_default_timezone_set("GMT");
    
    define('USER_INTERFACE_TIMEZONE', '+02:00'); // the timezone that will be used to convert the dates from the database to (they are stored as GMT)
    //ini_set("session.use_only_cookies", 1);

    //@header('Content-Type: text/html; charset=utf-8');
	
	//global $application_url_root;
	$application_url_root = "/speogis";
	define('ROOTPATH', $_SERVER['DOCUMENT_ROOT'].$application_url_root);
	define('WEBROOT', "http://".$_SERVER['HTTP_HOST'].$application_url_root."/");	
	// define('WEBROOT', "http://".$_SERVER['HTTP_HOST'].dirname($_SERVER['PHP_SELF'])."/");
	//$web_root = "http://".$_SERVER['HTTP_HOST'].dirname($_SERVER['PHP_SELF'])."/";
	//$root = realpath($_SERVER["DOCUMENT_ROOT"]).$application_url_root;
	//echo "config application_url_root = $application_url_root";
	
/*$protocol = $_SERVER['HTTPS'] == '' ? 'http://' : 'https://';
$folder = $protocol . $_SERVER['HTTP_HOST'];

$protocol = $_SERVER['HTTPS'] == '' ? 'http://' : 'https://';
$folder = $protocol . $_SERVER['HTTP_HOST'] . '/' . basename($_SERVER['REQUEST_URI']);
*/
?>
