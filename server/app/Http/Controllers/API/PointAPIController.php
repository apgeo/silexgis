<?php

namespace App\Http\Controllers\API;

use App\Http\Requests\API\CreatePointAPIRequest;
use App\Http\Requests\API\UpdatePointAPIRequest;
use App\Models\Point;
use App\Repositories\PointRepository;
use Illuminate\Http\Request;
use App\Http\Controllers\AppBaseController;
use Response;

/**
 * Class PointController
 * @package App\Http\Controllers\API
 */

class PointAPIController extends AppBaseController
{
    /** @var  PointRepository */
    private $pointRepository;

    public function __construct(PointRepository $pointRepo)
    {
        $this->pointRepository = $pointRepo;
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Get(
     *      path="/points",
     *      summary="getPointList",
     *      tags={"Point"},
     *      description="Get all Points",
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
     *                  @OA\Items(ref="#/definitions/Point")
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
        $points = $this->pointRepository->all(
            $request->except(['skip', 'limit']),
            $request->get('skip'),
            $request->get('limit')
        );

        return $this->sendResponse($points->toArray(), 'Points retrieved successfully');
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Post(
     *      path="/points",
     *      summary="createPoint",
     *      tags={"Point"},
     *      description="Create Point",
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
     *                  ref="#/definitions/Point"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function store(CreatePointAPIRequest $request)
    {
        $input = $request->all();

        $point = $this->pointRepository->create($input);

        return $this->sendResponse($point->toArray(), 'Point saved successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Get(
     *      path="/points/{id}",
     *      summary="getPointItem",
     *      tags={"Point"},
     *      description="Get Point",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of Point",
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
     *                  ref="#/definitions/Point"
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
        /** @var Point $point */
        $point = $this->pointRepository->find($id);

        if (empty($point)) {
            return $this->sendError('Point not found');
        }

        return $this->sendResponse($point->toArray(), 'Point retrieved successfully');
    }

    /**
     * @param int $id
     * @param Request $request
     * @return Response
     *
     * @OA\Put(
     *      path="/points/{id}",
     *      summary="updatePoint",
     *      tags={"Point"},
     *      description="Update Point",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of Point",
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
     *                  ref="#/definitions/Point"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function update($id, UpdatePointAPIRequest $request)
    {
        $input = $request->all();

        /** @var Point $point */
        $point = $this->pointRepository->find($id);

        if (empty($point)) {
            return $this->sendError('Point not found');
        }

        $point = $this->pointRepository->update($input, $id);

        return $this->sendResponse($point->toArray(), 'Point updated successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Delete(
     *      path="/points/{id}",
     *      summary="deletePoint",
     *      tags={"Point"},
     *      description="Delete Point",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of Point",
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
        /** @var Point $point */
        $point = $this->pointRepository->find($id);

        if (empty($point)) {
            return $this->sendError('Point not found');
        }

        $point->delete();

        return $this->sendSuccess('Point deleted successfully');
    }
}
