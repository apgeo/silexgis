<?php

use Illuminate\Support\Facades\Route;

// https://laravel.com/docs/9.x/routing#the-default-route-files
// Auth::routes();

Route::get('ping', 'Api\PingController@index');

Route::get('assets/{uuid}/render', 'Api\Assets\RenderFileController@show');

Route::post('register', 'Api\Auth\RegisterController@store');
Route::post('passwords/reset', 'Api\Auth\PasswordsController@store');
Route::put('passwords/reset', 'Api\Auth\PasswordsController@update');

Route::group(['middleware' => ['auth:api']], function () {
    Route::group(['prefix' => 'users'], function () {
        Route::get('/', 'Api\Users\UsersController@index');
        Route::post('/', 'Api\Users\UsersController@store');
        Route::get('/{uuid}', 'Api\Users\UsersController@show');
        Route::put('/{uuid}', 'Api\Users\UsersController@update');
        Route::patch('/{uuid}', 'Api\Users\UsersController@update');
        Route::delete('/{uuid}', 'Api\Users\UsersController@destroy');
    });

    Route::group(['prefix' => 'roles'], function () {
        Route::get('/', 'Api\Users\RolesController@index');
        Route::post('/', 'Api\Users\RolesController@store');
        Route::get('/{uuid}', 'Api\Users\RolesController@show');
        Route::put('/{uuid}', 'Api\Users\RolesController@update');
        Route::patch('/{uuid}', 'Api\Users\RolesController@update');
        Route::delete('/{uuid}', 'Api\Users\RolesController@destroy');
    });

    Route::get('permissions', 'Api\Users\PermissionsController@index');

    Route::group(['prefix' => 'me'], function () {
        Route::get('/', 'Api\Users\ProfileController@index');
        Route::put('/', 'Api\Users\ProfileController@update');
        Route::patch('/', 'Api\Users\ProfileController@update');
        Route::put('/password', 'Api\Users\ProfileController@updatePassword');
    });

    Route::group(['prefix' => 'assets'], function () {
        Route::post('/', 'Api\Assets\UploadFileController@store');
    });
});

// Route::group(['middleware' => ['auth:api']], function () {
//     Route::resource('caves', App\Http\Controllers\API\CaveAPIController::class);
// });
// Route::put('/caves/update/{id}', ['as' => 'update', 'uses' => 'CaveAPIController@update']);
// Route::post('/caves/update/{id}', ['as' => 'update', 'uses' => 'CaveAPIController@update']);
// Route::post('/caves/update/{id}', ['as' => 'update', 'uses' => 'CaveAPIController@update']);
// Route::post('/caves', ['as' => 'update', 'uses' => 'CaveAPIController@update']);

Route::resource('caves', App\Http\Controllers\API\CaveAPIController::class);


Route::resource('features', App\Http\Controllers\API\FeatureAPIController::class);


Route::resource('geofiles', App\Http\Controllers\API\GeofileAPIController::class);


Route::resource('georeferenced_maps', App\Http\Controllers\API\GeoreferencedMapAPIController::class);


Route::resource('images', App\Http\Controllers\API\ImageAPIController::class);


Route::resource('users', App\Http\Controllers\API\UserAPIController::class);


Route::resource('assets', App\Http\Controllers\API\AssetAPIController::class);


Route::resource('cave_entrances', App\Http\Controllers\API\CaveEntranceAPIController::class);


Route::resource('cave_types', App\Http\Controllers\API\CaveTypeAPIController::class);


Route::resource('entrance_types', App\Http\Controllers\API\EntranceTypeAPIController::class);


Route::resource('feature_types', App\Http\Controllers\API\FeatureTypeAPIController::class);


Route::resource('logs', App\Http\Controllers\API\LogAPIController::class);


Route::resource('map_views', App\Http\Controllers\API\MapViewAPIController::class);


Route::resource('points', App\Http\Controllers\API\PointAPIController::class);


Route::resource('tags', App\Http\Controllers\API\TagAPIController::class);


Route::resource('team_members', App\Http\Controllers\API\TeamMemberAPIController::class);


Route::resource('trip_logs', App\Http\Controllers\API\TripLogsAPIController::class);


Route::resource('ping', App\Http\Controllers\API\PingController::class);

Route::resource('files', App\Http\Controllers\API\FileAPIController::class);


Route::resource('sxg_users', App\Http\Controllers\API\SxgUserAPIController::class);
