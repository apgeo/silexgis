<?php
//include_once("conf/config.php");
if (@$_SESSION["logged"]) unset($_SESSION["logged"]);
$err = @$_SESSION["login_error"];
if (@$_SESSION["login_error"]) unset($_SESSION["login_error"]);
$login_display =<<<END
<!DOCTYPE html>

<!-- *********************************************************************************************

Login form - George Barbu

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
    <h1>Log In</h1>
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