## SilexGIS server application

Built using PHP, Laravel, Eloquent, PostgreSQL and PostGIS, openapi/swagger

## Installation

To install the project you can use composer

```bash
composer create-project joselfonseca/laravel-api new-api
```

Modify the .env file to configure the server application and database access

```
APP_ENV=local
APP_KEY=
APP_DEBUG=true
APP_URL=http://localhost

LOG_CHANNEL=stack
LOG_LEVEL=debug

DB_CONNECTION=pgsql
DB_HOST=127.0.0.1
DB_PORT=5432
DB_DATABASE=silexgis
DB_USERNAME=postgres
DB_PASSWORD=
```

## API documentation
The project uses API blueprint as API spec and [Aglio](https://github.com/danielgtaylor/aglio) to render the API docs, please install aglio and [merge-apib](https://github.com/ValeriaVG/merge-apib) in your machine and then you can run the following command to compile and render the API docs 
```bash
composer api-docs
```

## License

Based on The Laravel API Starter kit (joselfonseca/laravel-api) which is open-sourced software licensed under the [MIT license](http://opensource.org/licenses/MIT)
