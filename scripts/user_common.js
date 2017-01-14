  var document_onkeyup = undefined;
    
  var _geofiles = undefined;
  var _team_members = undefined;
  var url_base = 'http://' + window.location.host + "/speogis/";
  //var current_url_path = window.location.host;// + "/speogis";//window.location.pathname;
  
  console.log(url_base);  
  
$(document).ready(function() {
	localize_static_html();
	document.getElementsByTagName("html")[0].style.visibility = "visible";	
});

///////////////////////
//  begin localization
//var selected_language = 'en'; // 'ro';
var selected_language = 'ro';

function _t()
{
	return localizedText[selected_language];
}

function localize_static_html()
{
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



///////////////////////////

function postDataAsync(_url, data, onSuccess, onFailure)
{
	$.ajax({
		type: "POST",
		url: _url, //"/webservices/PodcastService.asmx/CreateMarkers",
		// The key needs to match your method's input parameter (case-sensitive).
		data: JSON.stringify(data), // JSON.stringify({ Markers: markers })
		contentType: "application/json; charset=utf-8",
		dataType: "json",
		success: function(data) { 
			onSuccess(data); 
			//alert(data); 
		},
		//failure: function(errMsg) {
			//$onFailure(errMsg);
			//},
		error:  function(jqXHR, textStatus, errorThrown )
		{
			onFailure(textStatus); //-- show error code returned
			//alert(errMsg);
		}
});
}

function showNotification(message, placement = undefined)
{
	// http://bootstrap-notify.remabledesigns.com/#documentation
	
	var default_placement = { from: "top", align: "right" };
	
	if (placement == undefined)
		placement = default_placement;
	
	var notify = $.notify({
		// options
		message: message
	},{
		// settings
		type: 'info',
		animate: {
			enter: 'animated fadeInDown',
			exit: 'animated fadeOutUp'
		},
		delay: 5000,
		timer: 1000,
		url_target: '_blank',
		placement: placement,
	});
}
	
    $.fn.serializeObject = function() {
        var o = {};
        var a = this.serializeArray();
        $.each(a, function() {
            if (o[this.name]) {
                if (!o[this.name].push) {
                    o[this.name] = [o[this.name]];
                }
                o[this.name].push(this.value || '');
            } else {
                o[this.name] = this.value || '';
            }
        });
        return o;
    };

	function rtrim(str, maxLength) {
		return str.substr(0, maxLength);
	}

var getUrlParameter = function getUrlParameter(sParam) {
    var sPageURL = decodeURIComponent(window.location.search.substring(1)),
        sURLVariables = sPageURL.split('&'),
        sParameterName,
        i;

    for (i = 0; i < sURLVariables.length; i++) {
        sParameterName = sURLVariables[i].split('=');

        if (sParameterName[0] === sParam) {
            return sParameterName[1] === undefined ? true : sParameterName[1];
        }
    }
};

var typeOf = function(obj){
    return ({}).toString.call(obj)
        .match(/\s([a-zA-Z]+)/)[1].toLowerCase();
};
function cloneObject(obj){
    var type = typeOf(obj);
    if (type == 'object' || type == 'array') {
        if (obj.clone) {
            return obj.clone();
        }
        var clone = type == 'array' ? [] : {};
        for (var key in obj) {
            clone[key] = cloneObject(obj[key]);
        }
        return clone;
    }
    return obj;
}

//-- implementation of Object.prototype functions generates errors like  "error SyntaxError: Failed to execute setRequestHeader' on 'XMLHttpRequest' " ?

/*Object.prototype.getName = function() { 
   var funcNameRegex = /function (.{1,})\(/;
   var results = (funcNameRegex).exec((this).constructor.toString());
   return (results && results.length > 1) ? results[1] : "";
};*/

/*
Object.prototype.inCollection = function() {
    for(var i=0; i<arguments.length; i++)
       if(arguments[i] == this) return true;
    return false;
}
*/

