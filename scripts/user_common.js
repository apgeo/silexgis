  var document_onkeyup = undefined;
    
  var _geofiles = undefined;
  var _team_members = undefined;
  var url_base = 'http://' + window.location.host + "/speogis/";
  //var current_url_path = window.location.host;// + "/speogis";//window.location.pathname;
  
  console.log(url_base);  
$(document).ready(function() {

	localize_static_html();
	document.getElementsByTagName("html")[0].style.visibility = "visible";	
	
	initTripReportForm();
	//return;
	initUploadControls();
	//initCaveDetailsUploadControl();
	initPicturesUploadControl();
	
	//initFeatureSearchControl();
	initTripFeatureSearchControl();

	//initCaveFilesTable();
  });

///////////////////////
//  begin localization
var selected_language = 'en'; // 'ro';

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


//////////////////////////
// Begin Trip Report form

function addTripReport()
{
	openTripReportForm(undefined);
	
	//if (feature_id == undefined)
	//$('#tripReportModal').modal();
}

function openTripReportForm(trip_log_id) // cave_id, cave_entrance_id, feature_id
{			
	//$('#upload_xx_cave_id').val("");
	
	//$('#uploadFilesForm').off('submit');
	//$("#uploadFilesForm")[0].reset();
	
	if (trip_log_id)
		$('#tripReportModalTitleLabel').text("Edit '" + "" +"'");

	if (trip_log_id)
	{
		$.getJSON(url_base + "data/getTripReport.php?trip_log_id=" + trip_log_id, function( data ) {
			
			//$('#tripReportModal').modal();
			
			$('#trip_log_id').val(data.Id);
			$('#tripreport_start_time').val(data.Tripstarttime);
			$('#tripreport_end_time').val(data.Tripendtime);				
			$('#tripreport_details').val(data.Details);
			$('#tripreport_place').val(data.Targetzone);
			//$('#tripreport_members').val(data.Members);
			
			//$('#tripreport_members').tagsinput('items');
			
			data.Members.forEach(function(team_member) {
				$('#tripreport_members').tagsinput('add', _team_members[team_member]);
			});
			
			$('#tripreport_members').tagsinput('refresh');
			//$('#').val(data.Feature_type_id);
									
			$('#tripReportModal').modal();
		});
	}
	else
	{
		$('#trip_log_id').val("");
		$('#tripreport_start_time').val("");
		$('#tripreport_end_time').val("");				
		$('#tripreport_details').val("");
		$('#tripreport_place').val("");
		$('#tripreport_members').val("");
		$('#tripreport_members').tagsinput('removeAll');
		
          var formData = $('#tripReportForm').serializeObject();
		  //var serializedFormData = JSON.stringify(formData);
		  formData.temporary = 1;
		  
		  postDataAsync(url_base + "data/postTripReport.php", formData, 
			function(response) 
			{ 
		$('#trip_log_id').val(response.Id);
		// $('#tripreport_start_time').val("");
		// $('#tripreport_end_time').val("");				
		// $('#tripreport_details').val("");
		// $('#tripreport_members').val("");
		// $('#tripreport_members').tagsinput('removeAll');
		
		$('#tripReportModal').modal();
			
				//console.log('close');
				//$('#tripReportModal').modal('toggle'); 
				
				showNotification("temp Trip report");
				//reloadMapFeatures();
				/* //-- $("caveModal").modal('hide');*/ 
			}, 
			function(err) 
			{ 
				console.log('error');
				alert(err);
			}
		  ); // { cave: formData }
	
		// $.getJSON("data/getTripReport.php?trip_log_id=" + trip_log_id, function( data ) {				

		// // $('#trip_log_id').val("");
		// // $('#tripreport_start_time').val("");
		// // $('#tripreport_end_time').val("");				
		// // $('#tripreport_details').val("");
		// // $('#tripreport_members').val("");
		// // $('#tripreport_members').tagsinput('removeAll');
		
		// // $('#tripReportModal').modal();
		// });
		
	}
	
	if (trip_log_id)
	{
		refreshTripReportFilesTable(trip_log_id);
	
		/*$('#uploadFilesModal').on('hidden.bs.modal', function () {
			//-- this is not executed, maybe ovewritten by the other event
			refreshTripReportFilesTable(trip_log_id);
		});
		*/
	}
	
	$('#trip_report_files_table').DataTable().clear();
	$('#trip_report_files_table').DataTable().draw();	
}

