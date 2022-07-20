<?php namespace Tests\APIs;

use Illuminate\Foundation\Testing\WithoutMiddleware;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;
use App\Models\Tag;

class TagApiTest extends TestCase
{
    use ApiTestTrait, WithoutMiddleware, DatabaseTransactions;

    /**
     * @test
     */
    public function test_create_tag()
    {
        $tag = Tag::factory()->make()->toArray();

        $this->response = $this->json(
            'POST',
            '/api/tags', $tag
        );

        $this->assertApiResponse($tag);
    }

    /**
     * @test
     */
    public function test_read_tag()
    {
        $tag = Tag::factory()->create();

        $this->response = $this->json(
            'GET',
            '/api/tags/'.$tag->id
        );

        $this->assertApiResponse($tag->toArray());
    }

    /**
     * @test
     */
    public function test_update_tag()
    {
        $tag = Tag::factory()->create();
        $editedTag = Tag::factory()->make()->toArray();

        $this->response = $this->json(
            'PUT',
            '/api/tags/'.$tag->id,
            $editedTag
        );

        $this->assertApiResponse($editedTag);
    }

    /**
     * @test
     */
    public function test_delete_tag()
    {
        $tag = Tag::factory()->create();

        $this->response = $this->json(
            'DELETE',
             '/api/tags/'.$tag->id
         );

        $this->assertApiSuccess();
        $this->response = $this->json(
            'GET',
            '/api/tags/'.$tag->id
        );

        $this->response->assertStatus(404);
    }
}
