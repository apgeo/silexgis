<?php

namespace App\Http\Controllers\API;

use App\Http\Requests\API\CreateCaveEntranceAPIRequest;
use App\Http\Requests\API\UpdateCaveEntranceAPIRequest;
use App\Models\CaveEntrance;
use App\Repositories\CaveEntranceRepository;
use Illuminate\Http\Request;
use App\Http\Controllers\AppBaseController;
use Response;

/**
 * Class CaveEntranceController
 * @package App\Http\Controllers\API
 */

class CaveEntranceAPIController extends AppBaseController
{
    /** @var  CaveEntranceRepository */
    private $caveEntranceRepository;

    public function __construct(CaveEntranceRepository $caveEntranceRepo)
    {
        $this->caveEntranceRepository = $caveEntranceRepo;
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Get(
     *      path="/caveEntrances",
     *      summary="getCaveEntranceList",
     *      tags={"CaveEntrance"},
     *      description="Get all CaveEntrances",
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
     *                  @OA\Items(ref="#/definitions/CaveEntrance")
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
        $caveEntrances = $this->caveEntranceRepository->all(
            $request->except(['skip', 'limit']),
            $request->get('skip'),
            $request->get('limit')
        );

        return $this->sendResponse($caveEntrances->toArray(), 'Cave Entrances retrieved successfully');
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Post(
     *      path="/caveEntrances",
     *      summary="createCaveEntrance",
     *      tags={"CaveEntrance"},
     *      description="Create CaveEntrance",
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
     *                  ref="#/definitions/CaveEntrance"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function store(CreateCaveEntranceAPIRequest $request)
    {
        $input = $request->all();

        $caveEntrance = $this->caveEntranceRepository->create($input);

        return $this->sendResponse($caveEntrance->toArray(), 'Cave Entrance saved successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Get(
     *      path="/caveEntrances/{id}",
     *      summary="getCaveEntranceItem",
     *      tags={"CaveEntrance"},
     *      description="Get CaveEntrance",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of CaveEntrance",
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
     *                  ref="#/definitions/CaveEntrance"
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
        /** @var CaveEntrance $caveEntrance */
        $caveEntrance = $this->caveEntranceRepository->find($id);

        if (empty($caveEntrance)) {
            return $this->sendError('Cave Entrance not found');
        }

        return $this->sendResponse($caveEntrance->toArray(), 'Cave Entrance retrieved successfully');
    }

    /**
     * @param int $id
     * @param Request $request
     * @return Response
     *
     * @OA\Put(
     *      path="/caveEntrances/{id}",
     *      summary="updateCaveEntrance",
     *      tags={"CaveEntrance"},
     *      description="Update CaveEntrance",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of CaveEntrance",
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
     *                  ref="#/definitions/CaveEntrance"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function update($id, UpdateCaveEntranceAPIRequest $request)
    {
        $input = $request->all();

        /** @var CaveEntrance $caveEntrance */
        $caveEntrance = $this->caveEntranceRepository->find($id);

        if (empty($caveEntrance)) {
            return $this->sendError('Cave Entrance not found');
        }

        $caveEntrance = $this->caveEntranceRepository->update($input, $id);

        return $this->sendResponse($caveEntrance->toArray(), 'CaveEntrance updated successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Delete(
     *      path="/caveEntrances/{id}",
     *      summary="deleteCaveEntrance",
     *      tags={"CaveEntrance"},
     *      description="Delete CaveEntrance",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of CaveEntrance",
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
        /** @var CaveEntrance $caveEntrance */
        $caveEntrance = $this->caveEntranceRepository->find($id);

        if (empty($caveEntrance)) {
            return $this->sendError('Cave Entrance not found');
        }

        $caveEntrance->delete();

        return $this->sendSuccess('Cave Entrance deleted successfully');
    }
}
