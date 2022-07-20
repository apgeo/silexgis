<?php

namespace App\Providers;

use Illuminate\Support\ServiceProvider;

class AppServiceProvider extends ServiceProvider
{
    /**
     * Register any application services.
     *
     * @return void
     */
    public function register()
    {
    }

    /**
     * Bootstrap any application services.
     *
     * @return void
     */
    public function boot()
    {
        //

        //xx added as per https://github.com/mstaack/laravel-postgis/issues/144#issuecomment-756154662
        // type mappings

        //for points table
        \DB::getDoctrineSchemaManager()->getDatabasePlatform()->registerDoctrineTypeMapping('point', 'string'); //?
        \DB::getDoctrineSchemaManager()->getDatabasePlatform()->registerDoctrineTypeMapping('geoobject_type', 'string');
        \DB::getDoctrineSchemaManager()->getDatabasePlatform()->registerDoctrineTypeMapping('picture_storage_type', 'string');
        \DB::getDoctrineSchemaManager()->getDatabasePlatform()->registerDoctrineTypeMapping('feature_type', 'string');
        \DB::getDoctrineSchemaManager()->getDatabasePlatform()->registerDoctrineTypeMapping('box', 'string');
        \DB::getDoctrineSchemaManager()->getDatabasePlatform()->registerDoctrineTypeMapping('tag_type', 'string');
        // \DB::getDoctrineSchemaManager()->getDatabasePlatform()->registerDoctrineTypeMapping('', 'string');

        \DB::getDoctrineSchemaManager()->getDatabasePlatform()->registerDoctrineTypeMapping('rock_type', 'string');
        \DB::getDoctrineSchemaManager()->getDatabasePlatform()->registerDoctrineTypeMapping('exploration_type', 'string');
        \DB::getDoctrineSchemaManager()->getDatabasePlatform()->registerDoctrineTypeMapping('geofile_type', 'string');
    }
}
