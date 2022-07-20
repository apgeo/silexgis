<?php namespace Tests\APIs;

use Illuminate\Foundation\Testing\WithoutMiddleware;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;
use App\Models\Log;

class LogApiTest extends TestCase
{
    use ApiTestTrait, WithoutMiddleware, DatabaseTransactions;

    /**
     * @test
     */
    public function test_create_log()
    {
        $log = Log::factory()->make()->toArray();

        $this->response = $this->json(
            'POST',
            '/api/logs', $log
        );

        $this->assertApiResponse($log);
    }

    /**
     * @test
     */
    public function test_read_log()
    {
        $log = Log::factory()->create();

        $this->response = $this->json(
            'GET',
            '/api/logs/'.$log->id
        );

        $this->assertApiResponse($log->toArray());
    }

    /**
     * @test
     */
    public function test_update_log()
    {
        $log = Log::factory()->create();
        $editedLog = Log::factory()->make()->toArray();

        $this->response = $this->json(
            'PUT',
            '/api/logs/'.$log->id,
            $editedLog
        );

        $this->assertApiResponse($editedLog);
    }

    /**
     * @test
     */
    public function test_delete_log()
    {
        $log = Log::factory()->create();

        $this->response = $this->json(
            'DELETE',
             '/api/logs/'.$log->id
         );

        $this->assertApiSuccess();
        $this->response = $this->json(
            'GET',
            '/api/logs/'.$log->id
        );

        $this->response->assertStatus(404);
    }
}
