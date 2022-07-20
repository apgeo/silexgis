<?php namespace Tests\APIs;

use Illuminate\Foundation\Testing\WithoutMiddleware;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;
use App\Models\Feature;

class FeatureApiTest extends TestCase
{
    use ApiTestTrait, WithoutMiddleware, DatabaseTransactions;

    /**
     * @test
     */
    public function test_create_feature()
    {
        $feature = Feature::factory()->make()->toArray();

        $this->response = $this->json(
            'POST',
            '/api/features', $feature
        );

        $this->assertApiResponse($feature);
    }

    /**
     * @test
     */
    public function test_read_feature()
    {
        $feature = Feature::factory()->create();

        $this->response = $this->json(
            'GET',
            '/api/features/'.$feature->id
        );

        $this->assertApiResponse($feature->toArray());
    }

    /**
     * @test
     */
    public function test_update_feature()
    {
        $feature = Feature::factory()->create();
        $editedFeature = Feature::factory()->make()->toArray();

        $this->response = $this->json(
            'PUT',
            '/api/features/'.$feature->id,
            $editedFeature
        );

        $this->assertApiResponse($editedFeature);
    }

    /**
     * @test
     */
    public function test_delete_feature()
    {
        $feature = Feature::factory()->create();

        $this->response = $this->json(
            'DELETE',
             '/api/features/'.$feature->id
         );

        $this->assertApiSuccess();
        $this->response = $this->json(
            'GET',
            '/api/features/'.$feature->id
        );

        $this->response->assertStatus(404);
    }
}
