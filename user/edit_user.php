<?php
	require_once dirname(__DIR__).'/config.php';
	$root = realpath($_SERVER["DOCUMENT_ROOT"]).$application_url_root;
	
	@session_start(); //-- ?
    require_once("$root/config.php");
	
	require_once "$root/auth.php";
	
	include_once "$root/header.php";
	
	//include_once('../geoPHP/geoPHP.inc');
	//require_once '../data/db_utilities.php';
	include_once '../data/db_common.php';

	$_user_id = $_SESSION["id_user"];
	
	$user = UsersQuery::create()->findPK($_user_id);
	
	$picture_storage_type;
	$lang;

	if (!empty($_POST["language"]))
	{
		$lang = $_POST["language"];
		$user->setLanguage($lang);
	}

	if (!empty($_POST["picture_storage_type"]))
	{
		$picture_storage_type = $_POST["picture_storage_type"];
		$user->setPictureStorageType($picture_storage_type);
	}

	@$p1 = $_POST["password"];
	@$p2 = $_POST["password2"];
	
			// if (!empty($_POST["password"]) && ($_POST["password"] != "********") && 
		// !empty($_POST["password"]) && ($_POST["password"] != "********"))

	if (!empty($p1) || !empty($p2))
	{
		if (empty($p1) || empty($p2))
			echo "<h4><b>ambele campuri parola trebuie completate identic</b></h4><br/>";
		else
		{
			if ($p1 === $p2)
			{
				$user->setPassword($p1);
				echo "<h2><b>parola a fost schimbata</b></h2><br/>";
			}
			else
				echo "<h4><b>cele doua parole nu sunt identice</b></h4><br/>";
		}
	}
	
	if (!empty($_POST["email"]))
	{		
		$user->setEmail($_POST["email"]);
		//echo "<h2><b>parola a fost schimbata</b></h2>";				
	}

	$user->save();
	
	$user = UsersQuery::create()->findPK($_user_id);	
	
	$user_language = $user->getLanguage();
	$user_email = $user->getEmail();

	$user_picture_storage_type = $user->getPictureStorageType();
?>

<script>

///////////////////////
//  begin localization
//var selected_language = 'en'; // 'ro';
var selected_language = 'ro';

function _t()
{
	try
	{
		return localizedText[selected_language];
	}
	catch (ex)
	{
		return "_not_found_";
	}
}

function localize_static_html()
{
	var lang_cookie = readCookie('lang');
	
	if (lang_cookie)
		selected_language = lang_cookie;
	//var regex = "/(<([^>]+)>)/ig";
	//var regex = "(?<=\{)(.*?)(?=\})";
	//var regex = /{(.*)}/
	
	var regex = /\*{\s*[\w\.]+\s*}\*/g
	// var regex = /{{\s*[\w\.]+\s*}}/g
	
	//var regex = /^^\s*[\w\.]+\s*^^/g
	//body = "<p>test</p>"

	// See more at: http://www.jsmantras.com/blog/String-Methods-search-match-and-replace#sthash.MPZVQvuX.dpuf
	function replacer(match, p1, p2, p3, offset, string)
	{ // p1 is nondigits, p2 digits, and p3 non-alphanumerics 
	
		res = match.match(/[\w\.]+/)[0];
		console.log(res);
		
		var localized_text = eval("_t()." + res);
			
		if (localized_text == undefined)
			return "*not found*";
		//m_res[0].replace(regex, localized_text);
		//document.body.innerHTML.replace(m_res[0], "");
	
		return localized_text;
		//return [p1, p2, p3].join(' - '); 
	}; 
	
	//var m_res = document.body.innerHTML.match(regex);
	//var m_res = document.body.innerHTML.replace(regex, replacer);
	document.body.innerHTML = document.body.innerHTML.replace(regex, replacer);
	
/*	if (m_res)
	m_res.map(function(x) { 
		// return x.match(/[\w\.]+/)[0]; 
		res = x.match(/[\w\.]+/)[0];
		console.log(res);
		var localized_text = eval("_t()." + res);
		m_res[0].replace(regex, localized_text);
		document.body.innerHTML.replace(m_res[0], "");
		// return res; 
		});
	*/
	
	/*if (m_res != null)
	{
		var res = m_res[0];
		console.log(res);
		//res2 = res.replace('{', '').replace('}', '');
		console.log(eval("_t()." + res));
		m_res[0].replace(regex, '$1');
		
	}
	
	document.body.innerHTML = document.body.innerHTML.replace(regex, "");
	*/
}

