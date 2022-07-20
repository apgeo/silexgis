<?php

namespace App\Http\Controllers\API;

use App\Http\Requests\API\CreateSxgUserAPIRequest;
use App\Http\Requests\API\UpdateSxgUserAPIRequest;
use App\Models\SxgUser;
use App\Repositories\SxgUserRepository;
use Illuminate\Http\Request;
use App\Http\Controllers\AppBaseController;
use Response;

/**
 * Class SxgUserController
 * @package App\Http\Controllers\API
 */

class SxgUserAPIController extends AppBaseController
{
    /** @var  SxgUserRepository */
    private $sxgUserRepository;

    public function __construct(SxgUserRepository $sxgUserRepo)
    {
        $this->sxgUserRepository = $sxgUserRepo;
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Get(
     *      path="/sxgUsers",
     *      summary="getSxgUserList",
     *      tags={"SxgUser"},
     *      description="Get all SxgUsers",
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
     *                  @OA\Items(ref="#/definitions/SxgUser")
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
        $sxgUsers = $this->sxgUserRepository->all(
            $request->except(['skip', 'limit']),
            $request->get('skip'),
            $request->get('limit')
        );

        return $this->sendResponse($sxgUsers->toArray(), 'Sxg Users retrieved successfully');
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Post(
     *      path="/sxgUsers",
     *      summary="createSxgUser",
     *      tags={"SxgUser"},
     *      description="Create SxgUser",
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
     *                  ref="#/definitions/SxgUser"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function store(CreateSxgUserAPIRequest $request)
    {
        $input = $request->all();

        $sxgUser = $this->sxgUserRepository->create($input);

        return $this->sendResponse($sxgUser->toArray(), 'Sxg User saved successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Get(
     *      path="/sxgUsers/{id}",
     *      summary="getSxgUserItem",
     *      tags={"SxgUser"},
     *      description="Get SxgUser",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of SxgUser",
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
     *                  ref="#/definitions/SxgUser"
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
        /** @var SxgUser $sxgUser */
        $sxgUser = $this->sxgUserRepository->find($id);

        if (empty($sxgUser)) {
            return $this->sendError('Sxg User not found');
        }

        return $this->sendResponse($sxgUser->toArray(), 'Sxg User retrieved successfully');
    }

    /**
     * @param int $id
     * @param Request $request
     * @return Response
     *
     * @OA\Put(
     *      path="/sxgUsers/{id}",
     *      summary="updateSxgUser",
     *      tags={"SxgUser"},
     *      description="Update SxgUser",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of SxgUser",
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
     *                  ref="#/definitions/SxgUser"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function update($id, UpdateSxgUserAPIRequest $request)
    {
        $input = $request->all();

        /** @var SxgUser $sxgUser */
        $sxgUser = $this->sxgUserRepository->find($id);

        if (empty($sxgUser)) {
            return $this->sendError('Sxg User not found');
        }

        $sxgUser = $this->sxgUserRepository->update($input, $id);

        return $this->sendResponse($sxgUser->toArray(), 'SxgUser updated successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Delete(
     *      path="/sxgUsers/{id}",
     *      summary="deleteSxgUser",
     *      tags={"SxgUser"},
     *      description="Delete SxgUser",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of SxgUser",
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
        /** @var SxgUser $sxgUser */
        $sxgUser = $this->sxgUserRepository->find($id);

        if (empty($sxgUser)) {
            return $this->sendError('Sxg User not found');
        }

        $sxgUser->delete();

        return $this->sendSuccess('Sxg User deleted successfully');
    }
}
