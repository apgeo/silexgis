<?php
	$root = realpath($_SERVER["DOCUMENT_ROOT"])."/speogis";
	@session_start(); //-- ?
    require_once "$root/auth.php";
	
	require_once("$root/config.php");
	
	include_once "$root/header.php";
?>
<form class="">
        <div class="form-group">
<div class="input-group">
	Email
  <input type="text" class="form-control" placeholder="Email" aria-describedby="email" />
  <!--<span class="input-group-addon" id="email"></span>-->
</div>

<div class="dropdown">
  <button class="btn btn-default dropdown-toggle" type="button" id="languageDropdownMenu" data-toggle="dropdown" aria-haspopup="true" aria-expanded="true">
    Language:
    <span class="caret"></span>
  </button>
  <ul class="dropdown-menu" aria-labelledby="dropdownMenu1">
    <li><a onclick="createCookie('lang','ro',7); " href="#">Romana</a></li>
    <li><a onclick="createCookie('lang','en',7); " href="#">English</a></li>
    <!--<li role="separator" class="divider"></li>-->
  </ul>
</div>


<div class="button">
	<button type="submit" class="btn btn-default">Submit</button>
</div>

</div>
</form>
<script>

///////////////////////
//  begin localization
var selected_language = 'en'; // 'ro';
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
	var x = readCookie('lang');
	
	if (x)
		selected_language = x;
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
	
	localize_static_html();
	document.getElementsByTagName("html")[0].style.visibility = "visible";	
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
</script>
