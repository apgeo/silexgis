<?php namespace Tests\APIs;

use Illuminate\Foundation\Testing\WithoutMiddleware;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;
use App\Models\Point;

class PointApiTest extends TestCase
{
    use ApiTestTrait, WithoutMiddleware, DatabaseTransactions;

    /**
     * @test
     */
    public function test_create_point()
    {
        $point = Point::factory()->make()->toArray();

        $this->response = $this->json(
            'POST',
            '/api/points', $point
        );

        $this->assertApiResponse($point);
    }

    /**
     * @test
     */
    public function test_read_point()
    {
        $point = Point::factory()->create();

        $this->response = $this->json(
            'GET',
            '/api/points/'.$point->id
        );

        $this->assertApiResponse($point->toArray());
    }

    /**
     * @test
     */
    public function test_update_point()
    {
        $point = Point::factory()->create();
        $editedPoint = Point::factory()->make()->toArray();

        $this->response = $this->json(
            'PUT',
            '/api/points/'.$point->id,
            $editedPoint
        );

        $this->assertApiResponse($editedPoint);
    }

    /**
     * @test
     */
    public function test_delete_point()
    {
        $point = Point::factory()->create();

        $this->response = $this->json(
            'DELETE',
             '/api/points/'.$point->id
         );

        $this->assertApiSuccess();
        $this->response = $this->json(
            'GET',
            '/api/points/'.$point->id
        );

        $this->response->assertStatus(404);
    }
}