function initTripReportForm()
{
	$('#trip_report_files_table').DataTable( {
        //"ajax": '../ajax/data/arrays.txt'
    } );

	$('#addFilesToTripReport').on('click', function(event) {
		event.preventDefault(); // To prevent following the link (optional)		
		addTripFiles();
		
		//onSaveCave(this);
		//$(this).submit();
	});
	
	$('#tripReportForm').on('submit', function(e) {
          event.preventDefault();

          var formData = $(this).serializeObject();
		  //var serializedFormData = JSON.stringify(formData);
		  
		  postDataAsync(url_base + "data/postTripReport.php", formData, 
			function(x) 
			{ 
				console.log('close');
				$('#tripReportModal').modal('toggle'); 
				
				showNotification("Trip report <b>" + "" + "</b> was saved.");
				//reloadMapFeatures();
				/* //-- $("caveModal").modal('hide');*/ 
			}, 
			function(err) 
			{ 
				console.log('error');
				alert(err);
			}
		  ); // { cave: formData }
		  
		  //console.log(formData);
		  //console.log(JSON.stringify($(this).serializeObject()));          
        });
		
	//fillCaveEntries();	
	
	$('#tripreport_start_time').datetimepicker({
		//defaultDate: "11/1/2013",
        /*disabledDates: [
							moment("12/25/2013"),
							new Date(2013, 11 - 1, 21),
							"11/22/2013 00:53"
						]
						*/
	});	

	$('#tripreport_end_time').datetimepicker({
		//defaultDate: "11/1/2013",
        /*disabledDates: [
							moment("12/25/2013"),
							new Date(2013, 11 - 1, 21),
							"11/22/2013 00:53"
						]*/
	});	

/*var tm2 = new Bloodhound({
  datumTokenizer: Bloodhound.tokenizers.obj.whitespace('name'),
  queryTokenizer: Bloodhound.tokenizers.whitespace,
  prefetch: {
    url: 'data/getTeamMembers.php',
    filter: function(list) {
      return $.map(list, function(cityname) {
        return { name: cityname }; });
    }
  }
});
tm2.initialize();
*/
//console.out(current_url_path);
$.getJSON(url_base + '/getTeamMembers.php', function( data ) 
{	
var items = [];
_team_members = [];

$.each( data, function( key, val ) {
		
		//val.short_name = val.FirstName + " " + val.LastName;
		val.short_name = val.FirstName;
		
		if (val.LastName && val.LastName.length > 0)
			val.short_name += " " + val.LastName.substr(0, 1) + ".";
			
		items.push(val);
		_team_members[val.Id] = val;
	});
	
	
var teamMembers = new Bloodhound({
  datumTokenizer: function (item) 
  {
	//return [item.FirstName + " " + item.LastName];
	return [item.FirstName, item.LastName, item.FirstName + " " + item.LastName, item.short_name];
  }, //Bloodhound.tokenizers.obj.whitespace('FirstName'),
  queryTokenizer: Bloodhound.tokenizers.whitespace,
  //queryTokenizer: Bloodhound.tokenizers.obj.whitespace('FirstName'),
  //prefetch: 'data/getTeamMembers.php',
  
  // prefetch: {
    // url: 'data/getTeamMembers.php',
    // /*filter: function(list) {
      // return $.map(list, function(cityname) {
        // return { name: cityname }; });
    // }*/
	// },
  identify: function(obj) { console.log("identify" + obj); return obj.Id; },
  dupDetector: function(a, b) { return a.Id === b.Id; },
  //prefetch: 'data/getSearchFeatures.php',
  /*remote: {
    url: 'data/getTeamMembers.php',
    //wildcard: '%QUERY'
  },*/
  transform: function(data)
  {
	console.log('p: ' + data);
  },
  local: items
  	/*local: function(query) {
		var items = [];
      $.getJSON('data/getTeamMembers.php', function( data ) {
	  
	$.each( data, function( key, val ) {
		items.push(val);
	});
	
	
	});
	return items;
    }
	*/
});

teamMembers.initialize();

/**
 * Typeahead
 */
/*var elt = $('.example_typeahead > > input');
elt.tagsinput({
  typeaheadjs: {
    name: 'citynames',
    displayKey: 'name',
    valueKey: 'name',
    source: tm2.ttAdapter()
  }
});
*/

/**
 * Objects as tags
 */
elt = $('#tripreport_members');
elt.tagsinput({
  itemValue: 'Id',
  //itemText: 'FirstName',
  itemText: 'short_name',
  typeaheadjs: {
/*
  hint: $('.Typeahead-hint'),
  menu: $('.Typeahead-menu'),
  //hint: true,
  highlight: true,
  minLength: 1,
  classNames: {
      open: 'is-open',
      empty: 'is-empty',
      cursor: 'is-active',
      suggestion: 'Typeahead-suggestion',
      selectable: 'Typeahead-selectable'
    },
*/  
    name: 'teamMembers',
    //displayKey: 'FirstName',
	displayKey: 'short_name',
	confirmKeys: [13, 44, 188],
    source: teamMembers.ttAdapter(),
	//limit: 9
    /*source: function(query) {
      return $.getJSON('data/getTeamMembers.php');
    }*/	
  }
});

//elt.tagsinput('add', { "Id": 21 , "FirstName": "Amsterdam" });
//elt.tagsinput('add', { "Id": 32 , "FirstName": "xx" });

// elt.tagsinput('add', { "value": 1 , "text": "Amsterdam"   , "continent": "Europe"    });
// elt.tagsinput('add', { "value": 2 , "text": "xx"   , "continent": "zz"    });

	
	// $('#membersTagsControl').tagsinput({
	/*
	$('#tripreport_members').tagsinput({
	  itemValue: 'Id',
	  itemText: 'FirstName',
	  typeahead: {
		source: function(query) {
		  return $.getJSON('data/getTeamMembers.php');
		}
	  }
	});	
	
var cities = new Bloodhound({
  datumTokenizer: Bloodhound.tokenizers.obj.whitespace('text'),
  queryTokenizer: Bloodhound.tokenizers.whitespace,
  prefetch: 'assets/cities.json'
});
cities.initialize();

var elt = $('input');
elt.tagsinput({
  itemValue: 'value',
  itemText: 'text',
  typeaheadjs: {
    name: 'cities',
    displayKey: 'text',
    source: cities.ttAdapter()
  }
});
elt.tagsinput('add', { "value": 1 , "text": "Amsterdam"   , "continent": "Europe"    });
elt.tagsinput('add', { "value": 4 , "text": "Washington"  , "continent": "America"   });
elt.tagsinput('add', { "value": 7 , "text": "Sydney"      , "continent": "Australia" });
elt.tagsinput('add', { "value": 10, "text": "Beijing"     , "continent": "Asia"      });
elt.tagsinput('add', { "value": 13, "text": "Cairo"       , "continent": "Africa"    });	
*/

// HACK: overrule hardcoded display inline-block of typeahead.js
	$(".twitter-typeahead").css('display', 'inline');
	
    });
	
	$('.modal-child').on('show.bs.modal', function () {
        var modalParent = $(this).attr('data-modal-parent');
        //$(modalParent).css('opacity', 0);
		$(modalParent).hide();
    });
	
    $('.modal-child').on('hidden.bs.modal', function () {
        var modalParent = $(this).attr('data-modal-parent');
        //$(modalParent).css('opacity', 1);
		$(modalParent).show();
		
		var trip_log_id = $('#trip_log_id').val();
		refreshTripReportFilesTable(trip_log_id);
    });	
}

