<?php

namespace App\Http\Controllers\Api\Assets;

use App\Http\Controllers\Controller;
use App\Models\Asset;
use Illuminate\Database\Eloquent\ModelNotFoundException;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Storage;
use Intervention\Image\Facades\Image;

class RenderFileController extends Controller
{
    protected $model;

    public function __construct(Asset $model)
    {
        $this->model = $model;
    }

    public function show($uuid, Request $request)
    {
        try {
            $model = $this->model->byUuid($uuid)->firstOrFail();

            return $this->renderFile($model, $request->get('width', null), $request->get('height', null));
        } catch (ModelNotFoundException $e) {
            return $this->renderPlaceholder($request->get('width', null), $request->get('height', null));
        }
    }

    public function renderFile($model, $width, $height)
    {
        $image = $this->makeFromPath($width, $height, $model->path);

        return $image->response();
    }

    public function renderPlaceholder($width, $height)
    {
        $image = Image::cache(function ($image) use ($width, $height) {
            $img = $image->canvas(800, 800, '#FFFFFF');
            $this->resize($img, $width, $height);

            return $img;
        }, 10, true);

        return $image->response();
    }

    protected function resize($img, $width, $height)
    {
        if (! empty($width) && ! empty($height)) {
            $img->resize($width, $height);
        } elseif (! empty($width)) {
            $img->resize($width, null, function ($constraint) {
                $constraint->aspectRatio();
            });
        } elseif (! empty($height)) {
            $img->resize(null, $height, function ($constraint) {
                $constraint->aspectRatio();
            });
        }

        return $img;
    }

    protected function makeFromPath($width, $height, $path)
    {
        return Image::cache(function ($image) use ($path, $width, $height) {
            $img = $image->make(Storage::get($path));
            $this->resize($img, $width, $height);

            return $img;
        }, 10, true);
    }
}
