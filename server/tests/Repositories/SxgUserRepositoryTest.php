<?php namespace Tests\Repositories;

use App\Models\SxgUser;
use App\Repositories\SxgUserRepository;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;

class SxgUserRepositoryTest extends TestCase
{
    use ApiTestTrait, DatabaseTransactions;

    /**
     * @var SxgUserRepository
     */
    protected $sxgUserRepo;

    public function setUp() : void
    {
        parent::setUp();
        $this->sxgUserRepo = \App::make(SxgUserRepository::class);
    }

    /**
     * @test create
     */
    public function test_create_sxg_user()
    {
        $sxgUser = SxgUser::factory()->make()->toArray();

        $createdSxgUser = $this->sxgUserRepo->create($sxgUser);

        $createdSxgUser = $createdSxgUser->toArray();
        $this->assertArrayHasKey('id', $createdSxgUser);
        $this->assertNotNull($createdSxgUser['id'], 'Created SxgUser must have id specified');
        $this->assertNotNull(SxgUser::find($createdSxgUser['id']), 'SxgUser with given id must be in DB');
        $this->assertModelData($sxgUser, $createdSxgUser);
    }

    /**
     * @test read
     */
    public function test_read_sxg_user()
    {
        $sxgUser = SxgUser::factory()->create();

        $dbSxgUser = $this->sxgUserRepo->find($sxgUser->id);

        $dbSxgUser = $dbSxgUser->toArray();
        $this->assertModelData($sxgUser->toArray(), $dbSxgUser);
    }

    /**
     * @test update
     */
    public function test_update_sxg_user()
    {
        $sxgUser = SxgUser::factory()->create();
        $fakeSxgUser = SxgUser::factory()->make()->toArray();

        $updatedSxgUser = $this->sxgUserRepo->update($fakeSxgUser, $sxgUser->id);

        $this->assertModelData($fakeSxgUser, $updatedSxgUser->toArray());
        $dbSxgUser = $this->sxgUserRepo->find($sxgUser->id);
        $this->assertModelData($fakeSxgUser, $dbSxgUser->toArray());
    }

    /**
     * @test delete
     */
    public function test_delete_sxg_user()
    {
        $sxgUser = SxgUser::factory()->create();

        $resp = $this->sxgUserRepo->delete($sxgUser->id);

        $this->assertTrue($resp);
        $this->assertNull(SxgUser::find($sxgUser->id), 'SxgUser should not exist in DB');
    }
}
