<?php namespace Tests\APIs;

use Illuminate\Foundation\Testing\WithoutMiddleware;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;
use App\Models\Asset;

class AssetApiTest extends TestCase
{
    use ApiTestTrait, WithoutMiddleware, DatabaseTransactions;

    /**
     * @test
     */
    public function test_create_asset()
    {
        $asset = Asset::factory()->make()->toArray();

        $this->response = $this->json(
            'POST',
            '/api/assets', $asset
        );

        $this->assertApiResponse($asset);
    }

    /**
     * @test
     */
    public function test_read_asset()
    {
        $asset = Asset::factory()->create();

        $this->response = $this->json(
            'GET',
            '/api/assets/'.$asset->id
        );

        $this->assertApiResponse($asset->toArray());
    }

    /**
     * @test
     */
    public function test_update_asset()
    {
        $asset = Asset::factory()->create();
        $editedAsset = Asset::factory()->make()->toArray();

        $this->response = $this->json(
            'PUT',
            '/api/assets/'.$asset->id,
            $editedAsset
        );

        $this->assertApiResponse($editedAsset);
    }

    /**
     * @test
     */
    public function test_delete_asset()
    {
        $asset = Asset::factory()->create();

        $this->response = $this->json(
            'DELETE',
             '/api/assets/'.$asset->id
         );

        $this->assertApiSuccess();
        $this->response = $this->json(
            'GET',
            '/api/assets/'.$asset->id
        );

        $this->response->assertStatus(404);
    }
}
