<?php namespace Tests\APIs;

use Illuminate\Foundation\Testing\WithoutMiddleware;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;
use App\Models\FeatureType;

class FeatureTypeApiTest extends TestCase
{
    use ApiTestTrait, WithoutMiddleware, DatabaseTransactions;

    /**
     * @test
     */
    public function test_create_feature_type()
    {
        $featureType = FeatureType::factory()->make()->toArray();

        $this->response = $this->json(
            'POST',
            '/api/feature_types', $featureType
        );

        $this->assertApiResponse($featureType);
    }

    /**
     * @test
     */
    public function test_read_feature_type()
    {
        $featureType = FeatureType::factory()->create();

        $this->response = $this->json(
            'GET',
            '/api/feature_types/'.$featureType->id
        );

        $this->assertApiResponse($featureType->toArray());
    }

    /**
     * @test
     */
    public function test_update_feature_type()
    {
        $featureType = FeatureType::factory()->create();
        $editedFeatureType = FeatureType::factory()->make()->toArray();

        $this->response = $this->json(
            'PUT',
            '/api/feature_types/'.$featureType->id,
            $editedFeatureType
        );

        $this->assertApiResponse($editedFeatureType);
    }

    /**
     * @test
     */
    public function test_delete_feature_type()
    {
        $featureType = FeatureType::factory()->create();

        $this->response = $this->json(
            'DELETE',
             '/api/feature_types/'.$featureType->id
         );

        $this->assertApiSuccess();
        $this->response = $this->json(
            'GET',
            '/api/feature_types/'.$featureType->id
        );

        $this->response->assertStatus(404);
    }
}
