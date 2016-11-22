<?php
session_start();
if(isset($_SESSION) && isset($_SESSION["logged"])) unset($_SESSION["logged"]);
if(isset($_SESSION) && isset($_SESSION["username"])) unset($_SESSION["username"]);
session_destroy();
header("Location: index.php");
?>