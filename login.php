<?php

function get_browser_ex()
{
    $browser = '';
    $ua = strtolower($_SERVER['HTTP_USER_AGENT']);
    if (preg_match('~(?:msie ?|trident.+?; ?rv: ?)(\d+)~', $ua, $matches)) $browser = 'ie ie'.$matches[1];
    elseif (preg_match('~(safari|chrome|firefox)~', $ua, $matches)) $browser = $matches[1];

    return $browser;
}

if (preg_match('/MSIE\s(?P<v>\d+)/i', @$_SERVER['HTTP_USER_AGENT'], $B) || (strpos(get_browser_ex(), "ie11") !== false)) { //&& $B['v'] <= 8
    echo "SilexGIS nu functioneaza pe Internet Explorer. 
    Recomandarea este sa folsiti browserul <a href='https://www.google.com/chrome/index.html'>Google Chrome</a> sau <a href='https://www.mozilla.org/ro/firefox/new/'>Mozilla Firefox</a>.
    <br/>
    <br/>
    SilexGIS is not working with Internet Explorer. 
    The recommended browser is <a href='https://www.google.com/chrome/index.html'>Google Chrome</a> or <a href='https://www.mozilla.org/en-US/en/firefox/new/'>Mozilla Firefox</a>.
    ";
    exit;
}

//include_once("conf/config.php");
if (@$_SESSION["logged"]) unset($_SESSION["logged"]);
$err = @$_SESSION["login_error"];
if (@$_SESSION["login_error"]) unset($_SESSION["login_error"]);

$redirect_url = "";

if (isset($_REQUEST["redir"]))
	$redirect_url = $_REQUEST["redir"];
else
if (isset($_SERVER['HTTP_REFERER']))
	$redirect_url = $_SERVER['HTTP_REFERER'];

$login_display =<<<END
<!DOCTYPE html>

<!-- *********************************************************************************************

Login form - George Barbu (modificat)

********************************************************************************************* -->

<html lang="en">

<head>

	<meta charset="utf-8">

	<title>Login form</title>

	<link rel="stylesheet" href="css/style.css" media="screen">

	<style>
    body{ 
        center;margin: 0 auto;width: 960px;padding-top: 50px
    }
    .footer{
        margin-top:50px;text-align:center;color:#625;font:bold 14px Arial
    }
    .footer a{
        color:#999;text-decoration:none
    }

   
    </style>
    <meta name="robots" content="noindex,follow" />
</head>

<body>
<form id="login" action="dologin.php" method="post">
	<input type="hidden" name="redirurl" value="
END
	.$redirect_url.
<<<END
" />
	<div>
	</div>
    <h1>Log in</h1>
    <fieldset id="inputs">
        <input id="username" name="username" type="text" placeholder="Username" autofocus required>   
        <input id="password" name="password" type="password" placeholder="Password" required>
    </fieldset>
    <fieldset id="actions">
        <input type="submit" id="submit" value="Log in">
    </fieldset>
    $err    
</form>
</body>
</html>
END;

echo $login_display;
?>