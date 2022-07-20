<?php namespace Tests\APIs;

use Illuminate\Foundation\Testing\WithoutMiddleware;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;
use App\Models\SxgUser;

class SxgUserApiTest extends TestCase
{
    use ApiTestTrait, WithoutMiddleware, DatabaseTransactions;

    /**
     * @test
     */
    public function test_create_sxg_user()
    {
        $sxgUser = SxgUser::factory()->make()->toArray();

        $this->response = $this->json(
            'POST',
            '/api/sxg_users', $sxgUser
        );

        $this->assertApiResponse($sxgUser);
    }

    /**
     * @test
     */
    public function test_read_sxg_user()
    {
        $sxgUser = SxgUser::factory()->create();

        $this->response = $this->json(
            'GET',
            '/api/sxg_users/'.$sxgUser->id
        );

        $this->assertApiResponse($sxgUser->toArray());
    }

    /**
     * @test
     */
    public function test_update_sxg_user()
    {
        $sxgUser = SxgUser::factory()->create();
        $editedSxgUser = SxgUser::factory()->make()->toArray();

        $this->response = $this->json(
            'PUT',
            '/api/sxg_users/'.$sxgUser->id,
            $editedSxgUser
        );

        $this->assertApiResponse($editedSxgUser);
    }

    /**
     * @test
     */
    public function test_delete_sxg_user()
    {
        $sxgUser = SxgUser::factory()->create();

        $this->response = $this->json(
            'DELETE',
             '/api/sxg_users/'.$sxgUser->id
         );

        $this->assertApiSuccess();
        $this->response = $this->json(
            'GET',
            '/api/sxg_users/'.$sxgUser->id
        );

        $this->response->assertStatus(404);
    }
}
