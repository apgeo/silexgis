<?php

namespace App\Http\Controllers;

use App\Http\Requests\CreateCaveRequest;
use App\Http\Requests\UpdateCaveRequest;
use App\Repositories\CaveRepository;
use App\Http\Controllers\AppBaseController;
use Illuminate\Http\Request;
use Flash;
use Response;

class CaveController extends AppBaseController
{
    /** @var CaveRepository $caveRepository*/
    private $caveRepository;

    public function __construct(CaveRepository $caveRepo)
    {
        $this->caveRepository = $caveRepo;
    }

    /**
     * Display a listing of the Cave.
     *
     * @param Request $request
     *
     * @return Response
     */
    public function index(Request $request)
    {
        $caves = $this->caveRepository->all();

        return view('caves.index')
            ->with('caves', $caves);
    }

    /**
     * Show the form for creating a new Cave.
     *
     * @return Response
     */
    public function create()
    {
        return view('caves.create');
    }

    /**
     * Store a newly created Cave in storage.
     *
     * @param CreateCaveRequest $request
     *
     * @return Response
     */
    public function store(CreateCaveRequest $request)
    {
        $input = $request->all();

        $cave = $this->caveRepository->create($input);

        Flash::success('Cave saved successfully.');

        return redirect(route('caves.index'));
    }

    /**
     * Display the specified Cave.
     *
     * @param int $id
     *
     * @return Response
     */
    public function show($id)
    {
        $cave = $this->caveRepository->find($id);

        if (empty($cave)) {
            Flash::error('Cave not found');

            return redirect(route('caves.index'));
        }

        return view('caves.show')->with('cave', $cave);
    }

    /**
     * Show the form for editing the specified Cave.
     *
     * @param int $id
     *
     * @return Response
     */
    public function edit($id)
    {
        $cave = $this->caveRepository->find($id);

        if (empty($cave)) {
            Flash::error('Cave not found');

            return redirect(route('caves.index'));
        }

        return view('caves.edit')->with('cave', $cave);
    }

    /**
     * Update the specified Cave in storage.
     *
     * @param int $id
     * @param UpdateCaveRequest $request
     *
     * @return Response
     */
    public function update($id, UpdateCaveRequest $request)
    {
        $cave = $this->caveRepository->find($id);

        if (empty($cave)) {
            Flash::error('Cave not found');

            return redirect(route('caves.index'));
        }

        $cave = $this->caveRepository->update($request->all(), $id);

        Flash::success('Cave updated successfully.');

        return redirect(route('caves.index'));
    }

    /**
     * Remove the specified Cave from storage.
     *
     * @param int $id
     *
     * @throws \Exception
     *
     * @return Response
     */
    public function destroy($id)
    {
        $cave = $this->caveRepository->find($id);

        if (empty($cave)) {
            Flash::error('Cave not found');

            return redirect(route('caves.index'));
        }

        $this->caveRepository->delete($id);

        Flash::success('Cave deleted successfully.');

        return redirect(route('caves.index'));
    }
}