//  end localization
///////////////////////

$(document).ready(function() {	
	
	createCookie('lang', <?="'$user_language'" ?>,7);
	
	localize_static_html();
	document.getElementsByTagName("html")[0].style.visibility = "visible";

	setPictureStorageType(<?="'$user_picture_storage_type'" ?>);
});

function createCookie(name,value,days) {
    if (days) {
        var date = new Date();
        date.setTime(date.getTime() + (days*24*60*60*1000));
        var expires = "; expires=" + date.toUTCString();
    }
    else var expires = "";
    document.cookie = name + "=" + value + expires + "; path=/";
}

function readCookie(name) {
    var nameEQ = name + "=";
    var ca = document.cookie.split(';');
    for(var i=0;i < ca.length;i++) {
        var c = ca[i];
        while (c.charAt(0)==' ') c = c.substring(1,c.length);
        if (c.indexOf(nameEQ) == 0) return c.substring(nameEQ.length,c.length);
    }
    return null;
}

function eraseCookie(name) {
    createCookie(name,"",-1);
}

function setLanguage(lang)
{
	document.getElementById("language").value = lang;
	
	if (lang == 'ro')
		selectedLanguageDescription = 'Română';
	else
	if (lang == 'en')	
		selectedLanguageDescription = 'English';
	else
		selectedLanguageDescription = 'Unknown';
	
	document.getElementById("selectedLanguageDescription").innerText = selectedLanguageDescription;
}

function setPictureStorageType(pst)
{
	document.getElementById("picture_storage_type").value = pst;
	
	if (pst == 'local')
		pictureStorageType = 'local';
	else
	if (pst == 'AmazonS3')	
		pictureStorageType = 'Amazon S3';
	else
		pictureStorageType = 'Unknown';
	
	document.getElementById("pictureStorageType").innerText = pictureStorageType;
}

</script>

<form class="" method="post">
<br/><br/>
        <div class="form-group">
<div class="input-group form-group row">
	<label for="email" class="col-sm-2 control-label">Email:</label>
	<div class="col-sm-10">
		<input type="text" name="email" value="<?=$user_email ?>" class="form-control" placeholder="Email" aria-describedby="email" />
	</div>
  <!--<span class="input-group-addon" id="email"></span>-->
</div>

<div class="input-group form-group row">
	<label for="password" class="col-sm-2 control-label">Parola noua:</label>
	<div class="col-sm-10">
		<input type="text" name="password" class="form-control" placeholder="Parola" aria-describedby="password" />
		<!-- value="" -->
	</div>
  <!--<span class="input-group-addon" id="email"></span>-->
</div>

<div class="input-group form-group row">
	<label for="password2" class="col-sm-2 control-label">Confirmare parola:</label>
	<div class="col-sm-10">
		<input type="text" name="password2" class="form-control" placeholder="Parola" aria-describedby="password2" />
	</div>
  <!--<span class="input-group-addon" id="email"></span>-->
</div>

<div class="dropdown">
  <button class="btn btn-default dropdown-toggle" type="button" id="languageDropdownMenu" data-toggle="dropdown" aria-haspopup="true" aria-expanded="true">
    Language:
    <span class="caret"></span>
  </button>
  <ul class="dropdown-menu" aria-labelledby="dropdownMenu1">
    <li><a onclick="setLanguage('ro');" href="#">Română</a></li>
    <li><a onclick="setLanguage('en');" href="#">English</a></li>
    <!--<li role="separator" class="divider"></li>-->
  </ul>
  <span id="selectedLanguageDescription" ></span>
</div>


<div class="dropdown">
  <button class="btn btn-default dropdown-toggle" type="button" id="pictureStorageTypeDropdownMenu" data-toggle="dropdown" aria-haspopup="true" aria-expanded="true">
    Default picture storage:
    <span class="caret"></span>
  </button>
  <ul class="dropdown-menu" aria-labelledby="picture_storage_type">
    <li><a onclick="setPictureStorageType('local');" href="#">Server local storage</a></li>
    <li><a onclick="setPictureStorageType('AmazonS3');" href="#">Amazon S3</a></li>
    <!--<li role="separator" class="divider"></li>-->
  </ul>
  <span id="pictureStorageType" ></span>
</div>

<input type="hidden" name="language" id="language" />
<input type="hidden" name="picture_storage_type" id="picture_storage_type" />


<div class="button">
	<button type="submit" class="btn btn-default">Submit</button>
</div>

</div>
</form>

</body>
</html>