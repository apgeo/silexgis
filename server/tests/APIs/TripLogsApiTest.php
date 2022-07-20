<?php namespace Tests\APIs;

use Illuminate\Foundation\Testing\WithoutMiddleware;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;
use App\Models\TripLogs;

class TripLogsApiTest extends TestCase
{
    use ApiTestTrait, WithoutMiddleware, DatabaseTransactions;

    /**
     * @test
     */
    public function test_create_trip_logs()
    {
        $tripLogs = TripLogs::factory()->make()->toArray();

        $this->response = $this->json(
            'POST',
            '/api/trip_logs', $tripLogs
        );

        $this->assertApiResponse($tripLogs);
    }

    /**
     * @test
     */
    public function test_read_trip_logs()
    {
        $tripLogs = TripLogs::factory()->create();

        $this->response = $this->json(
            'GET',
            '/api/trip_logs/'.$tripLogs->id
        );

        $this->assertApiResponse($tripLogs->toArray());
    }

    /**
     * @test
     */
    public function test_update_trip_logs()
    {
        $tripLogs = TripLogs::factory()->create();
        $editedTripLogs = TripLogs::factory()->make()->toArray();

        $this->response = $this->json(
            'PUT',
            '/api/trip_logs/'.$tripLogs->id,
            $editedTripLogs
        );

        $this->assertApiResponse($editedTripLogs);
    }

    /**
     * @test
     */
    public function test_delete_trip_logs()
    {
        $tripLogs = TripLogs::factory()->create();

        $this->response = $this->json(
            'DELETE',
             '/api/trip_logs/'.$tripLogs->id
         );

        $this->assertApiSuccess();
        $this->response = $this->json(
            'GET',
            '/api/trip_logs/'.$tripLogs->id
        );

        $this->response->assertStatus(404);
    }
}
