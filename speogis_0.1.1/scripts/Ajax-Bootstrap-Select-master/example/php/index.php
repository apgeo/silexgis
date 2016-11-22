<?php
$file_contents = file_get_contents('../dataset.json');

if (!$file_contents) {
    throw new Exception('Invalid file name');
}

$data = json_decode($file_contents, true);
?>

<!doctype html>
<html lang="en-US">
<head>
    <meta charset="UTF-8">
    <title></title>
	<!-- Bootstrap Only -->
	<!-- Latest compiled and minified CSS -->	
	<link rel="stylesheet" href="https://maxcdn.bootstrapcdn.com/bootstrap/3.3.6/css/bootstrap.min.css" integrity="sha384-1q8mTJOASx8j1Au+a5WDVnPi2lkFfwwEAa8hDDdjZlpLegxhjVME1fgjWPGmkzs7" crossorigin="anonymous">

	<!-- Optional theme -->
	<link rel="stylesheet" href="https://maxcdn.bootstrapcdn.com/bootstrap/3.3.6/css/bootstrap-theme.min.css" integrity="sha384-fLW2N01lMqjakBkx3l/M9EahuwpSfeNvV63J5ezn3uZzapT0u7EYsXMjQV+0En5r" crossorigin="anonymous">

	<!-- Latest compiled and minified JavaScript -->
	<!--<script src="https://maxcdn.bootstrapcdn.com/bootstrap/3.3.6/js/bootstrap.min.js" integrity="sha384-0mSbJDEHialfmuBBQP6A4Qrprq5OVfW37PRR3j5ELqxss1yVqOtnepnHVP9aJ7xS" crossorigin="anonymous"></script>-->
	
<!--    <link rel="stylesheet" href="//cdnjs.cloudflare.com/ajax/libs/twitter-bootstrap/3.2.0/css/bootstrap.min.css"/>-->
    <link rel="stylesheet" href="//cdnjs.cloudflare.com/ajax/libs/bootstrap-select/1.6.3/css/bootstrap-select.min.css"/>
	
    <link rel="stylesheet" href="../../dist/css/ajax-bootstrap-select.css"/>
	
<!--<script type="text/javascript" src="//cdnjs.cloudflare.com/ajax/libs/jquery/1.9.1/jquery.min.js"></script>-->

	<script type="text/javascript" src="http://ajax.googleapis.com/ajax/libs/jquery/1.11.3/jquery.min.js"></script>
    
	<script type="text/javascript" src="http://ajax.googleapis.com/ajax/libs/jqueryui/1.11.4/jquery-ui.min.js"></script>	
	<link rel="stylesheet" href="http://ajax.googleapis.com/ajax/libs/jqueryui/1.11.4/themes/ui-lightness/jquery-ui.css" type="text/css">

	<!-- Latest compiled and minified JavaScript -->
	<script src="https://maxcdn.bootstrapcdn.com/bootstrap/3.3.6/js/bootstrap.min.js" integrity="sha384-0mSbJDEHialfmuBBQP6A4Qrprq5OVfW37PRR3j5ELqxss1yVqOtnepnHVP9aJ7xS" crossorigin="anonymous"></script>

<script type="text/javascript" src="//cdnjs.cloudflare.com/ajax/libs/bootstrap-select/1.6.3/js/bootstrap-select.min.js"></script>
<script type="text/javascript" src="../../dist/js/ajax-bootstrap-select.js"></script>
	
    <style>h3 {
            text-align: center;
        }

        .bootstrap-select {
            width: 100% !important;
        }</style>
</head>
<body>
<div class="container">
    <div class="row">
        <div class="col-xs-4">
            <h3>Without<br>Ajax-Bootstrap-Select</h3>
            <select id="selectpicker" class="selectpicker" data-live-search="true">
                <?php
                foreach ($data as $d) {
                    echo '<option value="' . $d['Email'] . '">' . $d['Name'] . '</option>';
                }

                ?>
            </select>
        </div>

        <div class="col-xs-4">
            <h3>With<br>Ajax-Bootstrap-Select</h3>
            <select id="ajax-select" class="selectpicker with-ajax" data-live-search="true"></select>
        </div>

        <div class="col-xs-4">
            <h3>Multiple<br>Ajax-Bootstrap-Select</h3>
            <select id="ajax-select-multiple" class="selectpicker with-ajax" multiple data-live-search="true"></select>
        </div>
    </div>
    <div class="row">
        <div class="col-xs-4">
            <h3>Cached Options<br>Ajax-Bootstrap-Select</h3>
            <select class="selectpicker with-ajax" data-live-search="true" multiple>
                <option value="neque.venenatis.lacus@neque.com" data-subtext="neque.venenatis.lacus@neque.com" selected>
                    Chancellor
                </option>
            </select>
        </div>
    </div>
</div>


<script>
    var options = {
        ajax          : {
            url     : 'http://localhost/speogis/data/getSearchFeatures.php',
            type    : 'POST',
            dataType: 'json',
            // Use "{{{q}}}" as a placeholder and Ajax Bootstrap Select will
            // automatically replace it with the value of the search query.
            data    : {
                q: '{{{q}}}'
            }
        },
        locale        : {
            emptyTitle: 'Select and Begin Typing'
        },
        log           : 3,
        preprocessData: function (data) {
            var index, len = data.length, array = [];
            if (len) {
                for (index = 0; index < len; index++) {
                    array.push($.extend(true, data[index], {
                        text : data[index].name,
                        value: data[index].id,
                        data : {
                            subtext: data[index].res_type
                        }
                    }));
                }
            }
            // You must always return a valid array when processing data. The
            // data argument passed is a clone and cannot be modified directly.
            return array;
        }
    };

    $('.selectpicker').selectpicker().filter('.with-ajax').ajaxSelectPicker(options);
    $('select').trigger('change');
</script>
</body>
</html>
