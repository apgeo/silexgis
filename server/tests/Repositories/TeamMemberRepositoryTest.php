<?php namespace Tests\Repositories;

use App\Models\TeamMember;
use App\Repositories\TeamMemberRepository;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;

class TeamMemberRepositoryTest extends TestCase
{
    use ApiTestTrait, DatabaseTransactions;

    /**
     * @var TeamMemberRepository
     */
    protected $teamMemberRepo;

    public function setUp() : void
    {
        parent::setUp();
        $this->teamMemberRepo = \App::make(TeamMemberRepository::class);
    }

    /**
     * @test create
     */
    public function test_create_team_member()
    {
        $teamMember = TeamMember::factory()->make()->toArray();

        $createdTeamMember = $this->teamMemberRepo->create($teamMember);

        $createdTeamMember = $createdTeamMember->toArray();
        $this->assertArrayHasKey('id', $createdTeamMember);
        $this->assertNotNull($createdTeamMember['id'], 'Created TeamMember must have id specified');
        $this->assertNotNull(TeamMember::find($createdTeamMember['id']), 'TeamMember with given id must be in DB');
        $this->assertModelData($teamMember, $createdTeamMember);
    }

    /**
     * @test read
     */
    public function test_read_team_member()
    {
        $teamMember = TeamMember::factory()->create();

        $dbTeamMember = $this->teamMemberRepo->find($teamMember->id);

        $dbTeamMember = $dbTeamMember->toArray();
        $this->assertModelData($teamMember->toArray(), $dbTeamMember);
    }

    /**
     * @test update
     */
    public function test_update_team_member()
    {
        $teamMember = TeamMember::factory()->create();
        $fakeTeamMember = TeamMember::factory()->make()->toArray();

        $updatedTeamMember = $this->teamMemberRepo->update($fakeTeamMember, $teamMember->id);

        $this->assertModelData($fakeTeamMember, $updatedTeamMember->toArray());
        $dbTeamMember = $this->teamMemberRepo->find($teamMember->id);
        $this->assertModelData($fakeTeamMember, $dbTeamMember->toArray());
    }

    /**
     * @test delete
     */
    public function test_delete_team_member()
    {
        $teamMember = TeamMember::factory()->create();

        $resp = $this->teamMemberRepo->delete($teamMember->id);

        $this->assertTrue($resp);
        $this->assertNull(TeamMember::find($teamMember->id), 'TeamMember should not exist in DB');
    }
}
