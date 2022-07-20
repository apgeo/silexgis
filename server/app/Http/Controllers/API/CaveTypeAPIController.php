<?php

namespace App\Http\Controllers\API;

use App\Http\Requests\API\CreateCaveTypeAPIRequest;
use App\Http\Requests\API\UpdateCaveTypeAPIRequest;
use App\Models\CaveType;
use App\Repositories\CaveTypeRepository;
use Illuminate\Http\Request;
use App\Http\Controllers\AppBaseController;
use Response;

/**
 * Class CaveTypeController
 * @package App\Http\Controllers\API
 */

class CaveTypeAPIController extends AppBaseController
{
    /** @var  CaveTypeRepository */
    private $caveTypeRepository;

    public function __construct(CaveTypeRepository $caveTypeRepo)
    {
        $this->caveTypeRepository = $caveTypeRepo;
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Get(
     *      path="/caveTypes",
     *      summary="getCaveTypeList",
     *      tags={"CaveType"},
     *      description="Get all CaveTypes",
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
     *                  @OA\Items(ref="#/definitions/CaveType")
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
        $caveTypes = $this->caveTypeRepository->all(
            $request->except(['skip', 'limit']),
            $request->get('skip'),
            $request->get('limit')
        );

        return $this->sendResponse($caveTypes->toArray(), 'Cave Types retrieved successfully');
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Post(
     *      path="/caveTypes",
     *      summary="createCaveType",
     *      tags={"CaveType"},
     *      description="Create CaveType",
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
     *                  ref="#/definitions/CaveType"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function store(CreateCaveTypeAPIRequest $request)
    {
        $input = $request->all();

        $caveType = $this->caveTypeRepository->create($input);

        return $this->sendResponse($caveType->toArray(), 'Cave Type saved successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Get(
     *      path="/caveTypes/{id}",
     *      summary="getCaveTypeItem",
     *      tags={"CaveType"},
     *      description="Get CaveType",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of CaveType",
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
     *                  ref="#/definitions/CaveType"
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
        /** @var CaveType $caveType */
        $caveType = $this->caveTypeRepository->find($id);

        if (empty($caveType)) {
            return $this->sendError('Cave Type not found');
        }

        return $this->sendResponse($caveType->toArray(), 'Cave Type retrieved successfully');
    }

    /**
     * @param int $id
     * @param Request $request
     * @return Response
     *
     * @OA\Put(
     *      path="/caveTypes/{id}",
     *      summary="updateCaveType",
     *      tags={"CaveType"},
     *      description="Update CaveType",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of CaveType",
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
     *                  ref="#/definitions/CaveType"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function update($id, UpdateCaveTypeAPIRequest $request)
    {
        $input = $request->all();

        /** @var CaveType $caveType */
        $caveType = $this->caveTypeRepository->find($id);

        if (empty($caveType)) {
            return $this->sendError('Cave Type not found');
        }

        $caveType = $this->caveTypeRepository->update($input, $id);

        return $this->sendResponse($caveType->toArray(), 'CaveType updated successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Delete(
     *      path="/caveTypes/{id}",
     *      summary="deleteCaveType",
     *      tags={"CaveType"},
     *      description="Delete CaveType",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of CaveType",
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
        /** @var CaveType $caveType */
        $caveType = $this->caveTypeRepository->find($id);

        if (empty($caveType)) {
            return $this->sendError('Cave Type not found');
        }

        $caveType->delete();

        return $this->sendSuccess('Cave Type deleted successfully');
    }
}
