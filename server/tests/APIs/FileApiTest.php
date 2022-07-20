<?php namespace Tests\APIs;

use Illuminate\Foundation\Testing\WithoutMiddleware;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;
use App\Models\File;

class FileApiTest extends TestCase
{
    use ApiTestTrait, WithoutMiddleware, DatabaseTransactions;

    /**
     * @test
     */
    public function test_create_file()
    {
        $file = File::factory()->make()->toArray();

        $this->response = $this->json(
            'POST',
            '/api/files', $file
        );

        $this->assertApiResponse($file);
    }

    /**
     * @test
     */
    public function test_read_file()
    {
        $file = File::factory()->create();

        $this->response = $this->json(
            'GET',
            '/api/files/'.$file->id
        );

        $this->assertApiResponse($file->toArray());
    }

    /**
     * @test
     */
    public function test_update_file()
    {
        $file = File::factory()->create();
        $editedFile = File::factory()->make()->toArray();

        $this->response = $this->json(
            'PUT',
            '/api/files/'.$file->id,
            $editedFile
        );

        $this->assertApiResponse($editedFile);
    }

    /**
     * @test
     */
    public function test_delete_file()
    {
        $file = File::factory()->create();

        $this->response = $this->json(
            'DELETE',
             '/api/files/'.$file->id
         );

        $this->assertApiSuccess();
        $this->response = $this->json(
            'GET',
            '/api/files/'.$file->id
        );

        $this->response->assertStatus(404);
    }
}
