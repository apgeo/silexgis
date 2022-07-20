<?php namespace Tests\APIs;

use Illuminate\Foundation\Testing\WithoutMiddleware;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;
use App\Models\TeamMember;

class TeamMemberApiTest extends TestCase
{
    use ApiTestTrait, WithoutMiddleware, DatabaseTransactions;

    /**
     * @test
     */
    public function test_create_team_member()
    {
        $teamMember = TeamMember::factory()->make()->toArray();

        $this->response = $this->json(
            'POST',
            '/api/team_members', $teamMember
        );

        $this->assertApiResponse($teamMember);
    }

    /**
     * @test
     */
    public function test_read_team_member()
    {
        $teamMember = TeamMember::factory()->create();

        $this->response = $this->json(
            'GET',
            '/api/team_members/'.$teamMember->id
        );

        $this->assertApiResponse($teamMember->toArray());
    }

    /**
     * @test
     */
    public function test_update_team_member()
    {
        $teamMember = TeamMember::factory()->create();
        $editedTeamMember = TeamMember::factory()->make()->toArray();

        $this->response = $this->json(
            'PUT',
            '/api/team_members/'.$teamMember->id,
            $editedTeamMember
        );

        $this->assertApiResponse($editedTeamMember);
    }

    /**
     * @test
     */
    public function test_delete_team_member()
    {
        $teamMember = TeamMember::factory()->create();

        $this->response = $this->json(
            'DELETE',
             '/api/team_members/'.$teamMember->id
         );

        $this->assertApiSuccess();
        $this->response = $this->json(
            'GET',
            '/api/team_members/'.$teamMember->id
        );

        $this->response->assertStatus(404);
    }
}
