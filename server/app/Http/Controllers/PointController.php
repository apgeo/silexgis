<?php

namespace App\Http\Controllers;

use App\Http\Requests\CreatePointRequest;
use App\Http\Requests\UpdatePointRequest;
use App\Repositories\PointRepository;
use App\Http\Controllers\AppBaseController;
use Illuminate\Http\Request;
use Flash;
use Response;

class PointController extends AppBaseController
{
    /** @var PointRepository $pointRepository*/
    private $pointRepository;

    public function __construct(PointRepository $pointRepo)
    {
        $this->pointRepository = $pointRepo;
    }

    /**
     * Display a listing of the Point.
     *
     * @param Request $request
     *
     * @return Response
     */
    public function index(Request $request)
    {
        $points = $this->pointRepository->all();

        return view('points.index')
            ->with('points', $points);
    }

    /**
     * Show the form for creating a new Point.
     *
     * @return Response
     */
    public function create()
    {
        return view('points.create');
    }

    /**
     * Store a newly created Point in storage.
     *
     * @param CreatePointRequest $request
     *
     * @return Response
     */
    public function store(CreatePointRequest $request)
    {
        $input = $request->all();

        $point = $this->pointRepository->create($input);

        Flash::success('Point saved successfully.');

        return redirect(route('points.index'));
    }

    /**
     * Display the specified Point.
     *
     * @param int $id
     *
     * @return Response
     */
    public function show($id)
    {
        $point = $this->pointRepository->find($id);

        if (empty($point)) {
            Flash::error('Point not found');

            return redirect(route('points.index'));
        }

        return view('points.show')->with('point', $point);
    }

    /**
     * Show the form for editing the specified Point.
     *
     * @param int $id
     *
     * @return Response
     */
    public function edit($id)
    {
        $point = $this->pointRepository->find($id);

        if (empty($point)) {
            Flash::error('Point not found');

            return redirect(route('points.index'));
        }

        return view('points.edit')->with('point', $point);
    }

    /**
     * Update the specified Point in storage.
     *
     * @param int $id
     * @param UpdatePointRequest $request
     *
     * @return Response
     */
    public function update($id, UpdatePointRequest $request)
    {
        $point = $this->pointRepository->find($id);

        if (empty($point)) {
            Flash::error('Point not found');

            return redirect(route('points.index'));
        }

        $point = $this->pointRepository->update($request->all(), $id);

        Flash::success('Point updated successfully.');

        return redirect(route('points.index'));
    }

    /**
     * Remove the specified Point from storage.
     *
     * @param int $id
     *
     * @throws \Exception
     *
     * @return Response
     */
    public function destroy($id)
    {
        $point = $this->pointRepository->find($id);

        if (empty($point)) {
            Flash::error('Point not found');

            return redirect(route('points.index'));
        }

        $this->pointRepository->delete($id);

        Flash::success('Point deleted successfully.');

        return redirect(route('points.index'));
    }
}
