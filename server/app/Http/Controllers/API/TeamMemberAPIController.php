<?php

namespace App\Http\Controllers\API;

use App\Http\Requests\API\CreateTeamMemberAPIRequest;
use App\Http\Requests\API\UpdateTeamMemberAPIRequest;
use App\Models\TeamMember;
use App\Repositories\TeamMemberRepository;
use Illuminate\Http\Request;
use App\Http\Controllers\AppBaseController;
use Response;

/**
 * Class TeamMemberController
 * @package App\Http\Controllers\API
 */

class TeamMemberAPIController extends AppBaseController
{
    /** @var  TeamMemberRepository */
    private $teamMemberRepository;

    public function __construct(TeamMemberRepository $teamMemberRepo)
    {
        $this->teamMemberRepository = $teamMemberRepo;
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Get(
     *      path="/teamMembers",
     *      summary="getTeamMemberList",
     *      tags={"TeamMember"},
     *      description="Get all TeamMembers",
     *      @OA\Response(
     *          response=200,
     *          description="successful operation",
     *          @OA\Schema(
     *              type="object",
     *              @OA\Property(
     *                  property="success",
     *                  type="boolean"
     *              ),
     *              @OA\Property(
     *                  property="data",
     *                  type="array",
     *                  @OA\Items(ref="#/definitions/TeamMember")
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function index(Request $request)
    {
        $teamMembers = $this->teamMemberRepository->all(
            $request->except(['skip', 'limit']),
            $request->get('skip'),
            $request->get('limit')
        );

        return $this->sendResponse($teamMembers->toArray(), 'Team Members retrieved successfully');
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Post(
     *      path="/teamMembers",
     *      summary="createTeamMember",
     *      tags={"TeamMember"},
     *      description="Create TeamMember",
     *      @OA\RequestBody(
     *        required=true,
     *        @OA\MediaType(
     *            mediaType="application/x-www-form-urlencoded",
     *            @OA\Schema(
     *                type="object",
     *                required={""},
     *                @OA\Property(
     *                    property="name",
     *                    description="desc",
     *                    type="string"
     *                )
     *            )
     *        )
     *      ),
     *      @OA\Response(
     *          response=200,
     *          description="successful operation",
     *          @OA\Schema(
     *              type="object",
     *              @OA\Property(
     *                  property="success",
     *                  type="boolean"
     *              ),
     *              @OA\Property(
     *                  property="data",
     *                  ref="#/definitions/TeamMember"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function store(CreateTeamMemberAPIRequest $request)
    {
        $input = $request->all();

        $teamMember = $this->teamMemberRepository->create($input);

        return $this->sendResponse($teamMember->toArray(), 'Team Member saved successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Get(
     *      path="/teamMembers/{id}",
     *      summary="getTeamMemberItem",
     *      tags={"TeamMember"},
     *      description="Get TeamMember",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of TeamMember",
     *           @OA\Schema(
     *             type="integer"
     *          ),
     *          required=true,
     *          in="path"
     *      ),
     *      @OA\Response(
     *          response=200,
     *          description="successful operation",
     *          @OA\Schema(
     *              type="object",
     *              @OA\Property(
     *                  property="success",
     *                  type="boolean"
     *              ),
     *              @OA\Property(
     *                  property="data",
     *                  ref="#/definitions/TeamMember"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function show($id)
    {
        /** @var TeamMember $teamMember */
        $teamMember = $this->teamMemberRepository->find($id);

        if (empty($teamMember)) {
            return $this->sendError('Team Member not found');
        }

        return $this->sendResponse($teamMember->toArray(), 'Team Member retrieved successfully');
    }

    /**
     * @param int $id
     * @param Request $request
     * @return Response
     *
     * @OA\Put(
     *      path="/teamMembers/{id}",
     *      summary="updateTeamMember",
     *      tags={"TeamMember"},
     *      description="Update TeamMember",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of TeamMember",
     *           @OA\Schema(
     *             type="integer"
     *          ),
     *          required=true,
     *          in="path"
     *      ),
     *      @OA\RequestBody(
     *        required=true,
     *        @OA\MediaType(
     *            mediaType="application/x-www-form-urlencoded",
     *            @OA\Schema(
     *                type="object",
     *                required={""},
     *                @OA\Property(
     *                    property="name",
     *                    description="desc",
     *                    type="string"
     *                )
     *            )
     *        )
     *      ),
     *      @OA\Response(
     *          response=200,
     *          description="successful operation",
     *          @OA\Schema(
     *              type="object",
     *              @OA\Property(
     *                  property="success",
     *                  type="boolean"
     *              ),
     *              @OA\Property(
     *                  property="data",
     *                  ref="#/definitions/TeamMember"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function update($id, UpdateTeamMemberAPIRequest $request)
    {
        $input = $request->all();

        /** @var TeamMember $teamMember */
        $teamMember = $this->teamMemberRepository->find($id);

        if (empty($teamMember)) {
            return $this->sendError('Team Member not found');
        }

        $teamMember = $this->teamMemberRepository->update($input, $id);

        return $this->sendResponse($teamMember->toArray(), 'TeamMember updated successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Delete(
     *      path="/teamMembers/{id}",
     *      summary="deleteTeamMember",
     *      tags={"TeamMember"},
     *      description="Delete TeamMember",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of TeamMember",
     *           @OA\Schema(
     *             type="integer"
     *          ),
     *          required=true,
     *          in="path"
     *      ),
     *      @OA\Response(
     *          response=200,
     *          description="successful operation",
     *          @OA\Schema(
     *              type="object",
     *              @OA\Property(
     *                  property="success",
     *                  type="boolean"
     *              ),
     *              @OA\Property(
     *                  property="data",
     *                  type="string"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function destroy($id)
    {
        /** @var TeamMember $teamMember */
        $teamMember = $this->teamMemberRepository->find($id);

        if (empty($teamMember)) {
            return $this->sendError('Team Member not found');
        }

        $teamMember->delete();

        return $this->sendSuccess('Team Member deleted successfully');
    }
}
