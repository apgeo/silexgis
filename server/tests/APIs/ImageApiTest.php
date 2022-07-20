<?php namespace Tests\APIs;

use Illuminate\Foundation\Testing\WithoutMiddleware;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;
use App\Models\Image;

class ImageApiTest extends TestCase
{
    use ApiTestTrait, WithoutMiddleware, DatabaseTransactions;

    /**
     * @test
     */
    public function test_create_image()
    {
        $image = Image::factory()->make()->toArray();

        $this->response = $this->json(
            'POST',
            '/api/images', $image
        );

        $this->assertApiResponse($image);
    }

    /**
     * @test
     */
    public function test_read_image()
    {
        $image = Image::factory()->create();

        $this->response = $this->json(
            'GET',
            '/api/images/'.$image->id
        );

        $this->assertApiResponse($image->toArray());
    }

    /**
     * @test
     */
    public function test_update_image()
    {
        $image = Image::factory()->create();
        $editedImage = Image::factory()->make()->toArray();

        $this->response = $this->json(
            'PUT',
            '/api/images/'.$image->id,
            $editedImage
        );

        $this->assertApiResponse($editedImage);
    }

    /**
     * @test
     */
    public function test_delete_image()
    {
        $image = Image::factory()->create();

        $this->response = $this->json(
            'DELETE',
             '/api/images/'.$image->id
         );

        $this->assertApiSuccess();
        $this->response = $this->json(
            'GET',
            '/api/images/'.$image->id
        );

        $this->response->assertStatus(404);
    }
}