function openAddFilesToTripReportForm()
{
	openUploadFilesForm(undefined, undefined);
}

function refreshTripReportFilesTable(trip_log_id)
{
	var form_data = JSON.stringify( { trip_log_id: trip_log_id } );
	
    $.ajax({
                url: url_base + 'data/getTripReportFiles.php?trip_log_id=' + trip_log_id, // point to server-side PHP script 
                dataType: 'json', // dataType: 'jsonp', //dataType: 'text',  
                cache: false,
                contentType: false,
                processData: false,
                data: form_data,
                type: 'post',
                success: function(data){
							var dataSet = [];

							var file_directory = 'data/uploader/files/';
							
							data.forEach(function(item) {
								var file_url = file_directory + item.file_name;
								var file_name_html = "<a href='" + file_url + "' target='_blank' >" + item.file_name + "</a>";
								dataSet.push([file_name_html, item.size, item.add_time, 0/*item.object_type*/]);
							});
							  
							/*$('#cave_files_table').DataTable({
								//"ajax": '../ajax/data/arrays.txt'
								data: dataSet,
							});*/
							$('#trip_report_files_table').DataTable().clear();
							$('#trip_report_files_table').DataTable()
								.rows.add(dataSet)
								.draw();
                },
				error:  function(jqXHR, textStatus, errorThrown )
				{
					//onFailure(textStatus); //-- show error code returned
					console.error(errorThrown);
					console.error("Error loading trip report files: " + textStatus + " " + errorThrown);
					//alert(errMsg);
				}				
     });	
}

	function initTripFeatureSearchControl()
	{
  var engine, remoteHost, template, empty;
  
  remoteHost = '';
  template = Handlebars.compile($("#result-template").html());
  empty = Handlebars.compile($("#empty-template").html());
  
var featuresDataSource = new Bloodhound({
  datumTokenizer: Bloodhound.tokenizers.obj.whitespace('name'),  
  queryTokenizer: Bloodhound.tokenizers.whitespace,
  //datumTokenizer: Bloodhound.tokenizers.obj.whitespace('name', 'name'),  
  
  identify: function(obj) { console.log("identify" + obj); return obj.id; },
  dupDetector: function(a, b) { return a.id === b.id; },
  prefetch: url_base + 'data/getSearchFeatures.php',
  remote: {
    //url: remoteHost + 'data/getSearchFeatures.php?q=%QUERY',
	url: url_base + 'data/getSearchFeatures.php?q=%QUERY',
    wildcard: '%QUERY'
  },
  transform: function(data)
  {
	console.log('p: ' + data);
  }
});

featuresDataSource.initialize();

$('#tripreport_place').typeahead({
 //$('#searchFeatureControl .typeahead').typeahead({
  hint: $('.Typeahead-hint_2'),
  menu: $('.Typeahead-menu_2'),
  //hint: true,
  highlight: true,
  minLength: 1,
  classNames: {
      open: 'is-open',
      empty: 'is-empty',
      cursor: 'is-active',
      suggestion: 'Typeahead-suggestion',
      selectable: 'Typeahead-selectable'
    },
  //display: 'name',  
  //limit: 9
  //suggestion: Handlebars.compile('<div><strong>{{value}}</strong> � {{year}}</div>')
  /*
  filter: function (parsedResponse) {
            // parsedResponse is the array returned from your backend
            console.log(parsedResponse);

            // do whatever processing you need here
            return parsedResponse;
        }
		*/
},
{
  name: 'features',
  source: featuresDataSource, //substringMatcher(states)
  displayKey: 'name',
    templates: {
      suggestion: template,
      empty: empty,
	  //header: '<h3 class="league-name">Teams</h3>'
    },
})
/*.on('typeahead:asyncrequest', function() {
    $('.Typeahead-spinner').show();
  })
  .on('typeahead:asynccancel typeahead:asyncreceive', function() {
    $('.Typeahead-spinner').hide();
  })*/;	

$('#tripreport_place').bind('typeahead:select', function(ev, suggestion) {
  console.log('Selection: ');
  console.log(suggestion);
  //gotoDbElement(suggestion); // id
  // gotoMapElement(suggestion.point_db_id); // id
});
	}

