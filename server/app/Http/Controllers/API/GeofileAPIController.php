<?php

namespace App\Http\Controllers\API;

use App\Http\Requests\API\CreateGeofileAPIRequest;
use App\Http\Requests\API\UpdateGeofileAPIRequest;
use App\Models\Geofile;
use App\Repositories\GeofileRepository;
use Illuminate\Http\Request;
use App\Http\Controllers\AppBaseController;
use Response;

/**
 * Class GeofileController
 * @package App\Http\Controllers\API
 */

class GeofileAPIController extends AppBaseController
{
    /** @var  GeofileRepository */
    private $geofileRepository;

    public function __construct(GeofileRepository $geofileRepo)
    {
        $this->geofileRepository = $geofileRepo;
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Get(
     *      path="/geofiles",
     *      summary="getGeofileList",
     *      tags={"Geofile"},
     *      description="Get all Geofiles",
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
     *                  @OA\Items(ref="#/definitions/Geofile")
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
        $geofiles = $this->geofileRepository->all(
            $request->except(['skip', 'limit']),
            $request->get('skip'),
            $request->get('limit')
        );

        return $this->sendResponse($geofiles->toArray(), 'Geofiles retrieved successfully');
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Post(
     *      path="/geofiles",
     *      summary="createGeofile",
     *      tags={"Geofile"},
     *      description="Create Geofile",
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
     *                  ref="#/definitions/Geofile"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function store(CreateGeofileAPIRequest $request)
    {
        $input = $request->all();

        $geofile = $this->geofileRepository->create($input);

        return $this->sendResponse($geofile->toArray(), 'Geofile saved successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Get(
     *      path="/geofiles/{id}",
     *      summary="getGeofileItem",
     *      tags={"Geofile"},
     *      description="Get Geofile",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of Geofile",
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
     *                  ref="#/definitions/Geofile"
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
        /** @var Geofile $geofile */
        $geofile = $this->geofileRepository->find($id);

        if (empty($geofile)) {
            return $this->sendError('Geofile not found');
        }

        return $this->sendResponse($geofile->toArray(), 'Geofile retrieved successfully');
    }

    /**
     * @param int $id
     * @param Request $request
     * @return Response
     *
     * @OA\Put(
     *      path="/geofiles/{id}",
     *      summary="updateGeofile",
     *      tags={"Geofile"},
     *      description="Update Geofile",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of Geofile",
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
     *                  ref="#/definitions/Geofile"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function update($id, UpdateGeofileAPIRequest $request)
    {
        $input = $request->all();

        /** @var Geofile $geofile */
        $geofile = $this->geofileRepository->find($id);

        if (empty($geofile)) {
            return $this->sendError('Geofile not found');
        }

        $geofile = $this->geofileRepository->update($input, $id);

        return $this->sendResponse($geofile->toArray(), 'Geofile updated successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Delete(
     *      path="/geofiles/{id}",
     *      summary="deleteGeofile",
     *      tags={"Geofile"},
     *      description="Delete Geofile",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of Geofile",
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
        /** @var Geofile $geofile */
        $geofile = $this->geofileRepository->find($id);

        if (empty($geofile)) {
            return $this->sendError('Geofile not found');
        }

        $geofile->delete();

        return $this->sendSuccess('Geofile deleted successfully');
    }
}
