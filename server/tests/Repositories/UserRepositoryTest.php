<?php namespace Tests\Repositories;

use App\Models\User;
use App\Repositories\UserRepository;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;

class UserRepositoryTest extends TestCase
{
    use ApiTestTrait, DatabaseTransactions;

    /**
     * @var UserRepository
     */
    protected $userRepo;

    public function setUp() : void
    {
        parent::setUp();
        $this->userRepo = \App::make(UserRepository::class);
    }

    /**
     * @test create
     */
    public function test_create_user()
    {
        $user = User::factory()->make()->toArray();

        $createdUser = $this->userRepo->create($user);

        $createdUser = $createdUser->toArray();
        $this->assertArrayHasKey('id', $createdUser);
        $this->assertNotNull($createdUser['id'], 'Created User must have id specified');
        $this->assertNotNull(User::find($createdUser['id']), 'User with given id must be in DB');
        $this->assertModelData($user, $createdUser);
    }

    /**
     * @test read
     */
    public function test_read_user()
    {
        $user = User::factory()->create();

        $dbUser = $this->userRepo->find($user->id);

        $dbUser = $dbUser->toArray();
        $this->assertModelData($user->toArray(), $dbUser);
    }

    /**
     * @test update
     */
    public function test_update_user()
    {
        $user = User::factory()->create();
        $fakeUser = User::factory()->make()->toArray();

        $updatedUser = $this->userRepo->update($fakeUser, $user->id);

        $this->assertModelData($fakeUser, $updatedUser->toArray());
        $dbUser = $this->userRepo->find($user->id);
        $this->assertModelData($fakeUser, $dbUser->toArray());
    }

    /**
     * @test delete
     */
    public function test_delete_user()
    {
        $user = User::factory()->create();

        $resp = $this->userRepo->delete($user->id);

        $this->assertTrue($resp);
        $this->assertNull(User::find($user->id), 'User should not exist in DB');
    }
}