// End Trip Report form
///////////////////////

function initPicturesUploadControl()
{
	/*$('#fileupload').fileupload({
		//formData: {example: 'test'}
		url: 'speogis/data/uploader'
	}).on('fileuploadsubmit', function (e, data) {
		data.formData = data.context.find(':input').serializeArray();
	});
	
	$('#fileupload').bind('fileuploadsubmit', function (e, data) {
		// The example input, doesn't have to be part of the upload form:
		var input = $('#input');
		data.formData = {example: input.val()};
		if (!data.formData.example) {
		  data.context.find('button').prop('disabled', false);
		  input.focus();
		  return false;
		}
	});
	*/
    //'use strict';

    // Initialize the jQuery File Upload widget:
  
  /*$('#pictureUploader').fileupload({
        // Uncomment the following to send cross-domain cookies:
        //xhrFields: {withCredentials: true},
        url: 'data/uploader/picture_uploader.php'
		//url: 'data/uploader'
    });
	*/
	
	/*
    // Enable iframe cross-domain access via redirect option:
    $('#fileupload').fileupload(
        'option',
        'redirect',
        window.location.href.replace(
            /\/[^\/]*$/,
            '/cors/result.html?%s'
        )
    );
	*/
	
	/*
    if (window.location.hostname === 'blueimp.github.io') {
        // Demo settings:
        $('#fileupload').fileupload('option', {
            url: 'speogis/data/uploader',
            // Enable image resizing, except for Android and Opera,
            // which actually support image resizing, but fail to
            // send Blob objects via XHR requests:
            disableImageResize: /Android(?!.*Chrome)|Opera/
                .test(window.navigator.userAgent),
            maxFileSize: 999000,
            acceptFileTypes: /(\.|\/)(gif|jpe?g|png)$/i
        });
        // Upload server status check for browsers with CORS support:
        if ($.support.cors) {
            $.ajax({
                url: '//jquery-file-upload.appspot.com/',
                type: 'HEAD'
            }).fail(function () {
                $('<div class="alert alert-danger"/>')
                    .text('Upload server currently unavailable - ' +
                            new Date())
                    .appendTo('#fileupload');
            });
        }
    } else 
	*/
	{
        // Load existing files:
		console.log('#pictureUploader');
		console.log($('#pictureUploader').fileupload('option', 'url'));
        //..$('#pictureUploader').addClass('fileupload-processing');
        $.ajax({
            // Uncomment the following to send cross-domain cookies:
            //xhrFields: {withCredentials: true},
            url: $('#pictureUploader').fileupload('option', 'url'),
            dataType: 'json',
            context: $('#pictureUploader')[0]
        }).always(function () {
            $(this).removeClass('fileupload-processing');
        }).done(function (result) {
            $(this).fileupload('option', 'done')
                .call(this, $.Event('done'), {result: result});
        });
    }	
	
	// $('#fileupload').fileupload('destroy');
}

function initUploadControls()
{
	 $('.fileupload').fileupload({
        // Uncomment the following to send cross-domain cookies:
        //xhrFields: {withCredentials: true},
        //url: 'data/uploader/'
		//url: 'data/uploader'
    });
}


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

