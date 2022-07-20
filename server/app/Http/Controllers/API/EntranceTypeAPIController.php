<?php

namespace App\Http\Controllers\API;

use App\Http\Requests\API\CreateEntranceTypeAPIRequest;
use App\Http\Requests\API\UpdateEntranceTypeAPIRequest;
use App\Models\EntranceType;
use App\Repositories\EntranceTypeRepository;
use Illuminate\Http\Request;
use App\Http\Controllers\AppBaseController;
use Response;

/**
 * Class EntranceTypeController
 * @package App\Http\Controllers\API
 */

class EntranceTypeAPIController extends AppBaseController
{
    /** @var  EntranceTypeRepository */
    private $entranceTypeRepository;

    public function __construct(EntranceTypeRepository $entranceTypeRepo)
    {
        $this->entranceTypeRepository = $entranceTypeRepo;
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Get(
     *      path="/entranceTypes",
     *      summary="getEntranceTypeList",
     *      tags={"EntranceType"},
     *      description="Get all EntranceTypes",
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
     *                  @OA\Items(ref="#/definitions/EntranceType")
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
        $entranceTypes = $this->entranceTypeRepository->all(
            $request->except(['skip', 'limit']),
            $request->get('skip'),
            $request->get('limit')
        );

        return $this->sendResponse($entranceTypes->toArray(), 'Entrance Types retrieved successfully');
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Post(
     *      path="/entranceTypes",
     *      summary="createEntranceType",
     *      tags={"EntranceType"},
     *      description="Create EntranceType",
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
     *                  ref="#/definitions/EntranceType"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function store(CreateEntranceTypeAPIRequest $request)
    {
        $input = $request->all();

        $entranceType = $this->entranceTypeRepository->create($input);

        return $this->sendResponse($entranceType->toArray(), 'Entrance Type saved successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Get(
     *      path="/entranceTypes/{id}",
     *      summary="getEntranceTypeItem",
     *      tags={"EntranceType"},
     *      description="Get EntranceType",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of EntranceType",
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
     *                  ref="#/definitions/EntranceType"
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
        /** @var EntranceType $entranceType */
        $entranceType = $this->entranceTypeRepository->find($id);

        if (empty($entranceType)) {
            return $this->sendError('Entrance Type not found');
        }

        return $this->sendResponse($entranceType->toArray(), 'Entrance Type retrieved successfully');
    }

    /**
     * @param int $id
     * @param Request $request
     * @return Response
     *
     * @OA\Put(
     *      path="/entranceTypes/{id}",
     *      summary="updateEntranceType",
     *      tags={"EntranceType"},
     *      description="Update EntranceType",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of EntranceType",
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
     *                  ref="#/definitions/EntranceType"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function update($id, UpdateEntranceTypeAPIRequest $request)
    {
        $input = $request->all();

        /** @var EntranceType $entranceType */
        $entranceType = $this->entranceTypeRepository->find($id);

        if (empty($entranceType)) {
            return $this->sendError('Entrance Type not found');
        }

        $entranceType = $this->entranceTypeRepository->update($input, $id);

        return $this->sendResponse($entranceType->toArray(), 'EntranceType updated successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Delete(
     *      path="/entranceTypes/{id}",
     *      summary="deleteEntranceType",
     *      tags={"EntranceType"},
     *      description="Delete EntranceType",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of EntranceType",
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
        /** @var EntranceType $entranceType */
        $entranceType = $this->entranceTypeRepository->find($id);

        if (empty($entranceType)) {
            return $this->sendError('Entrance Type not found');
        }

        $entranceType->delete();

        return $this->sendSuccess('Entrance Type deleted successfully');
    }
}
